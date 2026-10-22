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
        public int Restaurados { get; set; }
        /// <summary>Nome que o projeto mostra para o GUID no final.</summary>
        public string NomeNoProjeto { get; set; }
    }

    /// <summary>
    /// Renomeia um parâmetro compartilhado (mesmo GUID) em todas as famílias carregadas no projeto,
    /// inclusive aninhadas: abre cada família, renomeia (ReplaceParameter, que mantém valores e fórmulas),
    /// trata as aninhadas e as recarrega na família mãe, e por fim recarrega no projeto mantendo os valores
    /// do projeto (guardados antes, conferidos e restaurados depois). Os .rfa da biblioteca não são alterados.
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
            List<Family> alvos = aninhadas ? Editaveis(doc) : ComParametro(doc, guid);
            foreach (Family f in alvos)
            {
                Document fd = null;
                try
                {
                    fd = doc.EditFamily(f);
                    r.Abertas++;
                    if (!Processar(app, fd, guid, novoNome, aninhadas, f.Name, r)) continue;

                    List<FormulaFamilia.ValorGuardado> antes = FormulaFamilia.Guardar(doc, f, null);
                    fd.LoadFamily(doc, new FormulaFamilia.CarregarMantendoValores());
                    var (restaurados, falhas) = FormulaFamilia.Restaurar(doc, antes);
                    r.Restaurados += restaurados;
                    if (falhas.Count > 0)
                        r.Falhas.Add($"{f.Name}: recarregada, mas {falhas.Count} valores não voltaram: " + string.Join(", ", falhas.Take(5)));
                }
                catch (Exception ex)
                {
                    r.Falhas.Add($"{f.Name}: {ex.Message}");
                }
                finally
                {
                    try { fd?.Close(false); } catch { }
                }
            }
            r.Avisos.AddRange(NaoEditaveis(doc, guid).Select(n => "Não foi possível abrir: " + n));
            r.NomeNoProjeto = SharedParameterElement.Lookup(doc, guid)?.GetDefinition()?.Name;
            return r;
        }

        /// <summary>
        /// Renomeia no documento da família e, se pedido, nas aninhadas (recarregando-as nele).
        /// O parâmetro da própria família é renomeado antes de recarregar as aninhadas, para os nomes
        /// do mesmo GUID não ficarem diferentes dentro do arquivo. Retorna true se algo mudou.
        /// </summary>
        private static bool Processar(Application app, Document fd, Guid guid, string novoNome, bool aninhadas, string caminho, ResultadoFamilias r)
        {
            bool mudou = false;
            FamilyParameter p = fd.FamilyManager.GetParameters().FirstOrDefault(x => x.IsShared && x.GUID == guid);
            if (p != null && p.Definition.Name != novoNome)
            {
                SharedParameterRenamer.RenameInFamily(app, fd, p, novoNome);
                r.Renomeadas.Add(caminho);
                mudou = true;
            }
            if (!aninhadas) return mudou;

            foreach (Family nf in new FilteredElementCollector(fd).OfClass(typeof(Family)).Cast<Family>().Where(Editavel).ToList())
            {
                Document nd = null;
                string sub = caminho + " › " + nf.Name;
                try
                {
                    nd = fd.EditFamily(nf);
                    r.Abertas++;
                    if (!Processar(app, nd, guid, novoNome, true, sub, r)) continue;
                    nd.LoadFamily(fd, new FormulaFamilia.CarregarMantendoValores());
                    mudou = true;
                }
                catch (Exception ex)
                {
                    r.Falhas.Add($"{sub}: {ex.Message}");
                }
                finally
                {
                    try { nd?.Close(false); } catch { }
                }
            }
            return mudou;
        }

        /// <summary>
        /// Se o projeto ainda mostrar o nome antigo para o GUID (parâmetro que só existe nas famílias),
        /// tenta renomear direto o elemento do parâmetro no projeto.
        /// </summary>
        public static bool AjustarNomeNoProjeto(Document doc, Guid guid, string novoNome)
        {
            SharedParameterElement spe = SharedParameterElement.Lookup(doc, guid);
            if (spe == null || spe.GetDefinition().Name == novoNome) return true;
            using (var t = new Transaction(doc, "MetroSP: nome do parâmetro no projeto"))
            {
                try
                {
                    t.Start();
                    spe.Name = novoNome;
                    t.Commit();
                }
                catch
                {
                    if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                }
            }
            return SharedParameterElement.Lookup(doc, guid)?.GetDefinition()?.Name == novoNome;
        }
    }
}
