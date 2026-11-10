using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;

namespace MetroSP.Ids
{
    /// <summary>Um arquivo de apoio citado pelo setup (.txt de psets ou de mapeamento) e onde ele foi achado.</summary>
    public class ArquivoResolvido
    {
        /// <summary>Caminho gravado no .json (pode ser de outra máquina).</summary>
        public string Original { get; set; }
        /// <summary>Caminho que existe nesta máquina, ou null.</summary>
        public string Caminho { get; set; }
        /// <summary>Como foi achado, para mostrar na janela.</summary>
        public string Situacao { get; set; }
        public bool Encontrado => Caminho != null;
    }

    /// <summary>
    /// Setup de exportação IFC salvo pelo exportador do Revit ("Salvar configuração" → .json).
    /// Repassa cada propriedade ao exportador como opção, do mesmo jeito que o próprio exportador faz
    /// (IFCExportConfiguration.UpdateOptions no projeto revit-ifc da Autodesk).
    /// </summary>
    public class IfcExportSetup
    {
        private static readonly HashSet<string> Ignoradas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Internas da janela do exportador
            "IsBuiltIn", "IsInSession", "ActiveViewId",
            // Modelo de mapeamento guardado no .rvt (Revit 2025+). Sem ele o exportador usa o .txt de psets.
            "PropertyMapping",
            // Tratadas à parte
            "Name", "IFCVersion", "SpaceBoundaries", "SplitWallsAndColumns", "ActivePhaseId",
            "ExportBaseQuantities", "IncludeSteelElements", "VisibleElementsOfCurrentView",
            "ExportUserDefinedPsets", "ExportUserDefinedPsetsFileName",
            "ExportUserDefinedParameterMappingFileName",
            // Fixadas pela MetroSP: coordenadas no PTL (Shared Coordinates) e sem EPSG
            "SitePlacement", "SelectedSite", "GeoRefEPSGCode",
        };

        private readonly Dictionary<string, JsonElement> _props;

        public string Arquivo { get; }
        public string Nome { get; }
        public IFCVersion Versao { get; }
        public bool ExportaPsetsUsuario { get; }
        public bool ExportaMapeamentoParametros { get; }
        public bool SoVisiveisNaVista { get; }

        /// <summary>Id da fase escolhida no setup (-1 = padrão do exportador).</summary>
        public long Fase => _props.TryGetValue("ActivePhaseId", out JsonElement f) && Inteiro(f, out long n) ? n : -1;
        public bool GravaGuid { get; }
        public ArquivoResolvido PropertySets { get; }
        public ArquivoResolvido MapeamentoParametros { get; }

        private IfcExportSetup(string arquivo, Dictionary<string, JsonElement> props, string pastaConfiguracoes)
        {
            Arquivo = arquivo;
            _props = props;
            Nome = Texto("Name");
            if (string.IsNullOrWhiteSpace(Nome)) Nome = Path.GetFileNameWithoutExtension(arquivo);
            Versao = LerVersao();
            ExportaPsetsUsuario = Bool("ExportUserDefinedPsets");
            ExportaMapeamentoParametros = Bool("ExportUserDefinedParameterMapping");
            SoVisiveisNaVista = Bool("VisibleElementsOfCurrentView");
            GravaGuid = Bool("StoreIFCGUID");

            PropertySets = Resolver(Texto("ExportUserDefinedPsetsFileName"), pastaConfiguracoes, FormaPaths.SubPropertySets);
            if (!PropertySets.Encontrado)
            {
                // Sem caminho válido: um .txt com o mesmo nome do .json, ao lado dele
                string vizinho = Path.ChangeExtension(arquivo, ".txt");
                if (File.Exists(vizinho))
                {
                    PropertySets.Caminho = vizinho;
                    PropertySets.Situacao = "mesmo nome do setup";
                }
            }
            MapeamentoParametros = Resolver(Texto("ExportUserDefinedParameterMappingFileName"), pastaConfiguracoes, FormaPaths.SubMapeamentos);
        }

        public static IfcExportSetup Load(string arquivo, string pastaConfiguracoes)
        {
            using (JsonDocument json = JsonDocument.Parse(File.ReadAllText(arquivo),
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
            {
                if (json.RootElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("O arquivo não é um setup de exportação IFC.");
                var props = json.RootElement.EnumerateObject()
                    .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Last().Value.Clone(), StringComparer.OrdinalIgnoreCase);
                if (!props.ContainsKey("IFCVersion"))
                    throw new InvalidDataException("O arquivo não tem IFCVersion: não parece um setup salvo pelo exportador IFC.");
                return new IfcExportSetup(arquivo, props, pastaConfiguracoes);
            }
        }

        /// <summary>Setups (.json): da subpasta Setups da pasta padrão, ou da pasta e subpastas próximas.</summary>
        public static List<string> Procurar(string pasta) => Listar(pasta, FormaPaths.SubSetups, "*.json");

        /// <summary>.txt de psets: da subpasta PropertySets da pasta padrão, ou da pasta e subpastas próximas.</summary>
        public static List<string> ProcurarTxt(string pasta) => Listar(pasta, FormaPaths.SubPropertySets, "*.txt");

        /// <summary>true se a pasta segue a estrutura padrão (Setups, PropertySets, Mapeamentos).</summary>
        public static bool EhPastaPadrao(string pasta) =>
            !string.IsNullOrEmpty(pasta) && Directory.Exists(Path.Combine(pasta, FormaPaths.SubSetups));

        private static List<string> Listar(string pasta, string subpasta, string padrao)
        {
            IEnumerable<string> arquivos = EhPastaPadrao(pasta)
                ? ArquivosNaPasta(Path.Combine(pasta, subpasta), padrao, niveis: 1)
                : ArquivosNaPasta(pasta, padrao);
            return arquivos.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public string DescricaoVersao
        {
            get
            {
                switch (Versao.ToString())
                {
                    case "IFC2x3CV2": return "IFC 2x3 Coordination View 2.0";
                    case "IFC2x3": return "IFC 2x3 Coordination View";
                    case "IFC4": return "IFC 4 Coordination View 2.0";
                    case "IFC4RV": return "IFC 4 Reference View";
                    case "IFC4DTV": return "IFC 4 Design Transfer View";
                    case "IFC4x3": return "IFC 4.3";
                    case "IFCCOBIE": return "IFC 2x3 COBie 2.4";
                    default: return Versao.ToString();
                }
            }
        }

        /// <summary>
        /// Monta as opções do exportador. psets/mapeamento: arquivos escolhidos (null = não usar).
        /// vista: vista que filtra os elementos (InvalidElementId = modelo inteiro).
        /// </summary>
        public IFCExportOptions CriarOpcoes(Document doc, string psets, string mapeamento, ElementId vista, bool epsgEmBranco = true)
        {
            var o = new IFCExportOptions();
            o.FileVersion = Versao;
            o.AddOption("ConfigName", Nome);

            bool filtrarVista = vista != null && vista != ElementId.InvalidElementId;
            o.FilterViewId = filtrarVista ? vista : ElementId.InvalidElementId;
            o.AddOption("VisibleElementsOfCurrentView", filtrarVista.ToString());

            if (_props.TryGetValue("SpaceBoundaries", out JsonElement sb) && Inteiro(sb, out long nivel))
                o.SpaceBoundaryLevel = (int)nivel;
            o.WallAndColumnSplitting = Bool("SplitWallsAndColumns");

            bool quantidades = Bool("ExportBaseQuantities");
            o.ExportBaseQuantities = quantidades;
            o.AddOption("ExportBaseQuantities", quantidades.ToString());

            // A fase só vale para o modelo inteiro, e só se existir neste arquivo
            if (!filtrarVista && _props.TryGetValue("ActivePhaseId", out JsonElement fase) && Inteiro(fase, out long faseId)
                && doc.GetElement(Compat.NovoId(faseId)) is Phase)
                o.AddOption("ActivePhaseId", faseId.ToString(CultureInfo.InvariantCulture));

            // Aço estrutural só no IFC 2x3
            string versao = Versao.ToString();
            bool podeAco = versao == "IFC2x3" || versao == "IFC2x3CV2" || versao == "IFC2x3FM" || versao == "IFC2x3BFM" || versao == "IFCCOBIE";
            o.AddOption("IncludeSteelElements", (podeAco && Bool("IncludeSteelElements")).ToString());

            bool usarPsets = !string.IsNullOrEmpty(psets);
            o.AddOption("ExportUserDefinedPsets", usarPsets.ToString());
            o.AddOption("ExportUserDefinedPsetsFileName", usarPsets ? psets : string.Empty);
            if (!string.IsNullOrEmpty(mapeamento))
                o.AddOption("ExportUserDefinedParameterMappingFileName", mapeamento);

            foreach (KeyValuePair<string, JsonElement> p in _props)
            {
                if (Ignoradas.Contains(p.Key)) continue;
                string valor = ParaOpcao(p.Value);
                if (valor != null) o.AddOption(p.Key, valor);
            }
            if (!string.IsNullOrEmpty(mapeamento)) o.AddOption("ExportUserDefinedParameterMapping", "True");

            // Coordenadas do IFC no PTL: base Shared Coordinates da localização ativa (a mesma do Project Base Point)
            // e EPSG em branco, para o exportador não declarar um sistema projetado que o modelo não usa.
            o.AddOption("SitePlacement", "Shared");
            o.AddOption("SelectedSite", doc.ActiveProjectLocation.Name);
            if (epsgEmBranco) o.AddOption("GeoRefEPSGCode", string.Empty);
            else if (EpsgDoSetup != null) o.AddOption("GeoRefEPSGCode", EpsgDoSetup);
            return o;
        }

        /// <summary>IFC4 (Coordination View, Reference View, Design Transfer View) ou IFC 4.3: as versões que a exportação MetroSP aceita.</summary>
        public bool VersaoAceita => OpcoesSetup.VersaoAceita(Versao.ToString());

        /// <summary>Base de coordenadas gravada no setup, se não for Shared Coordinates (a MetroSP troca por Shared).</summary>
        public string SitePlacementDiferente
        {
            get
            {
                if (!_props.TryGetValue("SitePlacement", out JsonElement v)) return null;
                string s = ParaOpcao(v);
                if (string.IsNullOrEmpty(s) || s == "0" || s.Equals("Shared", StringComparison.OrdinalIgnoreCase)) return null;
                // Valores do enum SiteTransformBasis do exportador revit-ifc
                string[] nomes = { "Shared", "Site", "Project", "Internal", "ProjectInTN", "InternalInTN" };
                return int.TryParse(s, out int n) && n >= 0 && n < nomes.Length ? nomes[n] : s;
            }
        }

        /// <summary>EPSG gravado no setup (a MetroSP exporta sem ele).</summary>
        public string EpsgDoSetup => string.IsNullOrWhiteSpace(Texto("GeoRefEPSGCode")) ? null : Texto("GeoRefEPSGCode");

        /// <summary>Valor como o exportador espera: o ToString() do .NET (bool "True", número na cultura atual).</summary>
        private static string ParaOpcao(JsonElement v)
        {
            switch (v.ValueKind)
            {
                case JsonValueKind.True: return bool.TrueString;
                case JsonValueKind.False: return bool.FalseString;
                case JsonValueKind.String: return v.GetString();
                case JsonValueKind.Number:
                    if (v.TryGetInt64(out long inteiro)) return inteiro.ToString(CultureInfo.CurrentCulture);
                    return v.GetDouble().ToString(CultureInfo.CurrentCulture);
                case JsonValueKind.Object:
                case JsonValueKind.Array:
                    return v.GetRawText();
                default: return null;
            }
        }

        private IFCVersion LerVersao()
        {
            JsonElement v = _props["IFCVersion"];
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) && Enum.IsDefined(typeof(IFCVersion), n)) return (IFCVersion)n;
            if (v.ValueKind == JsonValueKind.String && Enum.TryParse(v.GetString(), true, out IFCVersion e) && Enum.IsDefined(typeof(IFCVersion), e)) return e;
            // Ex.: setup em IFC 4.3 aberto no Revit 2022, cujo exportador vai só até o IFC4.
            if (v.ValueKind == JsonValueKind.Number || v.ValueKind == JsonValueKind.String)
                throw new InvalidDataException("Este setup usa uma versão IFC que este Revit não exporta (" + v.ToString() +
                                               "). Escolha um setup em IFC4 Reference View ou IFC 4.3.");
            throw new InvalidDataException("Versão IFC não reconhecida: " + v.GetRawText());
        }

        private string Texto(string nome) =>
            _props.TryGetValue(nome, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        /// <summary>
        /// Número inteiro gravado como número, texto ou ElementId serializado como objeto
        /// ({"IntegerValue": n} nos exportadores mais antigos, como o do Revit 2022; {"Value": n} nos novos).
        /// </summary>
        private static bool Inteiro(JsonElement v, out long n)
        {
            n = 0;
            switch (v.ValueKind)
            {
                case JsonValueKind.Number:
                    return v.TryGetInt64(out n);
                case JsonValueKind.String:
                    return long.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
                case JsonValueKind.Object:
                    foreach (string nome in new[] { "Value", "IntegerValue" })
                        foreach (JsonProperty p in v.EnumerateObject())
                            if (string.Equals(p.Name, nome, StringComparison.OrdinalIgnoreCase) && Inteiro(p.Value, out n))
                                return true;
                    return false;
                default:
                    return false;
            }
        }

        private bool Bool(string nome) =>
            _props.TryGetValue(nome, out JsonElement v) && v.ValueKind == JsonValueKind.True;

        /// <summary>
        /// Ordem: na pasta padrão, o .txt com o mesmo nome do setup (Setups\Civil.json → PropertySets\Civil.txt);
        /// o caminho gravado no .json, que é da máquina de quem salvou; um arquivo com o mesmo nome desse caminho
        /// ao lado do setup ou na pasta de configurações.
        /// </summary>
        private ArquivoResolvido Resolver(string original, string pastaConfiguracoes, string subpasta)
        {
            var r = new ArquivoResolvido { Original = original };
            if (EhPastaPadrao(pastaConfiguracoes))
            {
                string padrao = Path.Combine(pastaConfiguracoes, subpasta, Path.GetFileNameWithoutExtension(Arquivo) + ".txt");
                if (File.Exists(padrao))
                {
                    r.Caminho = padrao;
                    r.Situacao = $"pasta padrão: {subpasta}\\{Path.GetFileName(padrao)}";
                    return r;
                }
            }
            if (string.IsNullOrWhiteSpace(original))
            {
                r.Situacao = "não indicado no setup";
                return r;
            }
            if (File.Exists(original))
            {
                r.Caminho = original;
                r.Situacao = "caminho do setup";
                return r;
            }
            string nome = original.Replace('/', '\\').Split('\\').Last();
            foreach (string pasta in new[] { Path.GetDirectoryName(Arquivo), pastaConfiguracoes })
            {
                string achado = ArquivosNaPasta(pasta, nome).FirstOrDefault();
                if (achado != null)
                {
                    r.Caminho = achado;
                    r.Situacao = "achado pelo nome na pasta de configurações";
                    return r;
                }
            }
            r.Situacao = "não encontrado: " + original;
            return r;
        }

        /// <summary>
        /// Busca até 3 níveis abaixo. No Desktop Connector cada pasta listada é uma consulta à nuvem,
        /// então não vale descer a árvore inteira de um projeto.
        /// </summary>
        private static IEnumerable<string> ArquivosNaPasta(string pasta, string padrao, int niveis = 3)
        {
            if (string.IsNullOrEmpty(pasta) || !Directory.Exists(pasta)) yield break;
            var fila = new Queue<(string, int)>();
            fila.Enqueue((pasta, 0));
            int pastas = 0;
            while (fila.Count > 0 && pastas++ < 300)
            {
                (string atual, int nivel) = fila.Dequeue();
                string[] arquivos, subpastas;
                try
                {
                    arquivos = Directory.GetFiles(atual, padrao);
                    subpastas = nivel < niveis ? Directory.GetDirectories(atual) : new string[0];
                }
                catch { continue; }
                foreach (string a in arquivos) yield return a;
                foreach (string s in subpastas) fila.Enqueue((s, nivel + 1));
            }
        }
    }
}
