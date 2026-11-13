using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace MetroSP.Ids
{
    /// <summary>
    /// Configurações de IFC por classe que vêm com o instalador (pasta IFC ao lado da DLL), cada uma com o
    /// setup .json, o .txt de property sets e o .txt de mapeamento com o nome da classe. O usuário pode
    /// editar os property sets de uma classe pela janela: a cópia editada fica em %AppData%\MetroSP\IFC
    /// e vale no lugar da padrão até ele restaurar o padrão. Com uma pasta do Forma online escolhida, a cópia
    /// baixada dela vale sobre a instalada.
    ///
    /// A classe pode vir do nome do modelo: Nomenclatura.txt traz regras "padrão → classe"; sem regra que case,
    /// vale a classe cujo nome aparece como um trecho do nome do modelo (separado por . - _ ou espaço).
    /// </summary>
    public static class ClassesIfc
    {
        public class Classe
        {
            public string Nome { get; set; }
            public string Setup { get; set; }
            public string PropertySets { get; set; }
            public string Mapeamento { get; set; }
            /// <summary>true quando os property sets são a cópia editada pelo usuário.</summary>
            public bool Editada { get; set; }
            /// <summary>true quando os property sets vêm da pasta do Forma online.</summary>
            public bool DoForma { get; set; }
            /// <summary>true quando o setup ou os property sets vêm do arquivo de coordenação do modelo.</summary>
            public bool DaCoordenacao { get; set; }
            public override string ToString() => DaCoordenacao ? Nome + "  (coordenação)" : Editada ? Nome + "  (editada)" : DoForma ? Nome + "  (Forma)" : Nome;
        }

        /// <summary>Pasta IFC instalada junto com o add-in.</summary>
        public static string PastaPadrao =>
            Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "", "IFC");

        /// <summary>Pasta das edições do usuário.</summary>
        public static string PastaUsuario =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MetroSP", "IFC");

        /// <summary>Classes instaladas e as criadas pelo usuário, pelo nome dos arquivos.</summary>
        public static List<Classe> Listar()
        {
            var nomes = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (string raiz in Raizes)
            {
                Nomes(raiz, FormaPaths.SubSetups, "*.json", nomes);
                Nomes(raiz, FormaPaths.SubPropertySets, "*.txt", nomes);
            }
            return nomes.Select(Obter).ToList();
        }

        public static Classe Obter(string nome)
        {
            string psets = Achar(FormaPaths.SubPropertySets, nome + ".txt");
            string setup = Achar(FormaPaths.SubSetups, nome + ".json");
            string coord = Coordenacao.CoordenacaoRevit.PastaAtiva;
            bool DaCoord(string f) => coord != null && f != null && f.StartsWith(coord, StringComparison.OrdinalIgnoreCase);
            return new Classe
            {
                Nome = nome,
                Setup = setup,
                PropertySets = psets,
                Mapeamento = Achar(FormaPaths.SubMapeamentos, nome + ".txt"),
                Editada = psets != null && string.Equals(psets, CaminhoUsuario(nome), StringComparison.OrdinalIgnoreCase),
                DoForma = psets != null && psets.StartsWith(FormaOnline.PastaCache, StringComparison.OrdinalIgnoreCase),
                DaCoordenacao = DaCoord(psets) || DaCoord(setup),
            };
        }

        /// <summary>Onde fica a cópia editada dos property sets da classe.</summary>
        public static string CaminhoUsuario(string nome) =>
            Path.Combine(PastaUsuario, FormaPaths.SubPropertySets, nome + ".txt");

        /// <summary>Property sets da classe sem a edição do usuário: do Forma online ou do instalador (null se não houver).</summary>
        public static string CaminhoPadrao(string nome) =>
            new[] { FormaOnline.PastaCache, PastaPadrao }.Select(r => Path.Combine(r, FormaPaths.SubPropertySets, nome + ".txt")).FirstOrDefault(File.Exists);

        /// <summary>Apaga a cópia editada; a classe volta a usar o .txt instalado.</summary>
        public static void RestaurarPadrao(string nome)
        {
            string f = CaminhoUsuario(nome);
            if (File.Exists(f)) File.Delete(f);
        }

        // Ordem de preferência: da coordenação do modelo, do usuário, do Forma online, instalado.
        private static string[] Raizes =>
            Coordenacao.CoordenacaoRevit.PastaAtiva is string coord
                ? new[] { coord, PastaUsuario, FormaOnline.PastaCache, PastaPadrao }
                : new[] { PastaUsuario, FormaOnline.PastaCache, PastaPadrao };

        /// <summary>Raízes na ordem de preferência: da coordenação do modelo, do usuário, do Forma online, instalada.</summary>
        internal static string[] RaizesEmUso => Raizes;

        /// <summary>
        /// O arquivo que vale, na ordem das raízes. Exceção: um arquivo que o usuário salvou depois de receber a
        /// coordenação (mais novo que o dela) vale sobre o da coordenação.
        /// </summary>
        internal static string Achar(string sub, string arquivo)
        {
            string[] raizes = Raizes;
            string achado = raizes.Select(r => Path.Combine(r, sub, arquivo)).FirstOrDefault(File.Exists);
            string coord = Coordenacao.CoordenacaoRevit.PastaAtiva;
            if (achado == null || coord == null || !achado.StartsWith(coord, StringComparison.OrdinalIgnoreCase)) return achado;
            string doUsuario = Path.Combine(PastaUsuario, sub, arquivo);
            try { return File.Exists(doUsuario) && File.GetLastWriteTimeUtc(doUsuario) > File.GetLastWriteTimeUtc(achado) ? doUsuario : achado; }
            catch { return achado; }
        }

        // ---------- nomenclatura ----------

        public const string ArquivoNomenclatura = "Nomenclatura.txt";

        /// <summary>Regras de nomenclatura em uso (do usuário, do Forma online ou instaladas), ou null.</summary>
        public static string ArquivoDeRegras => Achar("", ArquivoNomenclatura);

        /// <summary>
        /// Classe indicada pelo nome do modelo, e o motivo (para mostrar). Regras em Nomenclatura.txt, uma por linha:
        ///   &lt;padrão&gt; [tab ou ;] &lt;Classe&gt;
        /// O padrão usa * e ? e vale para o nome inteiro sem .rvt (ex.: "*-ARQ-*"); começando com "re:" é uma
        /// expressão regular, e o grupo nomeado "classe", se houver, dá a classe. Linhas com # são comentários.
        /// </summary>
        public static (Classe Classe, string Motivo) DoNomeDoModelo(string titulo, IList<Classe> classes)
        {
            if (string.IsNullOrWhiteSpace(titulo) || classes == null || classes.Count == 0) return (null, null);
            Classe Por(string n) => classes.FirstOrDefault(c => string.Equals(c.Nome, n?.Trim(), StringComparison.CurrentCultureIgnoreCase));
            foreach (string nome in NomesBase(titulo))
            {
                var r = DoNome(nome, Por);
                if (r.Classe != null) return r;
            }
            return (null, null);
        }

        /// <summary>
        /// O nome do modelo e, depois, o nome sem os acréscimos que o Revit e o Windows põem no fim:
        /// cópia local ("_usuario"), cópia baixada (" (1)") e modelo desanexado ("_detached", "_desanexado").
        /// </summary>
        private static IEnumerable<string> NomesBase(string titulo)
        {
            string nome = titulo.Trim();
            if (nome.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase)) nome = nome.Substring(0, nome.Length - 4);
            yield return nome;
            string limpo = Regex.Replace(nome, @"\s*\(\d+\)$", "");
            foreach (string sufixo in new[] { "_" + Environment.UserName, "_detached", "_desanexado", "_backup" })
                if (limpo.EndsWith(sufixo, StringComparison.OrdinalIgnoreCase)) limpo = limpo.Substring(0, limpo.Length - sufixo.Length);
            if (limpo != nome) yield return limpo;
        }

        private static (Classe Classe, string Motivo) DoNome(string nome, Func<string, Classe> Por)
        {

            string regras = ArquivoDeRegras;
            if (regras != null)
            {
                try
                {
                    foreach (string linha in File.ReadAllLines(regras))
                    {
                        string l = linha.Trim();
                        if (l.Length == 0 || l.StartsWith("#")) continue;
                        int sep = l.IndexOf('\t') >= 0 ? l.IndexOf('\t') : l.LastIndexOf(';');
                        string padrao = sep > 0 ? l.Substring(0, sep).Trim() : l;
                        string classe = sep > 0 ? l.Substring(sep + 1).Trim() : null;

                        Regex re = padrao.StartsWith("re:", StringComparison.OrdinalIgnoreCase)
                            ? new Regex(padrao.Substring(3).Trim(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                            : new Regex("^" + Regex.Escape(padrao).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                        Match m = re.Match(nome);
                        if (!m.Success) continue;
                        Classe c = Por(m.Groups["classe"].Success ? m.Groups["classe"].Value : classe);
                        if (c != null) return (c, $"pelo nome do modelo (\"{m.Value}\", regra de {ArquivoNomenclatura})");
                    }
                }
                catch { /* regra inválida: segue para o trecho do nome */ }
            }

            foreach (string trecho in nome.Split(new[] { '.', '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Classe c = Por(trecho);
                if (c != null) return (c, $"pelo trecho \"{trecho}\" do nome do modelo");
            }
            return (null, null);
        }

        private static void Nomes(string raiz, string sub, string padrao, SortedSet<string> nomes)
        {
            try
            {
                string pasta = Path.Combine(raiz, sub);
                if (!Directory.Exists(pasta)) return;
                foreach (string f in Directory.EnumerateFiles(pasta, padrao)) nomes.Add(Path.GetFileNameWithoutExtension(f));
            }
            catch { /* pasta inacessível */ }
        }
    }
}
