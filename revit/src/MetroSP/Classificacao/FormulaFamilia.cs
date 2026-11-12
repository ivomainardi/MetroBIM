using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace MetroSP.Classificacao
{
    /// <summary>
    /// Parâmetro de classificação calculado por fórmula dentro da família: o Revit trava o valor no projeto.
    /// Aqui a família é aberta (sem salvar) para ler a fórmula e, se o usuário pedir, a fórmula é removida
    /// e a família recarregada no projeto. O parâmetro e o GUID continuam os mesmos.
    /// </summary>
    public static class FormulaFamilia
    {
        // Família → fórmula (null = sem fórmula). Limpo a cada verificação.
        private static readonly Dictionary<long, string> _cache = new Dictionary<long, string>();

        public static void LimparCache() => _cache.Clear();

        /// <summary>Família editável do elemento (instância ou tipo), ou null.</summary>
        public static Family Familia(Document doc, Element e)
        {
            Family f = (e as FamilyInstance)?.Symbol?.Family ?? (e as FamilySymbol)?.Family;
            return f != null && f.IsEditable && !f.IsInPlace ? f : null;
        }

        /// <summary>A fórmula do parâmetro na família, ou null se não tem. Abre a família só para ler.</summary>
        public static string Formula(Document doc, Family f, string nome)
        {
            if (f == null) return null;
            if (_cache.TryGetValue(f.Id.Value, out string c)) return c;
            string formula = null;
            Document fam = null;
            try
            {
                Ui.Progresso.Atual?.Log("Lendo a família " + f.Name);
                fam = doc.EditFamily(f);
                FamilyParameter p = fam.FamilyManager.get_Parameter(nome);
                if (p != null && !string.IsNullOrWhiteSpace(p.Formula)) formula = p.Formula;
            }
            catch { /* não deu para abrir: sem diagnóstico */ }
            finally
            {
                try { fam?.Close(false); } catch { }
            }
            _cache[f.Id.Value] = formula;
            return formula;
        }

        internal class CarregarMantendoValores : IFamilyLoadOptions
        {
            // Mantém os valores do projeto (passam a ser editáveis, já com o último valor calculado).
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = false;
                return true;
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = false;
                return true;
            }
        }

        /// <summary>Valor de um parâmetro de um elemento, guardado antes de recarregar a família.</summary>
        internal class ValorGuardado
        {
            public ElementId Elemento;
            public ElementId ParametroId;
            public string Nome;
            public StorageType Tipo;
            public object Valor;
        }

        private static object Ler(Parameter p)
        {
            switch (p.StorageType)
            {
                case StorageType.String: return p.AsString();
                case StorageType.Integer: return p.AsInteger();
                case StorageType.Double: return p.AsDouble();
                case StorageType.ElementId: return p.AsElementId();
                default: return null;
            }
        }

        private static bool Igual(object a, object b) =>
            a is double x && b is double y ? Math.Abs(x - y) < 1e-9 : Equals(a ?? "", b ?? "");

        /// <summary>
        /// Guarda todos os valores editáveis dos tipos e instâncias da família, mais o valor atual (calculado)
        /// do parâmetro de classificação, para conferir e restaurar depois da recarga.
        /// </summary>
        internal static List<ValorGuardado> Guardar(Document doc, Family f, string nome)
        {
            var r = new List<ValorGuardado>();
            var tipos = f.GetFamilySymbolIds().Select(doc.GetElement).Where(x => x != null).ToList();
            var tiposIds = new HashSet<ElementId>(tipos.Select(t => t.Id));
            var instancias = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance))
                .Where(i => tiposIds.Contains(i.GetTypeId())).ToList();
            foreach (Element e in tipos.Concat(instancias))
                foreach (Parameter p in e.Parameters)
                {
                    if (p.StorageType == StorageType.None || !p.HasValue) continue;
                    bool classificacao = p.Definition?.Name == nome;
                    if (p.IsReadOnly && !classificacao) continue;
                    r.Add(new ValorGuardado { Elemento = e.Id, ParametroId = p.Id, Nome = p.Definition?.Name, Tipo = p.StorageType, Valor = Ler(p) });
                }
            return r;
        }

        /// <summary>Confere os valores depois da recarga e devolve os que mudaram. Retorna (restaurados, não restaurados).</summary>
        internal static (int Restaurados, List<string> Falhas) Restaurar(Document doc, List<ValorGuardado> guardados)
        {
            int n = 0;
            var falhas = new List<string>();
            using (var t = new Transaction(doc, "MetroSP: restaurar valores após recarregar família"))
            {
                t.Start();
                foreach (ValorGuardado g in guardados)
                {
                    Element e = doc.GetElement(g.Elemento);
                    Parameter p = e?.Parameters.Cast<Parameter>().FirstOrDefault(x => x.Id == g.ParametroId);
                    if (p == null || Igual(Ler(p), g.Valor)) continue;
                    try
                    {
                        bool ok = !p.IsReadOnly && (g.Valor is string s ? p.Set(s)
                            : g.Valor is int i ? p.Set(i)
                            : g.Valor is double d ? p.Set(d)
                            : g.Valor is ElementId id && p.Set(id));
                        if (ok) n++; else falhas.Add($"{g.Nome} (Id {g.Elemento.Value})");
                    }
                    catch { falhas.Add($"{g.Nome} (Id {g.Elemento.Value})"); }
                }
                t.Commit();
            }
            return (n, falhas);
        }

        /// <summary>
        /// Remove a fórmula do parâmetro em cada família e recarrega no projeto, sem perder dados:
        /// os valores de tipos e instâncias são guardados antes, conferidos depois e restaurados se algo mudou.
        /// O parâmetro de classificação fica com o valor que tinha (o último calculado), agora editável.
        /// Retorna (ok, falhas com motivo).
        /// </summary>
        public static (List<string> Ok, List<string> Falhas) Remover(Document doc, IEnumerable<Family> familias, string nome)
        {
            var ok = new List<string>();
            var falhas = new List<string>();
            List<Family> lista = familias.GroupBy(x => x.Id).Select(g => g.First()).ToList();
            Ui.Progresso progresso = Ui.Progresso.Atual;
            progresso?.Etapa($"Removendo a fórmula de {nome} e recarregando {lista.Count} famílias", lista.Count);
            foreach (Family f in lista)
            {
                if (progresso?.Cancelado == true)
                {
                    falhas.Add($"{f.Name}: não processada (cancelado)");
                    continue;
                }
                progresso?.Item(f.Name);
                Document fam = null;
                try
                {
                    fam = doc.EditFamily(f);
                    FamilyParameter p = fam.FamilyManager.get_Parameter(nome);
                    if (p == null) { falhas.Add($"{f.Name}: parâmetro não encontrado na família"); continue; }
                    using (var t = new Transaction(fam, "MetroSP: remover fórmula de " + nome))
                    {
                        t.Start();
                        fam.FamilyManager.SetFormula(p, null);
                        t.Commit();
                    }
                    List<ValorGuardado> antes = Guardar(doc, f, nome);
                    fam.LoadFamily(doc, new CarregarMantendoValores());
                    var (restaurados, naoRestaurados) = Restaurar(doc, antes);
                    ok.Add(f.Name + (restaurados > 0 ? $" ({restaurados} valores restaurados)" : ""));
                    if (naoRestaurados.Count > 0)
                        falhas.Add($"{f.Name}: recarregada, mas {naoRestaurados.Count} valores não voltaram: " + string.Join(", ", naoRestaurados.Take(5)));
                    _cache.Remove(f.Id.Value);
                }
                catch (Exception ex)
                {
                    falhas.Add($"{f.Name}: {ex.Message}");
                }
                finally
                {
                    try { fam?.Close(false); } catch { }
                }
                progresso?.Avancar();
                progresso?.Log(falhas.Count > 0 && falhas[falhas.Count - 1].StartsWith(f.Name + ":") ? "Falha: " + falhas[falhas.Count - 1] : "OK: " + f.Name);
            }
            return (ok, falhas);
        }
    }
}
