using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace MetroSP.Parametros
{
    /// <summary>
    /// Descobre se cada parâmetro está em uso no arquivo e deleta os que não estão.
    ///
    /// No projeto, um parâmetro de projeto está em uso quando algum elemento das categorias vinculadas tem valor,
    /// quando é campo de alguma tabela ou quando é regra de algum filtro. Os que existem só por causa de famílias
    /// ou etiquetas carregadas nunca são deletados daqui (o parâmetro mora nas famílias).
    /// Deletar remove o vínculo de projeto, como o botão Remover do diálogo Parâmetros de projeto; o registro do
    /// GUID não é apagado porque alguma família carregada pode usá-lo.
    ///
    /// Na família, está em uso quando algum tipo tem valor, quando tem fórmula ou aparece na fórmula de outro
    /// parâmetro, quando está associado a parâmetros de elementos (visibilidade, material, família aninhada...)
    /// ou é rótulo de cota. Em famílias de anotação (etiquetas, carimbos) os rótulos de texto não são visíveis
    /// pela API, então nada é deletado.
    /// </summary>
    public static class UsoParametros
    {
        public const string NaoVerificado = "(clique em Verificar uso)";
        public const string NaoUsado = "Não usado";

        public static void Analisar(Document doc, List<SharedParameterInfo> itens)
        {
            if (doc.IsFamilyDocument) AnalisarFamilia(doc, itens);
            else AnalisarProjeto(doc, itens);
        }

        // ------------------------------------------------------------------ projeto

        private static void AnalisarProjeto(Document doc, List<SharedParameterInfo> itens)
        {
            Ui.Progresso p = Ui.Progresso.Atual;
            p?.Etapa("Lendo tabelas e filtros");

            var tabelas = new Dictionary<ElementId, List<string>>();
            foreach (ViewSchedule v in new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>())
            {
                ScheduleDefinition def;
                try { def = v.Definition; } catch { continue; }
                for (int i = 0; i < def.GetFieldCount(); i++)
                {
                    ElementId id;
                    try { id = def.GetField(i).ParameterId; } catch { continue; }
                    Acrescentar(tabelas, id, v.Name);
                }
            }

            var filtros = new Dictionary<ElementId, List<string>>();
            foreach (ParameterFilterElement f in new FilteredElementCollector(doc).OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>())
            {
                ICollection<ElementId> ids;
                try { ids = f.GetElementFilterParameters(); } catch { continue; }
                foreach (ElementId id in ids) Acrescentar(filtros, id, f.Name);
            }

            List<SharedParameterInfo> vinculados = itens.Where(i => i.Vinculado && i.Definicao != null).ToList();
            p?.Etapa($"Procurando valores de {vinculados.Count} parâmetros de projeto", vinculados.Count);
            foreach (SharedParameterInfo i in itens)
            {
                if (!i.Vinculado)
                {
                    i.Uso = "Em famílias ou etiquetas carregadas (apague nelas)";
                    i.PodeDeletar = false;
                    continue;
                }
                p?.ParaSeCancelado();
                p?.Item(i.Nome);

                var usos = new List<string>();
                int comValor = ContarComValor(doc, i);
                if (comValor > 0) usos.Add($"preenchido em {comValor} {(i.InstanciaOuTipo == "Tipo" ? "tipos" : "elementos")}");
                if (i.Id != null && tabelas.TryGetValue(i.Id, out List<string> ts)) usos.Add(Resumo("tabela", ts));
                if (i.Id != null && filtros.TryGetValue(i.Id, out List<string> fs)) usos.Add(Resumo("filtro", fs));

                i.PodeDeletar = usos.Count == 0;
                i.Uso = i.PodeDeletar ? NaoUsado : "Em uso: " + string.Join(" · ", usos);
                p?.Avancar(i.Nome);
            }
        }

        /// <summary>Elementos (ou tipos, se o vínculo é de tipo) das categorias vinculadas com valor no parâmetro.</summary>
        private static int ContarComValor(Document doc, SharedParameterInfo i)
        {
            if (!(doc.ParameterBindings.get_Item(i.Definicao) is ElementBinding binding)) return 0;
            List<ElementId> cats = binding.Categories.Cast<Category>().Select(c => c.Id).ToList();
            if (cats.Count == 0) return 0;

            var coletor = new FilteredElementCollector(doc).WherePasses(new ElementMulticategoryFilter(cats));
            coletor = binding is TypeBinding ? coletor.WhereElementIsElementType() : coletor.WhereElementIsNotElementType();
            int n = 0;
            foreach (Element e in coletor)
            {
                Parameter par;
                try { par = i.Compartilhado ? e.get_Parameter(i.Guid) : e.get_Parameter(i.Definicao); }
                catch { continue; }
                if (TemValor(par)) n++;
            }
            return n;
        }

        private static bool TemValor(Parameter p)
        {
            if (p == null || !p.HasValue) return false;
            switch (p.StorageType)
            {
                case StorageType.String: return !string.IsNullOrEmpty(p.AsString());
                case StorageType.ElementId: return p.AsElementId() != ElementId.InvalidElementId;
                default: return true;
            }
        }

        // ------------------------------------------------------------------ família

        private static void AnalisarFamilia(Document doc, List<SharedParameterInfo> itens)
        {
            FamilyManager fm = doc.FamilyManager;
            Category cat = doc.OwnerFamily?.FamilyCategory;
            bool anotacao = cat != null && cat.CategoryType == CategoryType.Annotation;

            var cotas = new HashSet<ElementId>();
            foreach (Dimension d in new FilteredElementCollector(doc).OfClass(typeof(Dimension)).Cast<Dimension>())
            {
                try { if (d.FamilyLabel != null) cotas.Add(d.FamilyLabel.Id); } catch { /* cota sem rótulo */ }
            }

            List<FamilyParameter> todos = fm.GetParameters().ToList();
            List<FamilyType> tipos = fm.Types.Cast<FamilyType>().ToList();
            foreach (SharedParameterInfo i in itens)
            {
                FamilyParameter fp = i.ParametroFamilia;
                if (fp == null) { i.Uso = "(não encontrado)"; i.PodeDeletar = false; continue; }

                var usos = new List<string>();
                int comValor = tipos.Count(t => TemValor(t, fp));
                if (comValor > 0) usos.Add($"valor em {comValor} tipo(s)");
                if (!string.IsNullOrEmpty(fp.Formula)) usos.Add("tem fórmula");
                List<string> emFormulas = todos
                    .Where(o => o.Id != fp.Id && !string.IsNullOrEmpty(o.Formula) && o.Formula.IndexOf(fp.Definition.Name, StringComparison.Ordinal) >= 0)
                    .Select(o => o.Definition.Name).ToList();
                if (emFormulas.Count > 0) usos.Add(Resumo("na fórmula de", emFormulas));
                int associados = fp.AssociatedParameters?.Size ?? 0;
                if (associados > 0) usos.Add($"associado a {associados} parâmetro(s) de elementos");
                if (cotas.Contains(fp.Id)) usos.Add("rótulo de cota");

                if (usos.Count == 0 && anotacao)
                {
                    i.Uso = "Família de anotação: pode estar num rótulo (não deletado daqui)";
                    i.PodeDeletar = false;
                    continue;
                }
                i.PodeDeletar = usos.Count == 0;
                i.Uso = i.PodeDeletar ? NaoUsado : "Em uso: " + string.Join(" · ", usos);
            }
        }

        /// <summary>
        /// Valor que alguém preencheu. Números e Sim/Não zerados contam como vazios: é o que o Revit
        /// coloca quando o parâmetro é criado.
        /// </summary>
        private static bool TemValor(FamilyType t, FamilyParameter fp)
        {
            try
            {
                if (!t.HasValue(fp)) return false;
                switch (fp.StorageType)
                {
                    case StorageType.String: return !string.IsNullOrEmpty(t.AsString(fp));
                    case StorageType.Integer: return t.AsInteger(fp) != 0;
                    case StorageType.Double: return Math.Abs(t.AsDouble(fp) ?? 0) > 1e-9;
                    case StorageType.ElementId: return t.AsElementId(fp) != ElementId.InvalidElementId;
                    default: return true;
                }
            }
            catch { return true; }
        }

        // ------------------------------------------------------------------ deletar

        /// <summary>
        /// Deleta numa transação só (Ctrl+Z desfaz tudo). Só aceita os marcados como não usados.
        /// Retorna os que não puderam ser deletados, com o motivo.
        /// </summary>
        public static List<string> Deletar(Document doc, List<SharedParameterInfo> itens)
        {
            var falhas = new List<string>();
            using (var t = new Transaction(doc, $"MetroSP: deletar {itens.Count} parâmetro(s) sem uso"))
            {
                t.Start();
                foreach (SharedParameterInfo i in itens)
                {
                    if (!i.PodeDeletar) { falhas.Add($"{i.Nome} (em uso)"); continue; }
                    try
                    {
                        if (doc.IsFamilyDocument)
                        {
                            doc.FamilyManager.RemoveParameter(i.ParametroFamilia);
                        }
                        else
                        {
                            if (!doc.ParameterBindings.Remove(i.Definicao)) { falhas.Add($"{i.Nome} (o Revit recusou remover o vínculo)"); continue; }
                            // Não compartilhado: só existe como parâmetro de projeto, então apaga o elemento também.
                            if (!i.Compartilhado && i.Id != null && doc.GetElement(i.Id) != null) doc.Delete(i.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        falhas.Add($"{i.Nome} ({ex.Message})");
                    }
                }
                if (falhas.Count == itens.Count) t.RollBack();
                else t.Commit();
            }
            return falhas;
        }

        // ------------------------------------------------------------------ util

        private static void Acrescentar(Dictionary<ElementId, List<string>> d, ElementId id, string nome)
        {
            if (id == null || id == ElementId.InvalidElementId) return;
            if (!d.TryGetValue(id, out List<string> l)) d[id] = l = new List<string>();
            if (!l.Contains(nome)) l.Add(nome);
        }

        private static string Resumo(string rotulo, List<string> nomes) =>
            nomes.Count == 1 ? $"{rotulo} \"{nomes[0]}\""
            : $"{rotulo} \"{nomes[0]}\" e mais {nomes.Count - 1}";
    }
}
