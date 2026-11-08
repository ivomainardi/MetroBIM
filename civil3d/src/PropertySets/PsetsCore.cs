using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using MetroSP.Config;
using MetroSP.Planilha;
using AecDataType = Autodesk.Aec.PropertyData.DataType;
using Civil = Autodesk.Civil.DatabaseServices;

namespace MetroSP.PropertySets
{
    public enum TipoPropriedade { Texto, Inteiro, Real, SimNao }

    /// <summary>Uma propriedade na lista de montagem (vira PropertyDefinition no desenho).</summary>
    public class PropriedadeMontagem
    {
        public string Nome { get; set; } = "";
        public TipoPropriedade Tipo { get; set; } = TipoPropriedade.Texto;
        public string Padrao { get; set; } = "";
        /// <summary>Situação em relação ao desenho (preenchida por Comparar).</summary>
        public string Situacao { get; set; } = "";
        /// <summary>Calculada pelo Civil 3D (fórmula, automática): só leitura aqui.</summary>
        public bool Automatica { get; set; }

        public string TipoTexto
        {
            get => PsetsCore.NomeTipo(Tipo);
            set => Tipo = PsetsCore.LerTipo(value);
        }

        public PropriedadeMontagem Clonar() => (PropriedadeMontagem)MemberwiseClone();
    }

    /// <summary>Um property set na lista de montagem (vira PropertySetDefinition no desenho).</summary>
    public class PsetMontagem
    {
        public string Nome { get; set; } = "";
        public string Descricao { get; set; } = "";
        public bool TodosObjetos { get; set; } = true;
        /// <summary>Classes do Civil 3D/AutoCAD (nome RX, ex.: AeccDbAlignment) quando não vale para todos.</summary>
        public List<string> Classes { get; set; } = new List<string>();
        public List<PropriedadeMontagem> Propriedades { get; set; } = new List<PropriedadeMontagem>();
        /// <summary>Acrescenta o property set ao mapeamento de propriedades do exportador IFC.</summary>
        public bool LevarIfc { get; set; } = true;
        public string EntidadeIfc { get; set; } = "IfcElement";

        public string Situacao { get; set; } = "";
        public bool NoDesenho { get; set; }

        public int NumeroPropriedades => Propriedades.Count;
        public string AplicaA => TodosObjetos || Classes.Count == 0 ? "Todos os objetos" : string.Join(", ", Classes.Select(PsetsCore.NomeClasse));
        public string Ifc => LevarIfc ? EntidadeIfc : "Não";
    }

    /// <summary>Um objeto do desenho na aba Anexar.</summary>
    public class ObjetoAnexar
    {
        public ObjectId Id { get; set; }
        public string Tipo { get; set; }
        public string Classe { get; set; }
        public string Nome { get; set; }
        public string Layer { get; set; }
        public string Anexados { get; set; }
        public string Faltam { get; set; }
        public HashSet<string> Linhagem { get; set; }
        public HashSet<string> Nomes { get; set; }
    }

    /// <summary>
    /// Monta Property Set Definitions do Civil 3D (as mesmas do Style Manager): lê do desenho, compara, cria/completa,
    /// importa/exporta planilha, leva ao mapeamento do exportador IFC e anexa aos objetos que vão para o IFC.
    /// </summary>
    public static class PsetsCore
    {
        // ------------------------------------------------------------------ tipos

        public static readonly string[] NomesTipos = { "Texto", "Inteiro", "Real", "Sim/Não" };

        public static string NomeTipo(TipoPropriedade t) => NomesTipos[(int)t];

        public static TipoPropriedade LerTipo(string s)
        {
            string n = MapaColunas.Normalizar(s);
            if (n.Length == 0) return TipoPropriedade.Texto;
            if (n.StartsWith("int") || n == "integer" || n == "inteiro" || n == "numero inteiro" || n == "ifcinteger" || n == "ifccountmeasure") return TipoPropriedade.Inteiro;
            if (n.StartsWith("real") || n == "decimal" || n == "double" || n == "numero" || n == "ifcreal" || n.Contains("measure")) return TipoPropriedade.Real;
            if (n.StartsWith("sim") || n.StartsWith("bool") || n.StartsWith("true") || n == "logico" || n == "ifcboolean" || n == "ifclogical" || n == "yes no") return TipoPropriedade.SimNao;
            return TipoPropriedade.Texto;
        }

        static AecDataType ParaAec(TipoPropriedade t) => t switch
        {
            TipoPropriedade.Inteiro => AecDataType.Integer,
            TipoPropriedade.Real => AecDataType.Real,
            TipoPropriedade.SimNao => AecDataType.TrueFalse,
            _ => AecDataType.Text,
        };

        static TipoPropriedade DeAec(AecDataType t) => t switch
        {
            AecDataType.Integer => TipoPropriedade.Inteiro,
            AecDataType.AutoIncrement => TipoPropriedade.Inteiro,
            AecDataType.Real => TipoPropriedade.Real,
            AecDataType.TrueFalse => TipoPropriedade.SimNao,
            _ => TipoPropriedade.Texto,
        };

        public static string MedidaIfc(TipoPropriedade t) => t switch
        {
            TipoPropriedade.Inteiro => "IfcInteger",
            TipoPropriedade.Real => "IfcReal",
            TipoPropriedade.SimNao => "IfcBoolean",
            _ => "IfcLabel",
        };

        static object ValorPadrao(TipoPropriedade t, string s)
        {
            s = (s ?? "").Trim();
            switch (t)
            {
                case TipoPropriedade.Inteiro:
                    return int.TryParse(s, NumberStyles.Integer, CultureInfo.CurrentCulture, out int i) || int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out i) ? i : 0;
                case TipoPropriedade.Real:
                    return double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out double d) || double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0.0;
                case TipoPropriedade.SimNao:
                    string n = MapaColunas.Normalizar(s);
                    return n == "sim" || n == "true" || n == "1" || n == "yes" || n == "verdadeiro";
                default:
                    return s;
            }
        }

        static string TextoPadrao(object v) => v switch
        {
            null => "",
            bool b => b ? "Sim" : "Não",
            double d => d.ToString(CultureInfo.CurrentCulture),
            _ => Convert.ToString(v, CultureInfo.CurrentCulture),
        };

        // ------------------------------------------------------------------ classes (Aplica-se a)

        /// <summary>Classes mais usadas, com nome amigável. O nome RX vem da própria API, para não errar a grafia.</summary>
        static readonly (string Nome, Func<Type> Tipo)[] Comuns =
        {
            ("Alinhamento", () => typeof(Civil.Alignment)),
            ("Corredor", () => typeof(Civil.Corridor)),
            ("Superfície TIN", () => typeof(Civil.TinSurface)),
            ("Superfície grid", () => typeof(Civil.GridSurface)),
            ("Linha característica", () => typeof(Civil.FeatureLine)),
            ("Ponto COGO", () => typeof(Civil.CogoPoint)),
            ("Tubo (gravidade)", () => typeof(Civil.Pipe)),
            ("Estrutura (gravidade)", () => typeof(Civil.Structure)),
            ("Tubo de pressão", () => typeof(Civil.PressurePipe)),
            ("Conexão de pressão", () => typeof(Civil.PressureFitting)),
            ("Acessório de pressão", () => typeof(Civil.PressureAppurtenance)),
            ("Sólido 3D", () => typeof(Solid3d)),
            ("Bloco", () => typeof(BlockReference)),
            ("Polilinha 3D", () => typeof(Polyline3d)),
            ("Polilinha", () => typeof(Polyline)),
        };

        static Dictionary<string, string> _nomes;

        static Dictionary<string, string> Nomes()
        {
            if (_nomes != null) return _nomes;
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (nome, tipo) in Comuns)
            {
                try { string rx = RXObject.GetClass(tipo()).Name; if (!d.ContainsKey(rx)) d[rx] = nome; } catch { }
            }
            return _nomes = d;
        }

        public static string NomeClasse(string rx) => Nomes().TryGetValue(rx ?? "", out string n) ? n : rx;

        /// <summary>Classe RX a partir do nome amigável ou do próprio nome RX.</summary>
        public static string ClassePorNome(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) return null;
            foreach (var kv in Nomes())
                if (string.Equals(kv.Value, s, StringComparison.CurrentCultureIgnoreCase) || string.Equals(kv.Key, s, StringComparison.OrdinalIgnoreCase))
                    return kv.Key;
            string n = MapaColunas.Normalizar(s);
            foreach (var kv in Nomes())
                if (MapaColunas.Normalizar(kv.Value) == n) return kv.Key;
            return s;
        }

        /// <summary>Opções do "Aplica-se a": as comuns primeiro, depois as do desenho e as que o Civil 3D aceita.</summary>
        public static List<(string Rx, string Nome)> OpcoesClasses(IEnumerable<string> doDesenho)
        {
            var r = Nomes().Select(kv => (kv.Key, kv.Value)).ToList();
            var vistos = new HashSet<string>(r.Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
            var extras = new List<string>(doDesenho ?? Enumerable.Empty<string>());
            try { extras.AddRange(PropertyDataServices.FindEligibleClassNames().Where(c => c.StartsWith("AeccDb", StringComparison.OrdinalIgnoreCase))); } catch { }
            foreach (string c in extras.OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
                if (!string.IsNullOrEmpty(c) && vistos.Add(c)) r.Add((c, c));
            return r;
        }

        /// <summary>Classe do objeto e as classes-mãe (o filtro do property set vale para subclasses).</summary>
        public static HashSet<string> Linhagem(DBObject o)
        {
            var h = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (RXClass c = o.GetRXClass(); c != null; c = c.MyParent) h.Add(c.Name);
            return h;
        }

        public static bool Aplica(PropertySetDefinition def, HashSet<string> linhagem)
        {
            if (def.AppliesToAll) return true;
            foreach (string c in def.AppliesToFilter) if (linhagem.Contains(c)) return true;
            return false;
        }

        // ------------------------------------------------------------------ desenho

        /// <summary>Property sets de objeto definidos no desenho (os de estilo ficam de fora).</summary>
        public static List<PsetMontagem> LerDesenho(Database db)
        {
            var lista = new List<PsetMontagem>();
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var dict = new DictionaryPropertySetDefinitions(db);
                foreach (ObjectId id in dict.Records)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is PropertySetDefinition def) || def.IsStyleBased) continue;
                    lista.Add(DeDefinicao(dict.NameAt(id), def));
                }
                tr.Commit();
            }
            return lista.OrderBy(p => p.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        static PsetMontagem DeDefinicao(string nome, PropertySetDefinition def)
        {
            var p = new PsetMontagem
            {
                Nome = nome,
                Descricao = def.Description ?? "",
                TodosObjetos = def.AppliesToAll,
                Classes = def.AppliesToAll ? new List<string>() : def.AppliesToFilter.Cast<string>().ToList(),
                NoDesenho = true,
                LevarIfc = false,
            };
            var defs = Enumerable.Range(0, def.Definitions.Count).Select(i => def.Definitions[i]).OrderBy(d => d.DisplayOrder);
            foreach (PropertyDefinition d in defs)
                p.Propriedades.Add(new PropriedadeMontagem { Nome = d.Name, Tipo = DeAec(d.DataType), Padrao = TextoPadrao(d.DefaultData), Automatica = d.Automatic });
            return p;
        }

        /// <summary>Preenche a situação de cada property set e propriedade em relação ao desenho.</summary>
        public static void Comparar(Database db, IEnumerable<PsetMontagem> lista)
        {
            var noDesenho = LerDesenho(db).ToDictionary(p => p.Nome, StringComparer.OrdinalIgnoreCase);
            foreach (PsetMontagem p in lista)
            {
                noDesenho.TryGetValue(p.Nome ?? "", out PsetMontagem d);
                p.NoDesenho = d != null;
                int novas = 0, tipos = 0;
                foreach (PropriedadeMontagem pr in p.Propriedades)
                {
                    PropriedadeMontagem e = d?.Propriedades.FirstOrDefault(x => string.Equals(x.Nome, pr.Nome, StringComparison.OrdinalIgnoreCase));
                    if (e == null) { pr.Situacao = "Nova"; novas++; }
                    else if (e.Tipo != pr.Tipo && !e.Automatica) { pr.Situacao = "Tipo diferente no desenho: " + NomeTipo(e.Tipo); tipos++; }
                    else pr.Situacao = e.Automatica ? "No desenho (automática)" : "No desenho";
                }
                bool todos = p.TodosObjetos || p.Classes.Count == 0;
                bool aplica = d != null && (d.TodosObjetos != todos ||
                    (!todos && !new HashSet<string>(d.Classes, StringComparer.OrdinalIgnoreCase).SetEquals(p.Classes)));
                bool descr = d != null && !string.Equals(d.Descricao ?? "", p.Descricao ?? "", StringComparison.CurrentCulture);
                if (d == null) p.Situacao = "Novo";
                else
                {
                    var partes = new List<string>();
                    if (novas > 0) partes.Add(novas == 1 ? "1 propriedade nova" : novas + " propriedades novas");
                    if (aplica) partes.Add("aplica-se a outro");
                    if (descr) partes.Add("descrição");
                    if (tipos > 0) partes.Add(tipos == 1 ? "1 tipo diferente" : tipos + " tipos diferentes");
                    p.Situacao = partes.Count == 0 ? "No desenho" : "Muda: " + string.Join(", ", partes);
                }
            }
        }

        /// <summary>
        /// Cria ou completa as definições no desenho (Style Manager › Property Set Definitions). Nunca apaga propriedades
        /// nem muda o tipo de uma que já existe (os valores gravados nos objetos se perderiam).
        /// </summary>
        public static string CriarNoDesenho(Database db, IEnumerable<PsetMontagem> lista)
        {
            var sb = new StringBuilder();
            int criados = 0, alterados = 0, props = 0;
            var avisos = new List<string>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var dict = new DictionaryPropertySetDefinitions(db);
                foreach (PsetMontagem p in lista)
                {
                    string nome = (p.Nome ?? "").Trim();
                    if (nome.Length == 0) continue;
                    PropertySetDefinition def;
                    bool novo = !dict.Has(nome, tr);
                    if (novo)
                    {
                        def = new PropertySetDefinition();
                        def.SetToStandard(db);
                        def.SubSetDatabaseDefaults(db);
                        dict.AddNewRecord(nome, def);
                        tr.AddNewlyCreatedDBObject(def, true);
                        criados++;
                    }
                    else
                    {
                        def = (PropertySetDefinition)tr.GetObject(dict.GetAt(nome), OpenMode.ForWrite);
                        if (def.IsStyleBased) { avisos.Add($"{nome}: é property set de estilo no desenho; não foi alterado."); continue; }
                    }

                    bool mudou = false;
                    if (!string.Equals(def.Description ?? "", p.Descricao ?? "", StringComparison.CurrentCulture)) { def.Description = p.Descricao ?? ""; mudou = true; }
                    var classes = p.Classes.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    bool todos = p.TodosObjetos || classes.Count == 0;
                    var atual = new HashSet<string>(def.AppliesToFilter.Cast<string>(), StringComparer.OrdinalIgnoreCase);
                    if (novo || def.AppliesToAll != todos || (!todos && !atual.SetEquals(classes)))
                    {
                        var filtro = new StringCollection();
                        if (!todos) filtro.AddRange(classes.ToArray());
                        def.SetAppliesToFilter(filtro, false);
                        def.AppliesToAll = todos;
                        mudou = true;
                    }

                    var existentes = Enumerable.Range(0, def.Definitions.Count).Select(i => def.Definitions[i]).ToList();
                    foreach (PropriedadeMontagem pr in p.Propriedades)
                    {
                        string pn = (pr.Nome ?? "").Trim();
                        if (pn.Length == 0) continue;
                        PropertyDefinition e = existentes.FirstOrDefault(x => string.Equals(x.Name, pn, StringComparison.OrdinalIgnoreCase));
                        if (e != null)
                        {
                            if (!e.Automatic && DeAec(e.DataType) != pr.Tipo)
                                avisos.Add($"{nome} › {pn}: já é {NomeTipo(DeAec(e.DataType))} no desenho; o tipo não foi mudado.");
                            continue;
                        }
                        var pd = new PropertyDefinition();
                        pd.SetToStandard(db);
                        pd.SubSetDatabaseDefaults(db);
                        pd.Name = pn;
                        pd.DataType = ParaAec(pr.Tipo);
                        pd.DefaultData = ValorPadrao(pr.Tipo, pr.Padrao);
                        def.Definitions.Add(pd);
                        existentes.Add(pd);
                        props++;
                        mudou = true;
                    }
                    if (mudou && !novo) alterados++;
                }
                tr.Commit();
            }
            sb.Append($"{criados} property set(s) criado(s), {alterados} atualizado(s), {props} propriedade(s) acrescentada(s).");
            sb.Append("\nEstão no Style Manager › Property Set Definitions.");
            if (avisos.Count > 0)
            {
                sb.Append("\n\n" + (avisos.Count == 1 ? "1 aviso:" : avisos.Count + " avisos:"));
                foreach (string a in avisos.Take(15)) sb.Append("\n•  " + a);
                if (avisos.Count > 15) sb.Append($"\n•  e mais {avisos.Count - 15}");
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ anexar

        /// <summary>Objetos que vão para o IFC (seleção ou desenho todo), com os property sets já anexados.</summary>
        public static List<ObjetoAnexar> Objetos(Database db, IEnumerable<ObjectId> selecao)
        {
            var r = new List<ObjetoAnexar>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in Ifc.FiltroIfc.Objetos(db, tr, selecao))
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                    var nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        foreach (ObjectId ps in PropertyDataServices.GetPropertySets(e))
                            if (tr.GetObject(ps, OpenMode.ForRead) is PropertySet s) nomes.Add(s.PropertySetDefinitionName);
                    }
                    catch { }
                    string classe = e.GetRXClass().Name;
                    r.Add(new ObjetoAnexar
                    {
                        Id = id,
                        Classe = classe,
                        Tipo = NomeClasse(classe) is string n && n != classe ? n : e.GetType().Name,
                        Nome = NomeObjeto(e, tr),
                        Layer = e.Layer,
                        Linhagem = Linhagem(e),
                        Nomes = nomes,
                        Anexados = string.Join(", ", nomes.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)),
                    });
                }
                tr.Commit();
            }
            return r;
        }

        static string NomeObjeto(Entity e, Transaction tr)
        {
            try
            {
                if (e is BlockReference br) return Ifc.FiltroIfc.NomeBloco(br, tr);
                var p = e.GetType().GetProperty("Name");
                if (p != null && p.PropertyType == typeof(string)) return (string)p.GetValue(e) ?? "";
            }
            catch { }
            return "";
        }

        /// <summary>Recalcula a coluna "Falta anexar" com os property sets escolhidos.</summary>
        public static void CalcularFaltas(Database db, IEnumerable<ObjetoAnexar> objetos, ICollection<string> escolhidos)
        {
            var defs = new Dictionary<string, (bool Todos, HashSet<string> Classes)>(StringComparer.OrdinalIgnoreCase);
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var dict = new DictionaryPropertySetDefinitions(db);
                foreach (string n in escolhidos)
                    if (dict.Has(n, tr) && tr.GetObject(dict.GetAt(n), OpenMode.ForRead) is PropertySetDefinition d)
                        defs[n] = (d.AppliesToAll, new HashSet<string>(d.AppliesToFilter.Cast<string>(), StringComparer.OrdinalIgnoreCase));
                tr.Commit();
            }
            foreach (ObjetoAnexar o in objetos)
                o.Faltam = string.Join(", ", defs.Where(kv => !o.Nomes.Contains(kv.Key) && (kv.Value.Todos || kv.Value.Classes.Overlaps(o.Linhagem)))
                    .Select(kv => kv.Key).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase));
        }

        /// <summary>Anexa os property sets que faltam (só os que se aplicam à classe de cada objeto).</summary>
        public static string Anexar(Database db, IEnumerable<ObjetoAnexar> objetos)
        {
            int ok = 0, objs = 0;
            var falhas = new List<string>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var dict = new DictionaryPropertySetDefinitions(db);
                foreach (ObjetoAnexar o in objetos)
                {
                    var faltam = (o.Faltam ?? "").Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                    if (faltam.Length == 0) continue;
                    DBObject obj;
                    try { obj = tr.GetObject(o.Id, OpenMode.ForWrite); }
                    catch (System.Exception ex) { falhas.Add($"{o.Tipo} {o.Nome}: {ex.Message}"); continue; }
                    bool algum = false;
                    foreach (string n in faltam)
                    {
                        try
                        {
                            PropertyDataServices.AddPropertySet(obj, dict.GetAt(n));
                            o.Nomes.Add(n);
                            ok++;
                            algum = true;
                        }
                        catch (System.Exception ex) { falhas.Add($"{o.Tipo} {o.Nome} › {n}: {ex.Message}"); }
                    }
                    if (algum) objs++;
                    o.Anexados = string.Join(", ", o.Nomes.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase));
                    o.Faltam = "";
                }
                tr.Commit();
            }
            var sb = new StringBuilder($"{ok} property set(s) anexado(s) em {objs} objeto(s).");
            if (falhas.Count > 0)
            {
                sb.Append($"\n\n{falhas.Count} não puderam ser anexados:");
                foreach (string f in falhas.Take(15)) sb.Append("\n•  " + f);
                if (falhas.Count > 15) sb.Append($"\n•  e mais {falhas.Count - 15}");
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ planilha

        public const string CampoPset = "pset", CampoProp = "propriedade", CampoTipo = "tipo", CampoPadrao = "padrao",
            CampoAplica = "aplica", CampoDescricao = "descricao", CampoIfc = "ifc";

        public static readonly CampoPlanilha[] Campos =
        {
            new CampoPlanilha(CampoPset, "Property set", true, "Nome do property set.", "property set", "pset", "propertyset", "conjunto", "ifcpropertyset name", "grupo"),
            new CampoPlanilha(CampoProp, "Propriedade", true, "Nome da propriedade.", "propriedade", "property", "parametro", "parâmetro", "ifcproperty name", "nome"),
            new CampoPlanilha(CampoTipo, "Tipo", false, "Texto, Inteiro, Real ou Sim/Não (também aceita IfcLabel, IfcReal...).", "tipo", "type", "data type", "tipo de dado", "primarymeasuretype"),
            new CampoPlanilha(CampoPadrao, "Valor padrão", false, "Valor inicial ao anexar.", "valor padrao", "padrao", "default", "valor"),
            new CampoPlanilha(CampoAplica, "Aplica-se a", false, "Tipos de objeto separados por ; (ex.: Alinhamento; Corredor). Vazio ou \"Todos\" = todos.", "aplica se a", "aplica a", "applies to", "objetos", "classes"),
            new CampoPlanilha(CampoDescricao, "Descrição", false, "Descrição do property set.", "descricao", "description"),
            new CampoPlanilha(CampoIfc, "IFC", false, "Entidade IFC (ex.: IfcElement) para levar ao IFC; \"Não\" para não levar.", "ifc", "entidade ifc", "applicableentities", "levar para o ifc"),
        };

        /// <summary>Se o arquivo é o IFCInfraExportPropertyMapping.csv da Autodesk, devolve os property sets dele (Source = PropertySet).</summary>
        public static List<PsetMontagem> SeMapeamentoAutodesk(string arquivo)
        {
            Tabela t = LeitorPlanilha.Ler(arquivo);
            List<List<string>> linhas = t.Linhas(null);
            int cab = MapaColunas.AcharCabecalho(t, null, Campos);
            List<string> titulos = cab - 1 < linhas.Count ? linhas[cab - 1] : new List<string>();
            int iSource = titulos.FindIndex(c => MapaColunas.Normalizar(c) == "source");
            int iCivil = titulos.FindIndex(c => MapaColunas.Normalizar(c) == "civil 3d name");
            int iGroup = titulos.FindIndex(c => MapaColunas.Normalizar(c) == "group");
            return iSource >= 0 && iCivil >= 0 && iGroup >= 0 ? LerMapeamentoAutodesk(linhas.Skip(cab).ToList(), iSource, iGroup, iCivil) : null;
        }

        /// <summary>
        /// Lê uma planilha (uma linha por propriedade) com as colunas sugeridas pelos títulos. Também aceita o
        /// IFCInfraExportPropertyMapping.csv da Autodesk.
        /// </summary>
        public static List<PsetMontagem> LerPlanilha(string arquivo, out string aviso)
        {
            aviso = null;
            List<PsetMontagem> autodesk = SeMapeamentoAutodesk(arquivo);
            if (autodesk != null) return autodesk;
            Tabela t = LeitorPlanilha.Ler(arquivo);
            var mapa = new MapaColunas { LinhaCabecalho = MapaColunas.AcharCabecalho(t, null, Campos) };
            mapa.Sugerir(t, Campos);
            var faltam = mapa.Faltando(Campos);
            if (faltam.Count > 0)
                throw new InvalidDataException("A planilha precisa das colunas: " + string.Join(", ", faltam.Select(f => f.Rotulo)) + ".");
            var r = DeLinhas(mapa.Linhas(t), out int ignoradas);
            if (ignoradas > 0) aviso = $"{ignoradas} linha(s) sem property set ou propriedade foram ignoradas.";
            return r;
        }

        /// <summary>
        /// Monta a estrutura (property sets, propriedades, tipos, aplica-se a) a partir das linhas da planilha, uma por propriedade.
        /// Só a estrutura: nenhum valor é gravado nos objetos.
        /// </summary>
        public static List<PsetMontagem> DeLinhas(IEnumerable<LinhaPlanilha> linhas, out int ignoradas)
        {
            var r = new List<PsetMontagem>();
            ignoradas = 0;
            foreach (LinhaPlanilha l in linhas)
            {
                string ps = l[CampoPset], pr = l[CampoProp];
                if (string.IsNullOrWhiteSpace(ps) || string.IsNullOrWhiteSpace(pr)) { ignoradas++; continue; }
                PsetMontagem p = r.FirstOrDefault(x => string.Equals(x.Nome, ps.Trim(), StringComparison.OrdinalIgnoreCase));
                if (p == null) r.Add(p = new PsetMontagem { Nome = ps.Trim() });
                if (!string.IsNullOrWhiteSpace(l[CampoDescricao]) && string.IsNullOrEmpty(p.Descricao)) p.Descricao = l[CampoDescricao];
                string ap = l[CampoAplica];
                if (!string.IsNullOrWhiteSpace(ap) && MapaColunas.Normalizar(ap) != "todos" && MapaColunas.Normalizar(ap) != "todos os objetos")
                {
                    p.TodosObjetos = false;
                    foreach (string c in ap.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string rx = ClassePorNome(c);
                        if (rx != null && !p.Classes.Contains(rx, StringComparer.OrdinalIgnoreCase)) p.Classes.Add(rx);
                    }
                }
                string ifc = l[CampoIfc];
                if (!string.IsNullOrWhiteSpace(ifc))
                {
                    string n = MapaColunas.Normalizar(ifc);
                    if (n == "nao" || n == "false" || n == "0") p.LevarIfc = false;
                    else if (ifc.Trim().StartsWith("Ifc", StringComparison.OrdinalIgnoreCase)) p.EntidadeIfc = ifc.Trim();
                }
                if (p.Propriedades.Any(x => string.Equals(x.Nome, pr.Trim(), StringComparison.OrdinalIgnoreCase))) continue;
                p.Propriedades.Add(new PropriedadeMontagem { Nome = pr.Trim(), Tipo = LerTipo(l[CampoTipo]), Padrao = l[CampoPadrao] ?? "" });
            }
            return r;
        }

        // ------------------------------------------------------------------ colunas lembradas

        static readonly string ArquivoColunas = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MetroSP", "Civil3D", "PropertySets", "colunas-planilha.json");

        /// <summary>Colunas indicadas na última importação (o padrão da equipe), ou um mapa vazio.</summary>
        public static MapaColunas ColunasLembradas()
        {
            try
            {
                if (File.Exists(ArquivoColunas))
                    return System.Text.Json.JsonSerializer.Deserialize<MapaColunas>(File.ReadAllText(ArquivoColunas, Encoding.UTF8)) ?? new MapaColunas();
            }
            catch { }
            return new MapaColunas();
        }

        public static void LembrarColunas(MapaColunas mapa)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ArquivoColunas));
                File.WriteAllText(ArquivoColunas, System.Text.Json.JsonSerializer.Serialize(mapa, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            }
            catch { }
        }

        static List<PsetMontagem> LerMapeamentoAutodesk(List<List<string>> linhas, int iSource, int iGroup, int iCivil)
        {
            string At(List<string> l, int i) => i < l.Count ? (l[i] ?? "").Trim() : "";
            var r = new List<PsetMontagem>();
            foreach (List<string> l in linhas)
            {
                if (!string.Equals(At(l, iSource), "PropertySet", StringComparison.OrdinalIgnoreCase)) continue;
                string ps = At(l, iGroup), pr = At(l, iCivil);
                if (ps.Length == 0 || pr.Length == 0) continue;
                PsetMontagem p = r.FirstOrDefault(x => string.Equals(x.Nome, ps, StringComparison.OrdinalIgnoreCase));
                if (p == null) r.Add(p = new PsetMontagem { Nome = ps, LevarIfc = false });
                if (!p.Propriedades.Any(x => string.Equals(x.Nome, pr, StringComparison.OrdinalIgnoreCase)))
                    p.Propriedades.Add(new PropriedadeMontagem { Nome = pr });
            }
            return r;
        }

        /// <summary>Property sets que o mapeamento IFC ativo espera encontrar no desenho (Source = PropertySet).</summary>
        public static List<PsetMontagem> DoMapeamentoIfc()
        {
            string csv = ConfigPackage.PathOf(ConfigKind.PropertyMappingCsv);
            if (!File.Exists(csv)) return new List<PsetMontagem>();
            var lista = LerPlanilha(csv, out _);
            // Tipos vêm dos modelos do JSON, quando houver.
            try
            {
                foreach (JsonNode tpl in Templates(ConfigPackage.LoadJson(ConfigKind.PropertyTemplates)) ?? new JsonArray())
                {
                    PsetMontagem p = lista.FirstOrDefault(x => string.Equals(x.Nome, (string)tpl?["Name"], StringComparison.OrdinalIgnoreCase));
                    if (p == null || !(tpl["PropertyTemplates"] is JsonArray props)) continue;
                    foreach (JsonNode pt in props)
                    {
                        PropriedadeMontagem pr = p.Propriedades.FirstOrDefault(x => string.Equals(x.Nome, (string)pt?["Name"], StringComparison.OrdinalIgnoreCase));
                        if (pr != null) pr.Tipo = LerTipo((string)pt["PrimaryMeasureType"]);
                    }
                }
            }
            catch { }
            return lista;
        }

        /// <summary>Salva a lista numa planilha .csv (UTF-8, separador ;), que abre direto no Excel.</summary>
        public static void ExportarPlanilha(string arquivo, IEnumerable<PsetMontagem> lista)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(";", Campos.Select(c => c.Rotulo)));
            string Q(string s) => (s ?? "").IndexOfAny(new[] { ';', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s ?? "";
            foreach (PsetMontagem p in lista)
            {
                string aplica = p.TodosObjetos || p.Classes.Count == 0 ? "Todos" : string.Join("; ", p.Classes.Select(NomeClasse));
                foreach (PropriedadeMontagem pr in p.Propriedades.Where(x => !x.Automatica))
                    sb.AppendLine(string.Join(";", new[] { p.Nome, pr.Nome, NomeTipo(pr.Tipo), pr.Padrao, aplica, p.Descricao, p.LevarIfc ? p.EntidadeIfc : "Não" }.Select(Q)));
            }
            File.WriteAllText(arquivo, sb.ToString(), new UTF8Encoding(true));
        }

        // ------------------------------------------------------------------ mapeamento IFC

        static JsonArray Templates(JsonNode raiz) => raiz as JsonArray ?? raiz?["PropertySetTemplates"] as JsonArray;

        /// <summary>
        /// Acrescenta os property sets ao mapeamento ativo do exportador IFC da Autodesk: o modelo do Pset
        /// (IFCInfraExportPropertyMapping.json) e de onde vem cada propriedade (IFCInfraExportPropertyMapping.csv, Source = PropertySet).
        /// O que já está no mapeamento fica como está.
        /// </summary>
        public static string LevarParaIfc(IEnumerable<PsetMontagem> lista)
        {
            var psets = lista.Where(p => p.LevarIfc && !string.IsNullOrWhiteSpace(p.Nome)).ToList();
            if (psets.Count == 0) return null;
            ConfigPackage.EnsureActive();

            JsonNode raiz = ConfigPackage.LoadJson(ConfigKind.PropertyTemplates) ?? new JsonObject();
            string caminho = ConfigPackage.PathOf(ConfigKind.PropertyMappingCsv);
            var linhas = File.Exists(caminho) ? File.ReadAllLines(caminho).ToList() : new List<string>();
            var (novosTpl, novasProps, novasLinhas) = Mesclar(psets, raiz, linhas);
            ConfigPackage.SaveJson(ConfigKind.PropertyTemplates, raiz);
            ConfigPackage.Write(ConfigKind.PropertyMappingCsv, new UTF8Encoding(true).GetBytes(string.Join("\r\n", linhas) + "\r\n"));

            if (novosTpl + novasProps + novasLinhas == 0) return "O mapeamento IFC já tinha esses property sets.";
            return $"Mapeamento IFC: {novosTpl} Pset(s) e {novasProps} propriedade(s) novos (Configurar IFC › Property sets).";
        }

        /// <summary>
        /// Na exportação: acrescenta às cópias do mapeamento que o exportador da Autodesk vai ler (as que o plugin acabou
        /// de gravar para o desenho) todos os property sets anexados a objetos que vão para o IFC. Sem isso, o
        /// IFCINFRAEXPORT só leva os property sets que já estavam no mapeamento. O pacote ativo não muda.
        /// Devolve os nomes dos property sets que entraram ou ganharam propriedades.
        /// </summary>
        public static List<string> CompletarMapeamentoNaExportacao(Database db, string jsonMapeamento, string csvMapeamento)
        {
            List<PsetMontagem> anexados = AnexadosAoIfc(db);
            if (anexados.Count == 0 || jsonMapeamento == null || csvMapeamento == null) return new List<string>();

            JsonNode raiz = File.Exists(jsonMapeamento)
                ? JsonNode.Parse(File.ReadAllText(jsonMapeamento), null, new System.Text.Json.JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = System.Text.Json.JsonCommentHandling.Skip })
                : new JsonObject();
            raiz ??= new JsonObject();
            var linhas = File.Exists(csvMapeamento) ? File.ReadAllLines(csvMapeamento).ToList() : new List<string>();
            var antes = ChavesCsv(linhas);
            var (novosTpl, novasProps, novasLinhas) = Mesclar(anexados, raiz, linhas);
            if (novosTpl + novasProps + novasLinhas == 0) return new List<string>();

            File.WriteAllText(jsonMapeamento, raiz.ToJsonString(ConfigPackage.JsonOpts), new UTF8Encoding(false));
            File.WriteAllText(csvMapeamento, string.Join("\r\n", linhas) + "\r\n", new UTF8Encoding(true));
            return anexados.Where(p => p.Propriedades.Any(pr => !antes.Contains(p.Nome + "\u0001" + pr.Nome))).Select(p => p.Nome).ToList();
        }

        /// <summary>
        /// Property sets de objeto anexados a pelo menos um objeto que vai para o IFC (FiltroIfc), com as propriedades
        /// da definição. Os que estão no desenho mas em nenhum objeto exportado ficam de fora.
        /// </summary>
        public static List<PsetMontagem> AnexadosAoIfc(Database db)
        {
            var nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in Ifc.FiltroIfc.Objetos(db, tr))
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                    try
                    {
                        foreach (ObjectId ps in PropertyDataServices.GetPropertySets(e))
                            if (tr.GetObject(ps, OpenMode.ForRead) is PropertySet s) nomes.Add(s.PropertySetDefinitionName);
                    }
                    catch { /* objeto sem property sets */ }
                }
                tr.Commit();
            }
            var r = LerDesenho(db).Where(p => nomes.Contains(p.Nome) && p.Propriedades.Count > 0).ToList();
            foreach (PsetMontagem p in r) { p.LevarIfc = true; p.EntidadeIfc = "IfcProduct"; }
            return r;
        }

        static HashSet<string> ChavesCsv(List<string> linhas)
        {
            var cab = linhas.FirstOrDefault(l => l.Trim().Length > 0) ?? "";
            char sep = cab.Count(c => c == ';') > cab.Count(c => c == ',') ? ';' : ',';
            return new HashSet<string>(linhas.Where(l => l.Trim().Length > 0).Skip(1).Select(l => LeitorPlanilhaLinha(l, sep)).Where(c => c.Count >= 2)
                .Select(c => c[0].Trim() + "\u0001" + c[1].Trim()), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Acrescenta os property sets ao modelo (JSON) e às linhas do CSV do mapeamento; o que já existe fica.</summary>
        static (int Tpl, int Props, int Linhas) Mesclar(List<PsetMontagem> psets, JsonNode raiz, List<string> linhas)
        {
            JsonArray tpls = Templates(raiz);
            if (tpls == null) { tpls = new JsonArray(); raiz["PropertySetTemplates"] = tpls; }
            int novosTpl = 0, novasProps = 0;
            foreach (PsetMontagem p in psets)
            {
                JsonNode tpl = tpls.FirstOrDefault(x => string.Equals((string)x?["Name"], p.Nome, StringComparison.OrdinalIgnoreCase));
                if (tpl == null)
                {
                    tpl = new JsonObject
                    {
                        ["Name"] = p.Nome,
                        ["Description"] = p.Descricao ?? "",
                        ["TemplateType"] = "NOTDEFINED",
                        ["ApplicableEntities"] = string.IsNullOrWhiteSpace(p.EntidadeIfc) ? "IfcElement" : p.EntidadeIfc,
                        ["PropertyTemplates"] = new JsonArray(),
                    };
                    tpls.Add(tpl);
                    novosTpl++;
                }
                if (!(tpl["PropertyTemplates"] is JsonArray props)) tpl["PropertyTemplates"] = props = new JsonArray();
                foreach (PropriedadeMontagem pr in p.Propriedades)
                {
                    if (props.Any(x => string.Equals((string)x?["Name"], pr.Nome, StringComparison.OrdinalIgnoreCase))) continue;
                    props.Add(new JsonObject { ["Name"] = pr.Nome, ["Description"] = "", ["PrimaryMeasureType"] = MedidaIfc(pr.Tipo) });
                    novasProps++;
                }
            }

            linhas.RemoveAll(l => l.Trim().Length == 0);
            if (linhas.Count == 0) linhas.Add("IfcPropertySet Name,IfcProperty Name,Active,Source,Group,Civil 3D Name");
            char sep = linhas[0].Count(c => c == ';') > linhas[0].Count(c => c == ',') ? ';' : ',';
            var existentes = ChavesCsv(linhas);
            string Q(string s) => s.IndexOfAny(new[] { sep, '"' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
            int novasLinhas = 0;
            foreach (PsetMontagem p in psets)
                foreach (PropriedadeMontagem pr in p.Propriedades)
                {
                    if (!existentes.Add(p.Nome + "\u0001" + pr.Nome)) continue;
                    linhas.Add(string.Join(sep.ToString(), new[] { p.Nome, pr.Nome, "TRUE", "PropertySet", p.Nome, pr.Nome }.Select(Q)));
                    novasLinhas++;
                }
            return (novosTpl, novasProps, novasLinhas);
        }

        static List<string> LeitorPlanilhaLinha(string linha, char sep)
        {
            var cells = new List<string>();
            var sb = new StringBuilder();
            bool q = false;
            for (int i = 0; i < linha.Length; i++)
            {
                char c = linha[i];
                if (q)
                {
                    if (c == '"' && i + 1 < linha.Length && linha[i + 1] == '"') { sb.Append('"'); i++; }
                    else if (c == '"') q = false;
                    else sb.Append(c);
                }
                else if (c == '"') q = true;
                else if (c == sep) { cells.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            cells.Add(sb.ToString());
            return cells;
        }

        // ------------------------------------------------------------------ modelos prontos

        /// <summary>Modelos prontos para começar (o padrão completo do Metrô vem da planilha da equipe).</summary>
        public static List<(string Rotulo, string Dica, Func<PsetMontagem> Criar)> Modelos() => new List<(string, string, Func<PsetMontagem>)>
        {
            ("Classificação (exportador IFC)",
             "Property set Classification com ClassificationCode, o mesmo que o Verificar classificação usa e o exportador IFC lê.",
             () => new PsetMontagem
             {
                 Nome = Classificacao.ClassificacaoCore.PsetPadrao,
                 Descricao = "Classificação (MetroSP)",
                 Propriedades = { new PropriedadeMontagem { Nome = "ClassificationCode" } },
             }),
        };
    }
}
