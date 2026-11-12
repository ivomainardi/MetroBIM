using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using MetroSP.Classificacao;
using MetroSP.Ui;

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
    /// Cada documento (projeto ou família) guarda um único registro (SharedParameterElement) por GUID, e o
    /// nome mostrado é o desse registro. O Revit só deixa apagar o registro do projeto quando nenhuma família
    /// carregada usa mais o GUID. Por isso, numa única operação (desfeita inteira se o nome final não for o novo):
    ///   1. em cada família (e nas aninhadas, de dentro para fora) o compartilhado vira parâmetro de família,
    ///      com valores e fórmulas, e o registro do GUID na família é apagado;
    ///   2. guarda os valores do parâmetro em todos os elementos do projeto, o vínculo e os campos das tabelas;
    ///   3. recarrega as famílias assim: o projeto deixa de usar o GUID;
    ///   4. apaga o registro do GUID no projeto;
    ///   5. volta o parâmetro a compartilhado com o nome novo em cada família e recarrega de novo;
    ///   6. refaz o vínculo do projeto, restaura valores e tabelas e confere o nome.
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

        /// <summary>
        /// Uma família preparada (parâmetro já passado a parâmetro de família), guardada num .rfa temporário.
        /// Não fica aberta: o Revit não abre duas vezes a mesma família (ex.: uma aninhada compartilhada que
        /// também está no projeto), o que dava "Loaded Family Editing failed".
        /// </summary>
        private class No
        {
            public string Caminho;
            public string Arquivo;
            public SharedParameterRenamer.DadosParametro Dados;
            public string NomeTemp;
            public List<No> Filhas = new List<No>();
        }

        /// <summary>Salva a família num .rfa temporário com o mesmo nome (o nome do arquivo vira o nome da família) e fecha.</summary>
        private static void Guardar(No no, Document fd, string nomeFamilia, string pastaTemp)
        {
            try
            {
                string pasta = Path.Combine(pastaTemp, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(pasta);
                string nome = nomeFamilia + ".rfa";
                if (nome.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new InvalidOperationException($"o nome \"{nomeFamilia}\" não pode ser usado como nome de arquivo.");
                no.Arquivo = Path.Combine(pasta, nome);
                fd.SaveAs(no.Arquivo, new SaveAsOptions { OverwriteExistingFile = true });
            }
            finally
            {
                try { fd.Close(false); } catch { }
            }
        }

        private static Document Abrir(Application app, No no) =>
            app.OpenDocumentFile(no.Arquivo) ?? throw new InvalidOperationException($"{no.Caminho}: não foi possível reabrir a família.");

        public static ResultadoFamilias Executar(Application app, Document doc, Guid guid, string novoNome, bool aninhadas, Progresso progresso = null)
        {
            var r = new ResultadoFamilias();
            var nos = new List<No>();
            string pastaTemp = Path.Combine(Path.GetTempPath(), "MetroSP_renomear_" + Guid.NewGuid().ToString("N"));
            using (var tg = new TransactionGroup(doc, "MetroSP: renomear parâmetro compartilhado"))
            {
                tg.Start();
                try
                {
                    SharedParameterElement spe = SharedParameterElement.Lookup(doc, guid);
                    if (spe == null) throw new InvalidOperationException("O parâmetro não foi encontrado no projeto.");

                    // 1. Abrir as famílias e passar o parâmetro a parâmetro de família. Ficam abertas até o fim.
                    List<Family> familias = aninhadas ? Editaveis(doc) : ComParametro(doc, guid);
                    progresso?.Etapa($"1/5  Preparando as famílias ({familias.Count})", familias.Count);
                    foreach (Family f in familias)
                    {
                        progresso?.ParaSeCancelado();
                        progresso?.Item("Abrindo " + f.Name);
                        Document fd = null;
                        try
                        {
                            fd = doc.EditFamily(f);
                            r.Abertas++;
                            No no = Preparar(fd, f.Name, guid, aninhadas, r, progresso, pastaTemp);
                            if (no != null) { Guardar(no, fd, f.Name, pastaTemp); fd = null; nos.Add(no); }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex) { r.Falhas.Add($"{f.Name}: {ex.Message}"); progresso?.Log($"Falha em {f.Name}: {ex.Message}"); }
                        finally { try { fd?.Close(false); } catch { } }
                        progresso?.Avancar();
                    }
                    progresso?.ParaSeCancelado();

                    // 2. Guardar o que se perde enquanto o GUID sai do projeto.
                    progresso?.Etapa("2/5  Guardando valores, vínculo e tabelas do projeto");
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

                    // 3. Recarregar as famílias sem o GUID: o projeto deixa de usá-lo.
                    progresso?.Etapa($"3/5  Recarregando as famílias sem o parâmetro ({nos.Count})", nos.Count);
                    foreach (No no in nos)
                    {
                        progresso?.ParaSeCancelado();
                        progresso?.Item("Recarregando " + no.Caminho);
                        Document d = Abrir(app, no);
                        try { d.LoadFamily(doc, new FormulaFamilia.CarregarMantendoValores()); }
                        finally { d.Close(false); }
                        progresso?.Avancar();
                    }

                    // 4. Apagar o registro do GUID no projeto.
                    progresso?.Etapa("4/5  Liberando o nome antigo no projeto");
                    using (var t = new Transaction(doc, "MetroSP: liberar o nome do parâmetro"))
                    {
                        t.Start();
                        bool liberado = SharedParameterRenamer.ApagarRegistroDoGuid(doc, guid, out string motivo);
                        t.Commit();
                        if (!liberado)
                        {
                            var quem = new List<string>();
                            quem.AddRange(NaoEditaveis(doc, guid));
                            quem.AddRange(new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                                .Where(x => x.get_Parameter(guid) != null && x.Family != null).Select(x => x.Family.Name).Distinct());
                            throw new InvalidOperationException(
                                "O Revit não deixou liberar o nome antigo no projeto" + (motivo != null ? $" ({motivo})" : "") +
                                (quem.Count > 0
                                    ? ". Estas famílias ainda usam o parâmetro: " + string.Join(", ", quem.Distinct().Take(10)) +
                                      (aninhadas ? "" : ". Tente com \"Incluir famílias aninhadas\" marcado")
                                    : r.Falhas.Count > 0 ? ". Provavelmente por causa das famílias que não puderam ser abertas (abaixo)"
                                    : aninhadas ? "" : ". Tente com \"Incluir famílias aninhadas\" marcado: o parâmetro pode estar numa aninhada") + ".");
                        }
                    }

                    // 5. Voltar a compartilhado com o nome novo e recarregar.
                    progresso?.Etapa($"5/5  Renomeando e recarregando as famílias ({nos.Count})", nos.Count);
                    foreach (No no in nos)
                    {
                        progresso?.Item("Renomeando " + no.Caminho);
                        int antes = r.Renomeadas.Count;
                        Document d = Finalizar(app, no, novoNome, r);
                        try { d.LoadFamily(doc, new FormulaFamilia.CarregarMantendoValores()); }
                        finally { d.Close(false); }
                        r.Recarregadas++;
                        for (int i = antes; i < r.Renomeadas.Count; i++) progresso?.Log("Renomeado em " + r.Renomeadas[i]);
                        progresso?.Avancar();
                    }

                    progresso?.Etapa($"Refazendo o vínculo e restaurando {valores.Count} valores");
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

                        int falhas = 0;
                        foreach (var v in valores)
                        {
                            if (v.JaIgual(doc, guid)) continue;
                            if (v.Restore(doc, guid)) r.Restaurados++;
                            else falhas++;
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
                    r.Motivo = ex is OperationCanceledException ? "cancelado pelo usuário." : ex.Message;
                    progresso?.Etapa("Desfazendo...");
                    if (tg.HasStarted() && !tg.HasEnded()) tg.RollBack();
                }
                finally
                {
                    try { Directory.Delete(pastaTemp, true); } catch { }
                }
            }
            if (!r.Ok) r.NomeNoProjeto = SharedParameterElement.Lookup(doc, guid)?.GetDefinition()?.Name;
            r.Avisos.AddRange(NaoEditaveis(doc, guid).Select(n => "Não foi possível abrir: " + n));
            return r;
        }

        /// <summary>
        /// Passo 1, de dentro para fora: prepara as aninhadas que têm o GUID e as recarrega nesta família,
        /// passa o parâmetro da própria família a parâmetro de família (nome temporário) e apaga o registro
        /// do GUID. Retorna null se a família não tem o GUID (o documento continua com quem chamou).
        /// </summary>
        private static No Preparar(Document fd, string caminho, Guid guid, bool aninhadas, ResultadoFamilias r, Progresso progresso, string pastaTemp)
        {
            FamilyParameter p = fd.FamilyManager.GetParameters().FirstOrDefault(x => x.IsShared && x.GUID == guid);
            // Uma aninhada com o parâmetro deixa o GUID registrado na família mãe. Sem o registro,
            // nada abaixo dela tem o parâmetro: não precisa abrir as aninhadas.
            if (p == null && SharedParameterElement.Lookup(fd, guid) == null) return null;

            var no = new No { Caminho = caminho };
            if (aninhadas)
            {
                    foreach (Family nf in new FilteredElementCollector(fd).OfClass(typeof(Family)).Cast<Family>().Where(Editavel).ToList())
                    {
                        Document nd = null;
                        string sub = caminho + " › " + nf.Name;
                        try
                        {
                            progresso?.Item("Abrindo " + sub);
                            nd = fd.EditFamily(nf);
                            r.Abertas++;
                            No filha = Preparar(nd, sub, guid, true, r, progresso, pastaTemp);
                            if (filha != null)
                            {
                                // Recarrega já preparada nesta família e guarda para o passo 5.
                                nd.LoadFamily(fd, new FormulaFamilia.CarregarMantendoValores());
                                Guardar(filha, nd, nf.Name, pastaTemp);
                                nd = null;
                                no.Filhas.Add(filha);
                            }
                        }
                        catch (Exception ex) { r.Falhas.Add($"{sub}: {ex.Message}"); }
                        finally { try { nd?.Close(false); } catch { } }
                    }
            }

            using (var t = new Transaction(fd, "MetroSP: liberar o nome do parâmetro"))
            {
                t.Start();
                if (p != null)
                {
                    no.Dados = SharedParameterRenamer.ParaParametroDeFamilia(fd, p, out FamilyParameter familia);
                    no.NomeTemp = familia.Definition.Name;
                }
                SharedParameterRenamer.ApagarRegistroDoGuid(fd, guid);
                t.Commit();
            }
            return no;
        }

        /// <summary>
        /// Passo 5, de dentro para fora: reabre a família guardada, recarrega nela as aninhadas já renomeadas
        /// e volta o parâmetro a compartilhado com o nome novo. Devolve o documento aberto para quem chamou recarregar.
        /// </summary>
        private static Document Finalizar(Application app, No no, string novoNome, ResultadoFamilias r)
        {
            Document d = Abrir(app, no);
            try
            {
                foreach (No filha in no.Filhas)
                {
                    Document fd = Finalizar(app, filha, novoNome, r);
                    try { fd.LoadFamily(d, new FormulaFamilia.CarregarMantendoValores()); }
                    finally { fd.Close(false); }
                }
                if (no.Dados == null) return d;
                FamilyParameter familia = d.FamilyManager.get_Parameter(no.NomeTemp)
                    ?? throw new InvalidOperationException($"{no.Caminho}: parâmetro temporário não encontrado.");
                string final;
                using (var t = new Transaction(d, "MetroSP: renomear parâmetro compartilhado"))
                {
                    t.Start();
                    final = SharedParameterRenamer.ParaCompartilhado(app, d, familia, no.Dados, novoNome);
                    t.Commit();
                }
                if (final == novoNome) r.Renomeadas.Add(no.Caminho);
                else r.Falhas.Add($"{no.Caminho}: a família manteve o nome \"{final}\"");
                return d;
            }
            catch
            {
                d.Close(false);
                throw;
            }
        }
    }
}
