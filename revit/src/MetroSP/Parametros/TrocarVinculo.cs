using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace MetroSP.Parametros
{
    /// <summary>
    /// Troca o vínculo de um parâmetro entre instância e tipo, como a opção Instância/Tipo do diálogo
    /// Parâmetros de projeto, mas cuidando dos valores já preenchidos.
    ///
    /// No projeto, o vínculo é refeito com as mesmas categorias e o mesmo grupo. Os valores são lidos antes e
    /// regravados depois:
    /// - Instância → Tipo: cada tipo recebe o valor das suas instâncias. Quando as instâncias de um tipo têm valores
    ///   diferentes, só um sobra (o mais comum, ou nenhum, conforme a escolha do usuário). Elementos sem tipo
    ///   (ambientes, áreas...) perdem o valor.
    /// - Tipo → Instância: cada instância recebe o valor do seu tipo. Tipos sem nenhuma instância no modelo perdem o valor.
    /// Elementos de famílias que já trazem o parâmetro definido dentro da família não mudam: vale o que a família define.
    ///
    /// Na família, usa FamilyManager.MakeInstance/MakeType; os valores de cada tipo da família são mantidos.
    /// </summary>
    public static class TrocarVinculo
    {
        /// <summary>O que vai acontecer com um parâmetro, calculado antes de trocar.</summary>
        public class Plano
        {
            public SharedParameterInfo Item;
            public bool ParaTipo;
            /// <summary>Motivo para não trocar (ex.: só existe em famílias carregadas). null = pode trocar.</summary>
            public string Impedimento;

            // Projeto, Instância → Tipo.
            internal readonly Dictionary<ElementId, List<(ElementId Elemento, Valor Valor)>> PorTipo =
                new Dictionary<ElementId, List<(ElementId, Valor)>>();
            public int InstanciasComValor;
            /// <summary>Elementos sem tipo com valor: perdem o valor.</summary>
            public int SemTipoComValor;
            /// <summary>Tipos cujas instâncias têm valores diferentes.</summary>
            public List<string> Conflitos = new List<string>();
            /// <summary>Instâncias que perdem o valor se cada tipo ficar com o valor mais comum.</summary>
            public int PerdemComMaisComum;

            // Projeto, Tipo → Instância.
            internal readonly Dictionary<ElementId, Valor> ValorDoTipo = new Dictionary<ElementId, Valor>();
            public int TiposComValor;
            /// <summary>Tipos com valor sem nenhuma instância no modelo: perdem o valor.</summary>
            public int TiposSemInstancia;
            public int InstanciasQueRecebem;

            public string De => ParaTipo ? "Instância" : "Tipo";
            public string Para => ParaTipo ? "Tipo" : "Instância";

            /// <summary>Uma linha para a confirmação.</summary>
            public string Resumo(bool familia)
            {
                string s = $"{Item.Nome}: {De} → {Para}";
                if (Impedimento != null) return s + $"  (não será trocado: {Impedimento})";
                if (familia) return s;
                var partes = new List<string>();
                if (ParaTipo)
                {
                    if (InstanciasComValor == 0) partes.Add("nenhuma instância preenchida");
                    else partes.Add($"{InstanciasComValor} instância(s) preenchida(s) em {PorTipo.Count} tipo(s)");
                    if (Conflitos.Count > 0) partes.Add($"{Conflitos.Count} tipo(s) com valores diferentes entre as instâncias");
                    if (SemTipoComValor > 0) partes.Add($"{SemTipoComValor} elemento(s) sem tipo perdem o valor");
                }
                else
                {
                    if (TiposComValor == 0) partes.Add("nenhum tipo preenchido");
                    else partes.Add($"{TiposComValor} tipo(s) preenchido(s), o valor passa para {InstanciasQueRecebem} instância(s)");
                    if (TiposSemInstancia > 0) partes.Add($"{TiposSemInstancia} tipo(s) sem instância no modelo perdem o valor");
                }
                return s + "  (" + string.Join("; ", partes) + ")";
            }

            public bool PerdeValores => Impedimento == null && (Conflitos.Count > 0 || SemTipoComValor > 0 || TiposSemInstancia > 0);
        }

        /// <summary>Resultado da troca de um parâmetro.</summary>
        public class Resultado
        {
            public string Nome;
            public bool Trocado;
            public string Erro;
            public int ValoresGravados;
            public int ValoresNaoGravados;
            /// <summary>Elementos em que o parâmetro vem da família: ficaram como a família define.</summary>
            public int DefinidosNaFamilia;
            public int ValoresPerdidos;
        }

        /// <summary>Valor bruto de um parâmetro, para comparar e regravar.</summary>
        internal struct Valor : IEquatable<Valor>
        {
            public StorageType Tipo;
            public double D;
            public int I;
            public string S;
            public ElementId Id;

            public static bool Ler(Parameter p, out Valor v)
            {
                v = default;
                if (p == null || !p.HasValue) return false;
                v.Tipo = p.StorageType;
                switch (p.StorageType)
                {
                    case StorageType.String:
                        v.S = p.AsString();
                        return !string.IsNullOrEmpty(v.S);
                    case StorageType.Integer:
                        v.I = p.AsInteger();
                        return true;
                    case StorageType.Double:
                        v.D = p.AsDouble();
                        return true;
                    case StorageType.ElementId:
                        v.Id = p.AsElementId();
                        return v.Id != null && v.Id != ElementId.InvalidElementId;
                    default:
                        return false;
                }
            }

            public bool Gravar(Parameter p)
            {
                if (p == null || p.IsReadOnly || p.StorageType != Tipo) return false;
                switch (Tipo)
                {
                    case StorageType.String: return p.Set(S);
                    case StorageType.Integer: return p.Set(I);
                    case StorageType.Double: return p.Set(D);
                    case StorageType.ElementId: return p.Set(Id);
                    default: return false;
                }
            }

            public bool Equals(Valor o)
            {
                if (Tipo != o.Tipo) return false;
                switch (Tipo)
                {
                    case StorageType.String: return string.Equals(S, o.S, StringComparison.Ordinal);
                    case StorageType.Integer: return I == o.I;
                    case StorageType.Double: return Math.Abs(D - o.D) < 1e-9;
                    case StorageType.ElementId: return Id == o.Id;
                    default: return true;
                }
            }

            public override bool Equals(object obj) => obj is Valor v && Equals(v);

            public override int GetHashCode()
            {
                switch (Tipo)
                {
                    case StorageType.String: return S?.GetHashCode() ?? 0;
                    case StorageType.Integer: return I;
                    case StorageType.Double: return Math.Round(D, 6).GetHashCode();
                    case StorageType.ElementId: return Id?.GetHashCode() ?? 0;
                    default: return 0;
                }
            }
        }

        // ------------------------------------------------------------------ análise

        /// <summary>Lê os valores atuais e diz o que acontece com cada parâmetro. Não altera o modelo.</summary>
        public static List<Plano> Analisar(Document doc, List<SharedParameterInfo> itens)
        {
            var planos = new List<Plano>();
            foreach (SharedParameterInfo i in itens)
            {
                var p = new Plano { Item = i, ParaTipo = i.InstanciaOuTipo == "Instância" };
                if (doc.IsFamilyDocument)
                {
                    if (i.ParametroFamilia == null) p.Impedimento = "não encontrado na família";
                    else p.ParaTipo = i.ParametroFamilia.IsInstance;
                }
                else if (!i.Vinculado || i.Definicao == null)
                {
                    p.Impedimento = "existe só em famílias ou etiquetas carregadas; troque na família";
                }
                else if (!(doc.ParameterBindings.get_Item(i.Definicao) is ElementBinding binding))
                {
                    p.Impedimento = "vínculo de projeto não encontrado";
                }
                else
                {
                    p.ParaTipo = binding is InstanceBinding;
                    if (p.ParaTipo) LerInstancias(doc, binding, p);
                    else LerTipos(doc, binding, p);
                }
                planos.Add(p);
            }
            return planos;
        }

        private static Parameter Par(Element e, SharedParameterInfo i)
        {
            try { return i.Compartilhado ? e.get_Parameter(i.Guid) : e.get_Parameter(i.Definicao); }
            catch { return null; }
        }

        private static FilteredElementCollector Coletor(Document doc, ElementBinding binding)
        {
            List<ElementId> cats = binding.Categories.Cast<Category>().Select(c => c.Id).ToList();
            if (cats.Count == 0) return null;
            return new FilteredElementCollector(doc).WherePasses(new ElementMulticategoryFilter(cats));
        }

        private static void LerInstancias(Document doc, ElementBinding binding, Plano p)
        {
            FilteredElementCollector c = Coletor(doc, binding);
            if (c == null) return;
            foreach (Element e in c.WhereElementIsNotElementType())
            {
                if (!Valor.Ler(Par(e, p.Item), out Valor v)) continue;
                p.InstanciasComValor++;
                ElementId tipo = e.GetTypeId();
                if (tipo == null || tipo == ElementId.InvalidElementId) { p.SemTipoComValor++; continue; }
                if (!p.PorTipo.TryGetValue(tipo, out var lista)) p.PorTipo[tipo] = lista = new List<(ElementId, Valor)>();
                lista.Add((e.Id, v));
            }
            foreach (var par in p.PorTipo)
            {
                var grupos = par.Value.GroupBy(x => x.Valor).OrderByDescending(g => g.Count()).ToList();
                if (grupos.Count <= 1) continue;
                p.PerdemComMaisComum += par.Value.Count - grupos[0].Count();
                p.Conflitos.Add($"{p.Item.Nome} · {NomeDoTipo(doc, par.Key)}: {grupos.Count} valores diferentes em {par.Value.Count} instâncias");
            }
        }

        private static void LerTipos(Document doc, ElementBinding binding, Plano p)
        {
            FilteredElementCollector tipos = Coletor(doc, binding);
            if (tipos == null) return;
            foreach (Element t in tipos.WhereElementIsElementType())
            {
                if (Valor.Ler(Par(t, p.Item), out Valor v)) p.ValorDoTipo[t.Id] = v;
            }
            p.TiposComValor = p.ValorDoTipo.Count;
            if (p.TiposComValor == 0) return;

            var comInstancia = new HashSet<ElementId>();
            foreach (Element e in Coletor(doc, binding).WhereElementIsNotElementType())
            {
                ElementId tipo = e.GetTypeId();
                if (tipo == null || !p.ValorDoTipo.ContainsKey(tipo)) continue;
                comInstancia.Add(tipo);
                p.InstanciasQueRecebem++;
            }
            p.TiposSemInstancia = p.ValorDoTipo.Keys.Count(k => !comInstancia.Contains(k));
        }

        private static string NomeDoTipo(Document doc, ElementId id)
        {
            Element t = doc.GetElement(id);
            if (t == null) return "(tipo)";
            string familia = (t as ElementType)?.FamilyName;
            return string.IsNullOrEmpty(familia) ? t.Name : $"{familia}: {t.Name}";
        }

        // ------------------------------------------------------------------ troca

        /// <summary>
        /// Troca o vínculo e regrava os valores, numa transação só (Ctrl+Z desfaz tudo).
        /// maisComum: nos tipos com valores diferentes entre as instâncias, grava o valor mais comum (true) ou deixa vazio (false).
        /// </summary>
        public static List<Resultado> Aplicar(Document doc, List<Plano> planos, bool maisComum)
        {
            var resultados = new List<Resultado>();
            string titulo = planos.Count == 1
                ? $"MetroSP: {planos[0].Item.Nome} de {planos[0].De.ToLowerInvariant()} para {planos[0].Para.ToLowerInvariant()}"
                : $"MetroSP: trocar instância/tipo de {planos.Count} parâmetros";
            using (var t = new Transaction(doc, titulo))
            {
                FailureHandlingOptions opcoes = t.GetFailureHandlingOptions();
                opcoes.SetFailuresPreprocessor(new SoAvisos());
                t.SetFailureHandlingOptions(opcoes);
                t.Start();
                foreach (Plano p in planos.Where(x => x.Impedimento == null))
                {
                    var r = new Resultado { Nome = p.Item.Nome };
                    resultados.Add(r);
                    using (var sub = new SubTransaction(doc))
                    {
                        sub.Start();
                        try
                        {
                            if (doc.IsFamilyDocument) TrocarNaFamilia(doc, p, r);
                            else TrocarNoProjeto(doc, p, r, maisComum);
                        }
                        catch (Exception ex)
                        {
                            r.Trocado = false;
                            r.Erro = ex.Message;
                        }
                        if (r.Trocado) sub.Commit();
                        else sub.RollBack();
                    }
                }
                if (resultados.Any(r => r.Trocado)) t.Commit();
                else t.RollBack();
            }
            return resultados;
        }

        private static void TrocarNaFamilia(Document doc, Plano p, Resultado r)
        {
            FamilyManager fm = doc.FamilyManager;
            FamilyParameter fp = p.Item.ParametroFamilia;
            if (p.ParaTipo) fm.MakeType(fp);
            else fm.MakeInstance(fp);
            r.Trocado = true;
        }

        private static void TrocarNoProjeto(Document doc, Plano p, Resultado r, bool maisComum)
        {
            Definition def = p.Item.Definicao;
            if (!(doc.ParameterBindings.get_Item(def) is ElementBinding atual)) { r.Erro = "vínculo de projeto não encontrado"; return; }

            CategorySet cats = doc.Application.Create.NewCategorySet();
            foreach (Category c in atual.Categories) cats.Insert(c);
            ElementBinding novo = p.ParaTipo
                ? (ElementBinding)doc.Application.Create.NewTypeBinding(cats)
                : doc.Application.Create.NewInstanceBinding(cats);

            ForgeTypeId grupo = def.GetGroupTypeId();
            if (!doc.ParameterBindings.ReInsert(def, novo, grupo))
            {
                r.Erro = "o Revit recusou refazer o vínculo";
                return;
            }
            bool trocou = p.ParaTipo
                ? doc.ParameterBindings.get_Item(def) is TypeBinding
                : doc.ParameterBindings.get_Item(def) is InstanceBinding;
            if (!trocou)
            {
                r.Erro = "o Revit manteve o vínculo anterior";
                return;
            }
            r.Trocado = true;
            doc.Regenerate();

            if (p.ParaTipo) RegravarNosTipos(doc, p, r, maisComum);
            else RegravarNasInstancias(doc, p, r);
        }

        private static void RegravarNosTipos(Document doc, Plano p, Resultado r, bool maisComum)
        {
            r.ValoresPerdidos = p.SemTipoComValor;
            foreach (var par in p.PorTipo)
            {
                List<(ElementId Elemento, Valor Valor)> doTipo = par.Value;

                // Instâncias que continuam com o parâmetro de instância: ele vem da família, que manda nelas.
                var daFamilia = new HashSet<ElementId>();
                foreach (var x in doTipo)
                {
                    Element e = doc.GetElement(x.Elemento);
                    Parameter ainda = e == null ? null : Par(e, p.Item);
                    if (ainda != null && Valor.Ler(ainda, out Valor v) && v.Equals(x.Valor)) daFamilia.Add(x.Elemento);
                }
                Element tipo = doc.GetElement(par.Key);
                Parameter alvo = tipo == null ? null : Par(tipo, p.Item);
                if (alvo == null || daFamilia.Count == doTipo.Count)
                {
                    r.DefinidosNaFamilia += daFamilia.Count;
                    if (daFamilia.Count < doTipo.Count) { r.ValoresNaoGravados++; r.ValoresPerdidos += doTipo.Count - daFamilia.Count; }
                    continue;
                }
                r.DefinidosNaFamilia += daFamilia.Count;

                var restantes = doTipo.Where(x => !daFamilia.Contains(x.Elemento)).ToList();
                var grupos = restantes.GroupBy(x => x.Valor).OrderByDescending(g => g.Count()).ToList();
                if (grupos.Count > 1 && !maisComum)
                {
                    r.ValoresPerdidos += restantes.Count;
                    continue;
                }
                if (grupos.Count > 1) r.ValoresPerdidos += restantes.Count - grupos[0].Count();
                try
                {
                    if (grupos[0].Key.Gravar(alvo)) r.ValoresGravados++;
                    else { r.ValoresNaoGravados++; r.ValoresPerdidos += grupos[0].Count(); }
                }
                catch { r.ValoresNaoGravados++; r.ValoresPerdidos += grupos[0].Count(); }
            }
        }

        private static void RegravarNasInstancias(Document doc, Plano p, Resultado r)
        {
            if (p.ValorDoTipo.Count == 0) return;
            r.ValoresPerdidos = p.TiposSemInstancia;
            var gravouEmAlgum = new HashSet<ElementId>();
            if (!(doc.ParameterBindings.get_Item(p.Item.Definicao) is ElementBinding binding)) return;
            foreach (Element e in Coletor(doc, binding).WhereElementIsNotElementType())
            {
                ElementId tipo = e.GetTypeId();
                if (tipo == null || !p.ValorDoTipo.TryGetValue(tipo, out Valor v)) continue;
                Element t = doc.GetElement(tipo);
                Parameter doTipo = t == null ? null : Par(t, p.Item);
                if (doTipo != null && Valor.Ler(doTipo, out Valor ainda) && ainda.Equals(v))
                {
                    // O tipo continua com o parâmetro de tipo: ele vem da família.
                    r.DefinidosNaFamilia++;
                    gravouEmAlgum.Add(tipo);
                    continue;
                }
                Parameter alvo = Par(e, p.Item);
                try
                {
                    if (v.Gravar(alvo)) { r.ValoresGravados++; gravouEmAlgum.Add(tipo); }
                    else r.ValoresNaoGravados++;
                }
                catch { r.ValoresNaoGravados++; }
            }
            // Tipos com instâncias em que nenhuma recebeu o valor também perderam.
            r.ValoresPerdidos += p.ValorDoTipo.Keys.Count(k => !gravouEmAlgum.Contains(k)) - p.TiposSemInstancia;
        }

        /// <summary>Some com os avisos (ex.: elementos em grupos) para não interromper a troca; erros continuam aparecendo.</summary>
        private class SoAvisos : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
            {
                foreach (FailureMessageAccessor f in a.GetFailureMessages())
                    if (f.GetSeverity() == FailureSeverity.Warning) a.DeleteWarning(f);
                return FailureProcessingResult.Continue;
            }
        }
    }
}
