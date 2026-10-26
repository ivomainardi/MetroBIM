using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using MetroSP.Classificacao;

namespace MetroSP.Parametros
{
    /// <summary>Resultado de renomear um parâmetro compartilhado nas famílias carregadas no projeto.</summary>
    public class ResultadoFamilias
    {
        /// <summary>Famílias alteradas, com o caminho quando aninhada (ex.: "Porta › Maçaneta").</summary>
        public List<string> Renomeadas { get; } = new List<string>();
        public List<string> Falhas { get; } = new List<string>();
        public List<string> Avisos { get; } = new List<string>();
        public int Abertas { get; set; }
        public int Recarregadas { get; set; }
        public int Restaurados { get; set; }
        /// <summary>Nome que o projeto mostra para o GUID no final.</summary>
        public string NomeNoProjeto { get; set; }
        /// <summary>true se o projeto passou a mostrar o nome novo. Se false, tudo foi desfeito.</summary>
        public bool Ok { get; set; }
        /// <summary>Por que foi desfeito, quando Ok = false.</summary>
        public string Motivo { get; set; }
    }

    /// <summary>
    /// Renomeia um parâmetro compartilhado (mesmo GUID) no projeto e em todas as famílias carregadas,
    /// inclusive aninhadas.
    ///
    /// O projeto guarda um único registro (SharedParameterElement) por GUID, e o nome mostrado é o desse
    /// registro: recarregar famílias renomeadas não muda nada enquanto ele existir. Por isso, numa única
    /// operação (desfeita inteira se o nome final não for o novo):
    ///   1. renomeia dentro de cada família (de dentro para fora nas aninhadas), sem recarregar ainda;
    ///   2. guarda os valores do parâmetro em todos os elementos, o vínculo e os campos das tabelas;
    ///   3. apaga o registro do GUID no projeto;
    ///   4. recarrega as famílias (o GUID volta com o nome novo) e refaz o vínculo do projeto;
    ///   5. restaura valores e campos das tabelas e confere o nome.
    /// Os .rfa da biblioteca não são alterados.
    /// </summary>
    public static class RenomearEmFamilias
    {
        private static bool Editavel(Family f) => f != null && f.IsEditable && !f.IsInPlace;

        /// <summary>Famílias do primeiro nível que mostram o parâmetro no projeto (tipos ou instâncias).</summary>
        public static List<Family> ComParametro(Document doc, Guid guid)
        {
            var familias = new Dictionary<ElementId, Family>();
            foreach (FamilySymbol s in new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>())
                if (s.get_Parameter(guid) != null && Editavel(s.Family)) familias[s.Family.Id] = s.Family;
            foreach (FamilyInstance i in new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
            {
                Family f = i.Symbol?.Family;
                if (f != null && !familias.ContainsKey(f.Id) && i.get_Parameter(guid) != null && Editavel(f)) familias[f.Id] = f;
            }
            return familias.Values.OrderBy(f => f.Name).ToList();
        }

        /// <summary>Todas as famílias editáveis (para procurar também nas aninhadas, que não aparecem no projeto).</summary>
        public static List<Family> Editaveis(Document doc) =>
            new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().Where(Editavel).OrderBy(f => f.Name).ToList();

        /// <summary>Famílias que não podem ser abertas pela API (in-place ou protegidas), com o parâmetro.</summary>
        public static List<string> NaoEditaveis(Document doc, Guid guid) =>
            new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Where(s => s.get_Parameter(guid) != null && s.Family != null && !Editavel(s.Family))
                .Select(s => s.Family.Name + (s.Family.IsInPlace ? " (in-place)" : " (não editável)"))
                .Distinct().OrderBy(n => n).ToList();

        public static ResultadoFamilias Executar(Application app, Document doc, Guid guid, string novoNome, bool aninhadas)
        {
            var r = new ResultadoFamilias();
            var abertas = new List<(Family Familia, Document Doc)>();
            using (var tg = new TransactionGroup(doc, "MetroSP: renomear parâmetro compartilhado"))
            {
                tg.Start();
                try
                {
                    // 1. Renomear dentro das famílias. Ficam abertas até o passo 4.
                    foreach (Family f in aninhadas ? Editaveis(doc) : ComParametro(doc, guid))
                    {
                        Document fd = null;
                        try
                        {
                            fd = doc.EditFamily(f);
                            r.Abertas++;
                            if (Processar(app, fd, guid, novoNome, aninhadas, f.Name, r)) { abertas.Add((f, fd)); fd = null; }
                        }
                        catch (Exception ex) { r.Falhas.Add($"{f.Name}: {ex.Message}"); }
                        finally { try { fd?.Close(false); } catch { } }
                    }

                    // 2. Guardar o que se perde ao apagar o registro do GUID.
                    SharedParameterElement spe = SharedParameterElement.Lookup(doc, guid);
                    if (spe == null) throw new InvalidOperationException("O parâmetro não foi encontrado no projeto.");
                    if (spe.GetDefinition().Name == novoNome && abertas.Count == 0)
                    {
                        r.Ok = true;
                        r.NomeNoProjeto = novoNome;
                        tg.Assimilate();
                        return r;
                    }
                    InternalDefinition def = spe.GetDefinition();
                    var vinculo = doc.ParameterBindings.get_Item(def) as ElementBinding;
                    bool porTipo = vinculo is TypeBinding;
                    var categorias = new CategorySet();
                    if (vinculo != null) foreach (Category c in vinculo.Categories) categorias.Insert(c);
                    ForgeTypeId grupo = def.GetGroupTypeId();
                    ForgeTypeId tipoDado = def.GetDataType();
                    bool visivel = def.Visible;
                    bool variaEntreGrupos = def.VariesAcrossGroups;
                    string nomeAntigo = def.Name;

                    List<SharedParameterRenamer.SavedValue> valores =
                        SharedParameterRenamer.ElementsWithParameter(doc, guid, true)
                            .Concat(SharedParameterRenamer.ElementsWithParameter(doc, guid, false))
                            .Select(e => SharedParameterRenamer.SavedValue.From(e, e.get_Parameter(guid)))
                            .Where(v => v != null)
                            .ToList();

                    var campos = new List<(ViewSchedule View, int Index, string Titulo)>();
                    foreach (ViewSchedule vs in new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>())
                    {
                        if (vs.IsTemplate) continue;
                        ScheduleDefinition sd = vs.Definition;
                        IList<ScheduleFieldId> ordem = sd.GetFieldOrder();
                        for (int i = 0; i < ordem.Count; i++)
                        {
                            ScheduleField f = sd.GetField(ordem[i]);
                            if (f.ParameterId == spe.Id) campos.Add((vs, i, f.ColumnHeading));
                        }
                    }

                    // 3. Apagar o registro do GUID no projeto.
                    using (var t = new Transaction(doc, "MetroSP: liberar o nome do parâmetro"))
                    {
                        t.Start();
                        bool liberado = SharedParameterRenamer.ApagarRegistroDoGuid(doc, guid);
                        t.Commit();
                        if (!liberado)
                        {
                            List<string> travadas = NaoEditaveis(doc, guid);
                            throw new InvalidOperationException(
                                "O Revit não deixou liberar o nome antigo no projeto" +
                                (travadas.Count > 0 ? ", porque estas famílias não podem ser abertas: " + string.Join(", ", travadas) : "") + ".");
                        }
                    }

                    // 4. Recarregar as famílias e refazer o vínculo.
                    foreach (var (f, fd) in abertas)
                    {
                        try
                        {
                            fd.LoadFamily(doc, new FormulaFamilia.CarregarMantendoValores());
                            r.Recarregadas++;
                        }
                        catch (Exception ex) { r.Falhas.Add($"{f.Name}: não recarregou ({ex.Message})"); }
                    }

                    using (var t = new Transaction(doc, "MetroSP: refazer vínculo e valores"))
                    {
                        t.Start();
                        if (vinculo != null)
                        {
                            SharedParameterRenamer.WithTempDefinition(app, novoNome, tipoDado, guid, visivel, novaDefinicao =>
                            {
                                Binding novo = porTipo ? (Binding)app.Create.NewTypeBinding(categorias) : app.Create.NewInstanceBinding(categorias);
                                if (!doc.ParameterBindings.Insert(novaDefinicao, novo, grupo))
                                    throw new InvalidOperationException("O Revit recusou refazer o vínculo do parâmetro no projeto.");
                            });
                        }

                        SharedParameterElement final = SharedParameterElement.Lookup(doc, guid);
                        if (final != null && variaEntreGrupos)
                        {
                            try { final.GetDefinition().SetAllowVaryBetweenGroups(doc, true); }
                            catch { r.Avisos.Add("Não foi possível reativar \"Valores podem variar por instância de grupo\"."); }
                        }

                        // 5. Restaurar valores e tabelas.
                        int falhas = 0;
                        foreach (var v in valores)
                        {
                            if (v.Restore(doc, guid)) r.Restaurados++;
                            else if (!v.JaIgual(doc, guid)) falhas++;
                        }
                        if (falhas > 0) r.Avisos.Add($"{falhas} de {valores.Count} valores não puderam ser restaurados (elementos somente leitura ou em grupos).");

                        if (final != null)
                        {
                            foreach (var (view, index, titulo) in campos)
                            {
                                try
                                {
                                    ScheduleDefinition sd = view.Definition;
                                    if (sd.GetFieldOrder().Any(id => sd.GetField(id).ParameterId == final.Id)) continue;
                                    SchedulableField sf = sd.GetSchedulableFields().FirstOrDefault(x => x.ParameterId == final.Id);
                                    if (sf == null) { r.Avisos.Add($"Tabela \"{view.Name}\": campo não disponível para recriar."); continue; }
                                    ScheduleField campo = sd.InsertField(sf, Math.Min(index, sd.GetFieldCount()));
                                    if (!string.IsNullOrEmpty(titulo) && titulo != nomeAntigo) campo.ColumnHeading = titulo;
                                }
                                catch (Exception ex) { r.Avisos.Add($"Tabela \"{view.Name}\": {ex.Message}"); }
                            }
                        }
                        t.Commit();
                    }

                    r.NomeNoProjeto = SharedParameterElement.Lookup(doc, guid)?.GetDefinition()?.Name;
                    if (r.NomeNoProjeto == novoNome)
                    {
                        r.Ok = true;
                        tg.Assimilate();
                    }
                    else
                    {
                        r.Motivo = r.NomeNoProjeto == null
                            ? "o parâmetro não voltou ao projeto depois de recarregar as famílias."
                            : $"o projeto continuou mostrando \"{r.NomeNoProjeto}\".";
                        tg.RollBack();
                    }
                }
                catch (Exception ex)
                {
                    r.Ok = false;
                    r.Motivo = ex.Message;
                    if (tg.HasStarted() && !tg.HasEnded()) tg.RollBack();
                }
                finally
                {
                    foreach (var (_, fd) in abertas) try { fd.Close(false); } catch { }
                }
            }
            if (!r.Ok) r.NomeNoProjeto = SharedParameterElement.Lookup(doc, guid)?.GetDefinition()?.Name;
            r.Avisos.AddRange(NaoEditaveis(doc, guid).Select(n => "Não foi possível abrir: " + n));
            return r;
        }

        /// <summary>
        /// Renomeia no documento da família e, se pedido, nas aninhadas, de dentro para fora:
        /// o parâmetro da própria família vira parâmetro de família (liberando o nome antigo), as aninhadas
        /// renomeadas são recarregadas, e só então ele volta a compartilhado com o nome novo.
        /// Retorna true se a família tem o GUID (nela ou nas aninhadas) e deve ser recarregada.
        /// </summary>
        private static bool Processar(Application app, Document fd, Guid guid, string novoNome, bool aninhadas, string caminho, ResultadoFamilias r)
        {
            FamilyParameter p = fd.FamilyManager.GetParameters().FirstOrDefault(x => x.IsShared && x.GUID == guid);
            bool tem = p != null || SharedParameterElement.Lookup(fd, guid) != null;

            var filhas = new List<(string Caminho, Document Doc)>();
            try
            {
                if (aninhadas)
                {
                    foreach (Family nf in new FilteredElementCollector(fd).OfClass(typeof(Family)).Cast<Family>().Where(Editavel).ToList())
                    {
                        Document nd = null;
                        string sub = caminho + " › " + nf.Name;
                        try
                        {
                            nd = fd.EditFamily(nf);
                            r.Abertas++;
                            if (Processar(app, nd, guid, novoNome, true, sub, r)) { filhas.Add((sub, nd)); nd = null; }
                        }
                        catch (Exception ex) { r.Falhas.Add($"{sub}: {ex.Message}"); }
                        finally { try { nd?.Close(false); } catch { } }
                    }
                }
                if (!tem && filhas.Count == 0) return false;

                SharedParameterRenamer.DadosParametro dados = null;
                FamilyParameter familia = null;
                using (var t = new Transaction(fd, "MetroSP: liberar o nome do parâmetro"))
                {
                    t.Start();
                    if (p != null && p.Definition.Name != novoNome)
                        dados = SharedParameterRenamer.ParaParametroDeFamilia(fd, p, out familia);
                    else if (p == null)
                        SharedParameterRenamer.ApagarRegistroDoGuid(fd, guid);
                    t.Commit();
                }

                foreach (var (sub, nd) in filhas)
                {
                    try { nd.LoadFamily(fd, new FormulaFamilia.CarregarMantendoValores()); }
                    catch (Exception ex) { r.Falhas.Add($"{sub}: não recarregou ({ex.Message})"); }
                }

                if (dados != null)
                {
                    string final;
                    using (var t = new Transaction(fd, "MetroSP: renomear parâmetro compartilhado"))
                    {
                        t.Start();
                        final = SharedParameterRenamer.ParaCompartilhado(app, fd, familia, dados, novoNome);
                        t.Commit();
                    }
                    if (final == novoNome) r.Renomeadas.Add(caminho);
                    else r.Falhas.Add($"{caminho}: a família manteve o nome \"{final}\"");
                }
                return true;
            }
            finally
            {
                foreach (var (_, nd) in filhas) try { nd.Close(false); } catch { }
            }
        }
    }
}
