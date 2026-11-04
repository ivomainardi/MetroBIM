using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MetroSP.Classificacao;
using MetroSP.Ids;
using MetroSP.Parametros;

namespace MetroSP.Conferencia
{
    /// <summary>
    /// Os arquivos que o Metrô pede para uma entrega (IDS, setup IFC, psets, classes IFC, planilha de classificação
    /// e MetroShared). O coordenador BIM salva um pacote (.metrosp, texto simples) numa pasta compartilhada, com os
    /// caminhos relativos à pasta do próprio pacote; o projetista só escolhe o pacote.
    ///
    /// Sem pacote salvo, os caminhos ficam em %AppData%\MetroSP\conferencia.ini e, na primeira vez, vêm das
    /// escolhas já feitas nas outras ferramentas (Verificar IDS, Exportar IFC, Classificação, base MetroShared).
    /// </summary>
    public class PacoteMetro
    {
        public const string Extensao = ".metrosp";

        private static readonly string ArquivoUsuario =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MetroSP", "conferencia.ini");

        /// <summary>Arquivo .metrosp de onde o pacote veio (null = pacote só deste usuário).</summary>
        public string Arquivo { get; set; }
        public string Nome { get; set; }
        public string Ids { get; set; }
        public string Setup { get; set; }
        public string PropertySets { get; set; }
        public string ClassesIfc { get; set; }
        public string Planilha { get; set; }
        public string MetroShared { get; set; }
        public string ParametroClassificacao { get; set; } = "ClassificationCode";

        /// <summary>Escopo da última conferência: true = só a vista ativa.</summary>
        public bool SoVistaAtiva { get; set; }

        public string NomeExibido =>
            !string.IsNullOrWhiteSpace(Nome) ? Nome
            : Arquivo != null ? Path.GetFileNameWithoutExtension(Arquivo)
            : "Arquivos escolhidos neste computador";

        public PacoteMetro Copia() => (PacoteMetro)MemberwiseClone();

        /// <summary>Texto que muda quando qualquer arquivo ou o nome muda (para saber se o pacote foi editado).</summary>
        public string Assinatura() =>
            string.Join("|", Nome, Ids, Setup, PropertySets, ClassesIfc, Planilha, MetroShared, ParametroClassificacao);

        // ------------------------------------------------------------------ usuário

        /// <summary>O pacote em uso por este usuário (o último escolhido).</summary>
        public static PacoteMetro Abrir()
        {
            Dictionary<string, string> d = LerIni(ArquivoUsuario);
            PacoteMetro p = null;

            if (d.TryGetValue("pacote", out string arquivo) && File.Exists(arquivo))
            {
                try { p = LerArquivo(arquivo); }
                catch { p = null; }
            }
            if (p == null)
            {
                p = d.Count > 0 ? DeIni(d, null) : DasOutrasFerramentas();
            }
            if (d.TryGetValue("vista", out string v)) p.SoVistaAtiva = v == "1";
            return p;
        }

        /// <summary>Lembra o pacote em uso (o arquivo .metrosp ou, sem ele, os caminhos).</summary>
        public void SalvarComoAtual()
        {
            var linhas = new List<string>();
            if (Arquivo != null) linhas.Add("pacote=" + Arquivo);
            else linhas.AddRange(ParaIni(null));
            linhas.Add("vista=" + (SoVistaAtiva ? "1" : "0"));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ArquivoUsuario));
                File.WriteAllLines(ArquivoUsuario, linhas, Encoding.UTF8);
            }
            catch { /* não é crítico */ }
        }

        /// <summary>Primeira vez: aproveita o que o usuário já escolheu nas outras ferramentas.</summary>
        public static PacoteMetro DasOutrasFerramentas()
        {
            var p = new PacoteMetro();
            try
            {
                IdsSettings ids = IdsSettings.Load();
                IfcExportSettings ifc = IfcExportSettings.Load();
                ClassSettings cls = ClassSettings.Abrir();
                ReferenceLibrary refs = ReferenceLibrary.Abrir();
                p.Ids = ids.IdsFile;
                p.Setup = ifc.Setup;
                p.PropertySets = !string.IsNullOrEmpty(ifc.PropertySets) ? ifc.PropertySets : ids.PropertySetsFile;
                p.ClassesIfc = ids.CategoryMappingFile;
                p.Planilha = cls.Planilha;
                if (!string.IsNullOrEmpty(cls.Parametro)) p.ParametroClassificacao = cls.Parametro;
                p.MetroShared = refs.Base;
            }
            catch { /* começa vazio */ }
            return p;
        }

        /// <summary>
        /// Passa os arquivos do pacote para as outras ferramentas, para que Exportar IFC, Verificar IDS e
        /// Classificação abram com os mesmos arquivos da conferência.
        /// </summary>
        public void AplicarNasOutrasFerramentas()
        {
            try
            {
                IfcExportSettings ifc = IfcExportSettings.Load();
                if (Existe(Setup)) ifc.Setup = Setup;
                if (Existe(PropertySets)) ifc.PropertySets = PropertySets;
                ifc.Save();

                IdsSettings ids = IdsSettings.Load();
                if (Existe(Ids)) ids.IdsFile = Ids;
                if (Existe(PropertySets)) ids.PropertySetsFile = PropertySets;
                if (Existe(ClassesIfc)) ids.CategoryMappingFile = ClassesIfc;
                ids.Save();

                ClassSettings cls = ClassSettings.Abrir();
                if (Existe(Planilha)) cls.Planilha = Planilha;
                if (!string.IsNullOrWhiteSpace(ParametroClassificacao)) cls.Parametro = ParametroClassificacao;
                cls.Salvar();

                if (Existe(MetroShared))
                {
                    ReferenceLibrary refs = ReferenceLibrary.Abrir();
                    refs.Adicionar(MetroShared);
                    refs.Base = MetroShared;
                    refs.Salvar();
                }
            }
            catch { /* não é crítico */ }
        }

        // ------------------------------------------------------------------ arquivo .metrosp

        public static PacoteMetro LerArquivo(string arquivo)
        {
            Dictionary<string, string> d = LerIni(arquivo);
            if (d.Count == 0) throw new InvalidDataException("O arquivo não é um pacote do MetroSP.");
            PacoteMetro p = DeIni(d, Path.GetDirectoryName(arquivo));
            p.Arquivo = arquivo;
            return p;
        }

        /// <summary>Salva o pacote com caminhos relativos à pasta dele (quando estão na mesma pasta ou abaixo).</summary>
        public void SalvarArquivo(string arquivo)
        {
            var linhas = new List<string>
            {
                "# Pacote de entrega do Metrô para o MetroSP (Conferir Entrega).",
                "# Caminhos relativos à pasta deste arquivo. Edite pelo próprio MetroSP ou num editor de texto.",
            };
            linhas.AddRange(ParaIni(Path.GetDirectoryName(arquivo)));
            File.WriteAllLines(arquivo, linhas, new UTF8Encoding(true));
            Arquivo = arquivo;
        }

        /// <summary>Pacotes .metrosp numa pasta e nas subpastas logo abaixo.</summary>
        public static List<string> Procurar(string pasta)
        {
            try
            {
                if (string.IsNullOrEmpty(pasta) || !Directory.Exists(pasta)) return new List<string>();
                var achados = Directory.EnumerateFiles(pasta, "*" + Extensao).ToList();
                foreach (string sub in Directory.EnumerateDirectories(pasta))
                {
                    try { achados.AddRange(Directory.EnumerateFiles(sub, "*" + Extensao)); }
                    catch { /* sem acesso */ }
                }
                return achados.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch { return new List<string>(); }
        }

        // ------------------------------------------------------------------ ini

        private IEnumerable<string> ParaIni(string pasta)
        {
            yield return "nome=" + (Nome ?? "");
            yield return "ids=" + Relativo(Ids, pasta);
            yield return "setup=" + Relativo(Setup, pasta);
            yield return "psets=" + Relativo(PropertySets, pasta);
            yield return "classes=" + Relativo(ClassesIfc, pasta);
            yield return "planilha=" + Relativo(Planilha, pasta);
            yield return "metroshared=" + Relativo(MetroShared, pasta);
            yield return "parametro=" + (ParametroClassificacao ?? "");
        }

        private static PacoteMetro DeIni(Dictionary<string, string> d, string pasta)
        {
            string V(string k) => d.TryGetValue(k, out string v) && v.Trim().Length > 0 ? v.Trim() : null;
            var p = new PacoteMetro
            {
                Nome = V("nome"),
                Ids = Absoluto(V("ids"), pasta),
                Setup = Absoluto(V("setup"), pasta),
                PropertySets = Absoluto(V("psets"), pasta),
                ClassesIfc = Absoluto(V("classes"), pasta),
                Planilha = Absoluto(V("planilha"), pasta),
                MetroShared = Absoluto(V("metroshared"), pasta),
            };
            if (V("parametro") != null) p.ParametroClassificacao = V("parametro");
            return p;
        }

        private static Dictionary<string, string> LerIni(string arquivo)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(arquivo)) return d;
                foreach (string l in File.ReadAllLines(arquivo, Encoding.UTF8))
                {
                    if (l.TrimStart().StartsWith("#")) continue;
                    string[] p = l.Split(new[] { '=' }, 2);
                    if (p.Length == 2) d[p[0].Trim()] = p[1].Trim();
                }
            }
            catch { /* ilegível: vazio */ }
            return d;
        }

        private static string Relativo(string caminho, string pasta)
        {
            if (string.IsNullOrWhiteSpace(caminho)) return "";
            if (string.IsNullOrEmpty(pasta)) return caminho;
            try
            {
                string base_ = Path.GetFullPath(pasta).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                string cheio = Path.GetFullPath(caminho);
                if (cheio.StartsWith(base_, StringComparison.OrdinalIgnoreCase)) return cheio.Substring(base_.Length);
            }
            catch { /* caminho estranho: grava como está */ }
            return caminho;
        }

        private static string Absoluto(string caminho, string pasta)
        {
            if (string.IsNullOrWhiteSpace(caminho)) return null;
            if (string.IsNullOrEmpty(pasta) || Path.IsPathRooted(caminho)) return caminho;
            try { return Path.GetFullPath(Path.Combine(pasta, caminho)); }
            catch { return caminho; }
        }

        public static bool Existe(string caminho) => !string.IsNullOrWhiteSpace(caminho) && File.Exists(caminho);
    }
}
