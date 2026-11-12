using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace MetroSP.Ids
{
    /// <summary>
    /// Mapeamento das categorias do Revit para classes IFC, uma tabela por esquema (IFC4 e IFC 4.3). As tabelas ficam
    /// em .txt (seu › Forma online › instalado, subpasta Categorias) e, na exportação, são gravadas no modelo como
    /// modelo de mapeamento do Revit ("MetroSP IFC4", "MetroSP IFC4x3"), que fica ativo durante a exportação.
    /// Formato do .txt (tab): Categoria, Subcategoria, Classe IFC, Tipo predefinido, Exportar (Sim/Não).
    /// </summary>
    public static class CategoriasIfc
    {
        public const string Sub = "Categorias";
        public const string Ifc4 = "IFC4", Ifc4x3 = "IFC4x3";

        public class Linha
        {
            public string Categoria { get; set; }
            public string Subcategoria { get; set; }
            public bool Exportar { get; set; } = true;
            public string Classe { get; set; }
            public string Tipo { get; set; }
            /// <summary>Elementos da categoria no modelo (só para mostrar).</summary>
            public int NoModelo { get; set; }
            /// <summary>Classe na tabela do outro esquema (só para mostrar).</summary>
            public string OutroEsquema { get; set; }
            public bool Diferente => OutroEsquema != null &&
                !string.Equals((Exportar ? Classe ?? "" : "não exportado") + "|" + (Tipo ?? ""), OutroEsquema, StringComparison.OrdinalIgnoreCase);
            public string Chave => (Categoria ?? "") + "|" + (Subcategoria ?? "");
            public string Resumo => (Exportar ? Classe ?? "" : "não exportado") + "|" + (Tipo ?? "");
            /// <summary>Resumo para mostrar: "IfcSlab · FLOOR", "IfcWall" ou "não exportado".</summary>
            public static string Mostrar(string resumo)
            {
                if (resumo == null) return null;
                string[] p = resumo.Split('|');
                return p.Length > 1 && p[1].Length > 0 ? p[0] + " · " + p[1] : p[0];
            }
            public string OutroEsquemaTexto => Mostrar(OutroEsquema);
            /// <summary>Esquema da tabela da linha (CategoriasIfc.Ifc4 ou Ifc4x3), para conferir a classe e o tipo.</summary>
            public string Esquema { get; set; }
            /// <summary>Classe que não existe no esquema da tabela (aparece em vermelho e impede salvar).</summary>
            public bool ClasseInvalida => Esquema != null && !EsquemaIfc.ClasseValida(Esquema, Classe);
            /// <summary>Tipo predefinido que a classe não tem no esquema da tabela.</summary>
            public bool TipoInvalido => Esquema != null && !ClasseInvalida && !EsquemaIfc.TipoValido(Esquema, Classe, Tipo);
            public Linha Copia() => (Linha)MemberwiseClone();
        }

        /// <summary>Esquema da tabela para a versão do setup (IFC4RV, IFC4DTV e IFC4 usam a do IFC4).</summary>
        public static string EsquemaDaVersao(string versao)
        {
            if (string.IsNullOrEmpty(versao)) return null;
            if (versao.StartsWith("IFC4x3", StringComparison.OrdinalIgnoreCase)) return Ifc4x3;
            if (versao.StartsWith("IFC4", StringComparison.OrdinalIgnoreCase)) return Ifc4;
            return null;
        }

        public static string NomeNoModelo(string esquema) => "MetroSP " + esquema;

        public static string Destino(string esquema) => Path.Combine(ClassesIfc.PastaUsuario, Sub, esquema + ".txt");

        /// <summary>A tabela que vale (sua, do Forma ou instalada), ou null.</summary>
        public static string Arquivo(string esquema) =>
            ClassesIfc.RaizesEmUso.Select(r => Path.Combine(r, Sub, esquema + ".txt")).FirstOrDefault(File.Exists);

        public static List<Linha> Ler(string arquivo)
        {
            var r = new List<Linha>();
            foreach (string bruta in File.ReadAllLines(arquivo))
            {
                if (bruta.Trim().Length == 0 || bruta.TrimStart().StartsWith("#")) continue;
                string[] c = bruta.Split('\t').Select(x => x.Trim()).ToArray();
                if (c.Length < 3 || c[0].Length == 0) continue;
                r.Add(new Linha
                {
                    Categoria = c[0],
                    Subcategoria = c[1],
                    Classe = c[2],
                    Tipo = c.Length > 3 ? c[3] : "",
                    Exportar = !(c.Length > 4 && (c[4].Equals("Não", StringComparison.OrdinalIgnoreCase) || c[4].Equals("Nao", StringComparison.OrdinalIgnoreCase) ||
                                                  c[4].Equals("No", StringComparison.OrdinalIgnoreCase) || c[4] == "0")),
                });
            }
            return r;
        }

        public static string Texto(IEnumerable<Linha> linhas, string esquema)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Categorias do Revit → classes IFC ({esquema}) - salvo pelo MetroSP em {DateTime.Now:dd/MM/yyyy HH:mm}");
            sb.AppendLine("# Categoria\tSubcategoria\tClasse IFC\tTipo predefinido\tExportar");
            foreach (Linha l in linhas)
                sb.AppendLine($"{l.Categoria}\t{l.Subcategoria}\t{l.Classe}\t{l.Tipo}\t{(l.Exportar ? "Sim" : "Não")}");
            return sb.ToString();
        }

        public static void Gravar(string arquivo, IEnumerable<Linha> linhas, string esquema)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(arquivo));
            if (arquivo.StartsWith(ClassesIfc.PastaUsuario, StringComparison.OrdinalIgnoreCase)) ArquivosIfc.GuardarAnterior(arquivo);
            File.WriteAllText(arquivo, Texto(linhas, esquema), new UTF8Encoding(true));
        }

        /// <summary>A tabela que o Revit usa hoje neste modelo (modelo ativo ou o da sessão), como ponto de partida.</summary>
        public static List<Linha> DoModelo(Document doc)
        {
            var r = new List<Linha>();
#if REVIT_ANTES_2025
            return r;   // Revit 2022 a 2024: a API não lê o mapeamento de categorias do modelo
#else
            IFCCategoryTemplate t = IFCCategoryTemplate.GetActiveTemplate(doc) ?? IFCCategoryTemplate.GetOrCreateInSessionTemplate(doc);
            if (t == null) return r;
            foreach (KeyValuePair<ExportIFCCategoryKey, ExportIFCCategoryInfo> p in t.GetCategoryMappingTable(doc))
            {
                if (string.IsNullOrEmpty(p.Key.CategoryName)) continue;
                r.Add(new Linha
                {
                    Categoria = p.Key.CategoryName,
                    Subcategoria = p.Key.SubCategoryName ?? "",
                    Classe = p.Value.IFCEntityName ?? "",
                    Tipo = p.Value.IFCPredefinedType ?? "",
                    Exportar = p.Value.IFCExportFlag,
                });
            }
            return r.OrderBy(l => l.Categoria, StringComparer.CurrentCultureIgnoreCase).ThenBy(l => l.Subcategoria, StringComparer.CurrentCultureIgnoreCase).ToList();
#endif
        }

        /// <summary>
        /// Tabela padrão quando não há tabela salva: o mapeamento atual do modelo com a tabela recomendada do MetroSP
        /// (CategoriasRecomendadas, ISO 16739) por cima. Com o modelo, entram também as categorias recomendadas que faltam.
        /// </summary>
        public static List<Linha> Padrao(Document doc, string esquema, IList<Linha> doModelo = null)
        {
            List<Linha> r = (doModelo ?? new List<Linha>()).Select(l => l.Copia()).ToList();
            Dictionary<string, CategoriasRecomendadas.Item> rec = CategoriasRecomendadas.Para(esquema, doc);
            var feitas = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (Linha l in r)
            {
                if (!rec.TryGetValue(l.Chave, out CategoriasRecomendadas.Item i)) continue;
                feitas.Add(l.Chave);
                l.Classe = i.Classe; l.Tipo = i.Tipo; l.Exportar = i.Exportar;
            }
            if (doc != null)
                foreach (CategoriasRecomendadas.Item i in rec.Values.Where(i => !feitas.Contains(i.Chave)))
                    r.Add(new Linha { Categoria = i.Categoria, Subcategoria = i.Subcategoria, Classe = i.Classe, Tipo = i.Tipo, Exportar = i.Exportar });
            return r.OrderBy(l => l.Categoria, StringComparer.CurrentCultureIgnoreCase).ThenBy(l => l.Subcategoria, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>Elementos de modelo por nome de categoria.</summary>
        public static Dictionary<string, int> Contagem(Document doc)
        {
            var r = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (Element e in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                Category c = e.Category;
                if (c == null || c.CategoryType != CategoryType.Model || e.ViewSpecific) continue;
                r[c.Name] = r.TryGetValue(c.Name, out int n) ? n + 1 : 1;
            }
            return r;
        }

        /// <summary>
        /// Grava a tabela no modelo de mapeamento "MetroSP &lt;esquema&gt;" e o torna ativo. Precisa de transação aberta.
        /// Retorna quantas categorias foram ajustadas.
        /// </summary>
        public static int Aplicar(Document doc, string esquema, IList<Linha> linhas)
        {
#if REVIT_ANTES_2025
            throw new InvalidOperationException(SemApiNoRevit);
#else
            string nome = NomeNoModelo(esquema);
            // Um modelo criado numa exportação anterior e desfeito (rollback) pode voltar como objeto inválido: cria de novo.
            IFCCategoryTemplate t = null;
            try
            {
                t = IFCCategoryTemplate.FindByName(doc, nome);
                if (t != null && !t.IsValidObject) t = null;
                t?.GetCategoryMappingTable(doc);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { t = null; }
            if (t == null) t = IFCCategoryTemplate.Create(doc, nome);
            try { t.UpdateCategoryList(doc); } catch { /* lista já atualizada */ }
            var porChave = new Dictionary<string, Linha>(StringComparer.OrdinalIgnoreCase);
            foreach (Linha l in linhas) porChave[l.Chave] = l;
            var mudar = new Dictionary<ExportIFCCategoryKey, ExportIFCCategoryInfo>();
            foreach (KeyValuePair<ExportIFCCategoryKey, ExportIFCCategoryInfo> p in t.GetCategoryMappingTable(doc))
            {
                if (!porChave.TryGetValue((p.Key.CategoryName ?? "") + "|" + (p.Key.SubCategoryName ?? ""), out Linha l)) continue;
                ExportIFCCategoryInfo info = p.Value;
                info.IFCExportFlag = l.Exportar;
                info.IFCEntityName = l.Classe ?? "";
                info.IFCPredefinedType = l.Tipo ?? "";
                mudar[p.Key] = info;
            }
            if (mudar.Count > 0) t.SetMappingInfo(mudar);
#if REVIT2025
            t.SetActiveTemplate(doc);
#else
            t.SetActiveTemplate();
#endif
            return mudar.Count;
#endif
        }

        /// <summary>Aviso do Revit 2022 a 2024, que não deixam o add-in gravar o mapeamento de categorias.</summary>
        public const string SemApiNoRevit =
            "Nesta versão do Revit o add-in não consegue gravar a tabela de categorias no modelo. " +
            "Vale o mapeamento do próprio Revit (Arquivo > Exportar > Opções > Opções IFC).";

        /// <summary>Na exportação: aplica a tabela do esquema do setup (a salva ou, sem ela, o padrão MetroSP). Retorna a linha para o resumo (ou null).</summary>
        public static string AplicarNaExportacao(Document doc, string versao)
        {
            string esquema = EsquemaDaVersao(versao);
            if (esquema == null) return null;
            string arquivo = Arquivo(esquema);
#if REVIT_ANTES_2025
            return "Categorias IFC: nesta versão do Revit vale o mapeamento do próprio Revit (Opções IFC).";
#else
            // Numa subtransação: se algo falhar, o modelo volta como estava e a exportação segue com o mapeamento do modelo.
            using (var sub = new SubTransaction(doc))
            {
                try
                {
                    sub.Start();
                    string r;
                    if (arquivo == null)
                    {
                        int p = Aplicar(doc, esquema, Padrao(doc, esquema));
                        r = $"Categorias IFC: padrão MetroSP {esquema} (ISO 16739), {p} categorias.";
                    }
                    else
                    {
                        int n = Aplicar(doc, esquema, Ler(arquivo));
                        r = $"Categorias IFC: tabela {esquema} ({ArquivosIfc.Origem(arquivo)}), {n} categorias.";
                    }
                    sub.Commit();
                    return r;
                }
                catch (Exception ex)
                {
                    try { if (sub.HasStarted() && !sub.HasEnded()) sub.RollBack(); } catch { /* já desfeita */ }
                    return $"Categorias IFC: não foi possível aplicar a tabela {esquema} ({ex.Message}); valeu o mapeamento do modelo.";
                }
            }
#endif
        }
    }
}
