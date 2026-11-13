using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MetroSP.Ifc
{
    /// <summary>Nome, nome longo, descrição, fase (só IfcProject) e endereço (IfcSite e IfcBuilding) de uma entidade IFC.</summary>
    public class DadosEntidadeIfc
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Nome { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string NomeLongo { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Descricao { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Fase { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Endereco { get; set; }

        public DadosEntidadeIfc Copia() => (DadosEntidadeIfc)MemberwiseClone();

        /// <summary>Copia do padrão os campos que ele preenche (os vazios do padrão não apagam nada).</summary>
        public void Completar(DadosEntidadeIfc p)
        {
            if (p == null) return;
            if (!Vazio(p.Nome)) Nome = p.Nome;
            if (!Vazio(p.NomeLongo)) NomeLongo = p.NomeLongo;
            if (!Vazio(p.Descricao)) Descricao = p.Descricao;
            if (!Vazio(p.Fase)) Fase = p.Fase;
            if (!Vazio(p.Endereco)) Endereco = p.Endereco;
        }

        internal static bool Vazio(string s) => string.IsNullOrWhiteSpace(s);

        /// <summary>Texto que o Revit põe no endereço de um modelo novo ("Enter address here", "Address Line 2"): conta como vazio.</summary>
        internal static bool EnderecoPadraoRevit(string s) =>
            s != null && (s.IndexOf("Enter address here", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          s.IndexOf("Address Line 2", StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>O endereço sem o texto padrão do Revit (vazio se só tinha ele).</summary>
        internal static string SemEnderecoPadrao(string s) => EnderecoPadraoRevit(s) ? "" : s;
    }

    /// <summary>
    /// Dados do IfcProject, do IfcSite e do IfcBuilding e o Pset MetroSP_Projeto: os mesmos campos nos plugins
    /// do Revit e do Civil 3D (este arquivo é igual nos dois). Os padrões ficam em %AppData%\MetroSP\DadosProjeto,
    /// lidos pelos dois plugins, para a equipe reaproveitar os dados de um contrato em todos os modelos.
    /// Campos vazios não mudam o que o exportador gravou.
    /// </summary>
    public class DadosProjetoIfc
    {
        public const string NomePset = "MetroSP_Projeto";

        public DadosEntidadeIfc Projeto { get; set; } = new DadosEntidadeIfc();
        public DadosEntidadeIfc Terreno { get; set; } = new DadosEntidadeIfc();
        public DadosEntidadeIfc Edificio { get; set; } = new DadosEntidadeIfc();
        /// <summary>Propriedades do Pset MetroSP_Projeto (gravado no IfcProject). Valor vazio não entra.</summary>
        public Dictionary<string, string> Propriedades { get; set; } = new Dictionary<string, string>();

        public DadosProjetoIfc Copia() => new DadosProjetoIfc
        {
            Projeto = (Projeto ?? new DadosEntidadeIfc()).Copia(),
            Terreno = (Terreno ?? new DadosEntidadeIfc()).Copia(),
            Edificio = (Edificio ?? new DadosEntidadeIfc()).Copia(),
            Propriedades = new Dictionary<string, string>(Propriedades ?? new Dictionary<string, string>()),
        };

        /// <summary>Preenche com o padrão: campos preenchidos no padrão substituem; propriedades do padrão entram ou substituem.</summary>
        public void Completar(DadosProjetoIfc p)
        {
            if (p == null) return;
            (Projeto ??= new DadosEntidadeIfc()).Completar(p.Projeto);
            (Terreno ??= new DadosEntidadeIfc()).Completar(p.Terreno);
            (Edificio ??= new DadosEntidadeIfc()).Completar(p.Edificio);
            Propriedades ??= new Dictionary<string, string>();
            foreach (var kv in p.Propriedades ?? new Dictionary<string, string>())
                if (!DadosEntidadeIfc.Vazio(kv.Key) && !DadosEntidadeIfc.Vazio(kv.Value)) Propriedades[kv.Key.Trim()] = kv.Value;
        }

        // ================================================================== padrões salvos

        public static string Pasta =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MetroSP", "DadosProjeto");

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            PropertyNameCaseInsensitive = true,
        };

        public static List<string> Listar()
        {
            try
            {
                if (!Directory.Exists(Pasta)) return new List<string>();
                return Directory.GetFiles(Pasta, "*.json").Select(Path.GetFileNameWithoutExtension)
                    .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
            catch { return new List<string>(); }
        }

        public static string Arquivo(string nome) => Path.Combine(Pasta, NomeValido(nome) + ".json");

        public static DadosProjetoIfc Carregar(string nome) => LerArquivo(Arquivo(nome));

        public static DadosProjetoIfc LerArquivo(string arquivo) =>
            JsonSerializer.Deserialize<DadosProjetoIfc>(File.ReadAllText(arquivo), Json) ?? new DadosProjetoIfc();

        public void Salvar(string nome)
        {
            Directory.CreateDirectory(Pasta);
            File.WriteAllText(Arquivo(nome), JsonSerializer.Serialize(Limpo(), Json), new UTF8Encoding(false));
        }

        public string ParaJson() => JsonSerializer.Serialize(Limpo(), Json);

        public static DadosProjetoIfc DeJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonSerializer.Deserialize<DadosProjetoIfc>(json, Json); }
            catch { return null; }
        }

        /// <summary>Cópia sem textos vazios (o JSON fica só com o que foi preenchido).</summary>
        private DadosProjetoIfc Limpo()
        {
            static DadosEntidadeIfc L(DadosEntidadeIfc e) => new DadosEntidadeIfc
            {
                Nome = T(e?.Nome), NomeLongo = T(e?.NomeLongo), Descricao = T(e?.Descricao), Fase = T(e?.Fase), Endereco = T(e?.Endereco),
            };
            return new DadosProjetoIfc
            {
                Projeto = L(Projeto), Terreno = L(Terreno), Edificio = L(Edificio),
                Propriedades = (Propriedades ?? new Dictionary<string, string>())
                    .Where(kv => !DadosEntidadeIfc.Vazio(kv.Key))
                    .ToDictionary(kv => kv.Key.Trim(), kv => kv.Value ?? ""),
            };
        }

        private static string T(string s) => DadosEntidadeIfc.Vazio(s) ? null : s.Trim();

        public static string NomeValido(string nome)
        {
            var s = (nome ?? "").Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Length == 0 ? "Padrão" : s;
        }

        // ================================================================== no IFC exportado

        /// <summary>Tipos de instalação (IFC 4.3) que recebem os dados do "Edificio" quando não há IfcBuilding.</summary>
        private static readonly string[] Instalacoes = { "IFCBUILDING", "IFCRAILWAY", "IFCROAD", "IFCBRIDGE", "IFCMARINEFACILITY", "IFCFACILITY" };

        /// <summary>
        /// Grava os campos preenchidos no IfcProject (Name, Description, LongName, Phase), no IfcSite (Name, Description,
        /// LongName, endereço) e na primeira instalação (Name, Description, LongName, endereço do IfcBuilding), e o Pset
        /// MetroSP_Projeto no IfcProject (substitui o anterior). Devolve o que fez.
        /// </summary>
        public List<string> AplicarNoIfc(StepModel m) => AplicarNoIfc(m, out _);

        public List<string> AplicarNoIfc(StepModel m, out bool alterou)
        {
            var log = new List<string>();
            alterou = false;
            var projeto = m.OfType("IFCPROJECT").FirstOrDefault();
            if (projeto == null) { log.Add("IFC sem IfcProject: dados do projeto não aplicados."); return log; }
            int n = Texto(m, projeto, 2, Projeto?.Nome) + Texto(m, projeto, 3, Projeto?.Descricao) +
                    Texto(m, projeto, 5, Projeto?.NomeLongo) + Texto(m, projeto, 6, Projeto?.Fase);
            if (n > 0) alterou = true;
            if (n > 0) log.Add($"IfcProject: {n} campo(s) gravado(s).");

            var site = m.OfType("IFCSITE").FirstOrDefault();
            if (site != null)
            {
                n = Texto(m, site, 2, Terreno?.Nome) + Texto(m, site, 3, Terreno?.Descricao) + Texto(m, site, 7, Terreno?.NomeLongo) +
                    Endereco(m, site, 13, Terreno?.Endereco);
                if (n > 0) alterou = true;
                if (n > 0) log.Add($"IfcSite: {n} campo(s) gravado(s).");
            }
            else if (Preenchido(Terreno)) log.Add("IFC sem IfcSite: dados do terreno não aplicados.");

            StepEntity inst = Instalacoes.Select(t => m.OfType(t).FirstOrDefault()).FirstOrDefault(e => e != null);
            if (inst != null)
            {
                n = Texto(m, inst, 2, Edificio?.Nome) + Texto(m, inst, 3, Edificio?.Descricao) + Texto(m, inst, 7, Edificio?.NomeLongo) +
                    (inst.Type == "IFCBUILDING" ? TirarEnderecoPadrao(m, inst, 11) + Endereco(m, inst, 11, Edificio?.Endereco) : 0);
                if (n > 0) alterou = true;
                if (n > 0) log.Add($"{NomeTipo(inst.Type)}: {n} campo(s) gravado(s).");
            }
            else if (Preenchido(Edificio)) log.Add("IFC sem IfcBuilding: dados do edifício não aplicados.");

            int props = Pset(m, projeto);
            if (props > 0) alterou = true;
            if (props > 0) log.Add($"Pset {NomePset} com {props} propriedade(s) no IfcProject.");
            return log;
        }

        /// <summary>Confere no IFC o que foi preenchido. Cada linha diz se confere; TemProblema se algo não confere.</summary>
        public List<string> Conferir(StepModel m, out bool temProblema)
        {
            var linhas = new List<string>();
            bool problema = false;
            void Linha(string rotulo, string esperado, string achado)
            {
                if (DadosEntidadeIfc.Vazio(esperado)) return;
                bool ok = string.Equals((achado ?? "").Trim(), esperado.Trim(), StringComparison.Ordinal);
                linhas.Add($"{rotulo}: \"{achado}\"" + (ok ? "" : $" (esperado \"{esperado.Trim()}\")"));
                if (!ok) problema = true;
            }
            var projeto = m.OfType("IFCPROJECT").FirstOrDefault();
            if (projeto != null)
            {
                Linha("IfcProject Name", Projeto?.Nome, Arg(projeto, 2));
                Linha("IfcProject LongName", Projeto?.NomeLongo, Arg(projeto, 5));
                Linha("IfcProject Description", Projeto?.Descricao, Arg(projeto, 3));
                Linha("IfcProject Phase", Projeto?.Fase, Arg(projeto, 6));
            }
            var site = m.OfType("IFCSITE").FirstOrDefault();
            if (site != null)
            {
                Linha("IfcSite LongName", Terreno?.NomeLongo, Arg(site, 7));
                Linha("IfcSite Description", Terreno?.Descricao, Arg(site, 3));
            }
            StepEntity inst = Instalacoes.Select(t => m.OfType(t).FirstOrDefault()).FirstOrDefault(e => e != null);
            if (inst != null)
            {
                Linha(NomeTipo(inst.Type) + " LongName", Edificio?.NomeLongo, Arg(inst, 7));
                Linha(NomeTipo(inst.Type) + " Description", Edificio?.Descricao, Arg(inst, 3));
            }
            int esperadas = (Propriedades ?? new Dictionary<string, string>()).Count(kv => !DadosEntidadeIfc.Vazio(kv.Key) && !DadosEntidadeIfc.Vazio(kv.Value));
            if (esperadas > 0)
            {
                bool tem = m.OfType("IFCPROPERTYSET").Any(p => StepModel.Text(p.Args[2]) == NomePset);
                linhas.Add($"Pset {NomePset}: " + (tem ? "no IfcProject" : "NÃO encontrado"));
                if (!tem) problema = true;
            }
            temProblema = problema;
            return linhas;
        }

        private static bool Preenchido(DadosEntidadeIfc e) =>
            e != null && new[] { e.Nome, e.NomeLongo, e.Descricao, e.Fase, e.Endereco }.Any(s => !DadosEntidadeIfc.Vazio(s));

        private static string Arg(StepEntity e, int i) => i < e.Args.Count ? StepModel.Text(e.Args[i]) ?? "" : "";

        private static string NomeTipo(string tipo) => tipo switch
        {
            "IFCBUILDING" => "IfcBuilding", "IFCRAILWAY" => "IfcRailway", "IFCROAD" => "IfcRoad", "IFCBRIDGE" => "IfcBridge",
            "IFCMARINEFACILITY" => "IfcMarineFacility", _ => "IfcFacility",
        };

        private static int Texto(StepModel m, StepEntity e, int i, string valor)
        {
            if (DadosEntidadeIfc.Vazio(valor) || i >= e.Args.Count) return 0;
            string novo = S(valor.Trim());
            if (e.Args[i] == novo) return 0;
            m.SetArg(e.Id, i, novo);
            return 1;
        }

        /// <summary>
        /// Tira o IfcPostalAddress que o exportador gravou com o texto padrão do Revit ("Enter address here",
        /// "Address Line 2"): a entidade fica sem endereço, ou recebe o preenchido logo depois.
        /// </summary>
        private static int TirarEnderecoPadrao(StepModel m, StepEntity e, int i)
        {
            if (i >= e.Args.Count) return 0;
            int id = StepModel.Ref(e.Args[i]);
            StepEntity end = id == 0 ? null : m.Get(id);
            if (end == null || end.Type != "IFCPOSTALADDRESS") return 0;
            bool padrao = end.Args.Count > 4 && (DadosEntidadeIfc.EnderecoPadraoRevit(StepModel.Text(end.Args[4])) ||
                StepModel.SplitArgs(end.Args[4].Trim().TrimStart('(').TrimEnd(')')).Any(l => DadosEntidadeIfc.EnderecoPadraoRevit(StepModel.Text(l))));
            if (!padrao) return 0;
            m.SetArg(e.Id, i, "$");
            string refEnd = "#" + id;
            bool usado = m.Ids.Any(x => x != e.Id && m.Get(x).Args.Any(a => ContemRef(a, refEnd)));
            if (!usado) m.Remove(id);
            return 1;
        }

        private static bool ContemRef(string arg, string r)
        {
            int p = arg.IndexOf(r, StringComparison.Ordinal);
            while (p >= 0)
            {
                int fim = p + r.Length;
                if (fim >= arg.Length || !char.IsDigit(arg[fim])) return true;
                p = arg.IndexOf(r, fim, StringComparison.Ordinal);
            }
            return false;
        }

        /// <summary>Cria o IfcPostalAddress só se a entidade ainda não tem endereço (o exportador pode já ter gravado um).</summary>
        private static int Endereco(StepModel m, StepEntity e, int i, string valor)
        {
            if (DadosEntidadeIfc.Vazio(valor) || DadosEntidadeIfc.EnderecoPadraoRevit(valor) || i >= e.Args.Count || e.Args[i] != "$") return 0;
            int end = m.Add("IFCPOSTALADDRESS", "$", "$", "$", "$", "(" + S(valor.Trim()) + ")", "$", "$", "$", "$", "$");
            m.SetArg(e.Id, i, "#" + end);
            return 1;
        }

        private int Pset(StepModel m, StepEntity projeto)
        {
            var valores = (Propriedades ?? new Dictionary<string, string>())
                .Where(kv => !DadosEntidadeIfc.Vazio(kv.Key) && !DadosEntidadeIfc.Vazio(kv.Value)).ToList();
            if (valores.Count == 0) return 0;

            // Tira o MetroSP_Projeto anterior do IfcProject (exportação repetida ou já gravado pelo exportador).
            string refProjeto = "#" + projeto.Id;
            foreach (var rel in m.OfType("IFCRELDEFINESBYPROPERTIES").ToList())
            {
                int psetId = StepModel.Ref(rel.Args[5]);
                var pset = psetId == 0 ? null : m.Get(psetId);
                if (pset == null || pset.Type != "IFCPROPERTYSET" || StepModel.Text(pset.Args[2]) != NomePset) continue;
                var alvos = StepModel.RefList(rel.Args[4]);
                if (!alvos.Contains(projeto.Id)) continue;
                if (alvos.Count > 1) { m.SetArg(rel.Id, 4, StepModel.RefListText(alvos.Where(a => a != projeto.Id))); continue; }
                m.Remove(rel.Id);
                // O pset era só do IfcProject: sai junto, com as propriedades (o novo usa o mesmo GlobalId).
                bool usadoEmOutro = m.OfType("IFCRELDEFINESBYPROPERTIES").Any(r => StepModel.Ref(r.Args[5]) == psetId);
                if (!usadoEmOutro)
                {
                    foreach (int v in StepModel.RefList(pset.Args[4]))
                        if (m.Get(v)?.Type == "IFCPROPERTYSINGLEVALUE") m.Remove(v);
                    m.Remove(psetId);
                }
            }

            string gid = StepModel.Text(projeto.Args[0]) ?? "projeto";
            var ids = valores.Select(kv => m.Add("IFCPROPERTYSINGLEVALUE", S(kv.Key.Trim()), "$", "IFCLABEL(" + S(kv.Value.Trim()) + ")", "$")).ToList();
            int novo = m.Add("IFCPROPERTYSET", Guid(gid, "pset-projeto"), "$", S(NomePset), "$", StepModel.RefListText(ids));
            m.Add("IFCRELDEFINESBYPROPERTIES", Guid(gid, "pset-projeto-rel"), "$", "$", "$", "(" + refProjeto + ")", "#" + novo);
            return ids.Count;
        }

        private static string Guid(string semente, string chave) =>
            S(CompressGuid(MD5.HashData(Encoding.UTF8.GetBytes("MetroSP|" + semente + "|" + chave))));

        /// <summary>Texto STEP: aspas duplicadas, barra invertida escapada, não-ASCII em \X2\.</summary>
        private static string S(string text)
        {
            if (text == null) return "$";
            var sb = new StringBuilder("'");
            foreach (char c in text)
            {
                if (c == '\'') sb.Append("''");
                else if (c == '\\') sb.Append("\\\\");
                else if (c >= 32 && c <= 126) sb.Append(c);
                else sb.Append("\\X2\\").Append(((int)c).ToString("X4")).Append("\\X0\\");
            }
            return sb.Append('\'').ToString();
        }

        private const string Base64Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

        private static string CompressGuid(byte[] bytes16)
        {
            var n = new System.Numerics.BigInteger(bytes16, isUnsigned: true, isBigEndian: true);
            var chars = new char[22];
            for (int i = 21; i >= 0; i--)
            {
                chars[i] = Base64Chars[(int)(n & 63)];
                n >>= 6;
            }
            return new string(chars);
        }
    }
}
