using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.DatabaseServices;
using MetroSP.Planilha;
using AecDataType = Autodesk.Aec.PropertyData.DataType;

namespace MetroSP.Classificacao
{
    // ====================================================================== lista (planilha)

    /// <summary>Um item da classificação (equivalente a um IfcClassificationReference).</summary>
    public class ClassItem
    {
        public string Codigo { get; set; }
        public string Descricao { get; set; }
        /// <summary>Tipos de objeto ou layers em que o item vale (vazio = todos). Aceita * e ?.</summary>
        public List<string> Categorias { get; } = new List<string>();

        public bool ValePara(string tipo, string layer) =>
            Categorias.Count == 0 || Categorias.Any(c => Casa(c, tipo) || Casa(c, layer));

        private static bool Casa(string padrao, string valor)
        {
            if (string.IsNullOrEmpty(valor)) return false;
            if (padrao.IndexOfAny(new[] { '*', '?' }) < 0) return string.Equals(padrao, valor, StringComparison.CurrentCultureIgnoreCase);
            string re = "^" + Regex.Escape(padrao).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(valor, re, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        public override string ToString() => $"{Codigo}  {Descricao}";
    }

    public class ClassificationList
    {
        public string Nome { get; set; }
        public List<ClassItem> Itens { get; } = new List<ClassItem>();
        public Dictionary<string, ClassItem> PorCodigo { get; } = new Dictionary<string, ClassItem>(StringComparer.OrdinalIgnoreCase);
        public List<string> Avisos { get; } = new List<string>();

        public static ClassificationList De(Tabela t, MapaColunas mapa, string nome)
        {
            var lista = new ClassificationList { Nome = nome };
            foreach (LinhaPlanilha l in mapa.Linhas(t))
            {
                string cod = l[SistemaClassificacao.CampoCodigo.Chave];
                if (string.IsNullOrWhiteSpace(cod)) continue;
                var item = new ClassItem { Codigo = cod.Trim(), Descricao = (l[SistemaClassificacao.CampoDescricao.Chave] ?? "").Trim() };
                string cats = l[SistemaClassificacao.CampoCategorias.Chave];
                if (!string.IsNullOrWhiteSpace(cats))
                    item.Categorias.AddRange(cats.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim()));
                if (lista.PorCodigo.ContainsKey(item.Codigo))
                {
                    lista.Avisos.Add($"Código repetido na planilha: {item.Codigo} (linha {l.Numero}); ficou a primeira ocorrência.");
                    continue;
                }
                lista.PorCodigo[item.Codigo] = item;
                lista.Itens.Add(item);
            }
            return lista;
        }
    }

    /// <summary>"Código:Descrição" ou "[Nome]Código:Descrição", como o ClassificationCode do exportador IFC da Autodesk.</summary>
    public class ValorClassificacao
    {
        public string Sistema { get; set; }
        public string Codigo { get; set; }
        public string Descricao { get; set; }
        public bool Valido { get; set; }

        private static readonly Regex Formato = new Regex(@"^\s*(?:\[(?<s>[^\]]*)\])?\s*(?<c>[^:]+?)\s*:\s*(?<d>.*?)\s*$");

        public static ValorClassificacao Ler(string texto)
        {
            var v = new ValorClassificacao();
            if (string.IsNullOrWhiteSpace(texto)) return v;
            Match m = Formato.Match(texto);
            if (m.Success && m.Groups["d"].Value.Length > 0)
            {
                v.Sistema = m.Groups["s"].Success && m.Groups["s"].Value.Length > 0 ? m.Groups["s"].Value : null;
                v.Codigo = m.Groups["c"].Value;
                v.Descricao = m.Groups["d"].Value;
                v.Valido = true;
            }
            return v;
        }

        public static string Escrever(string codigo, string descricao, string sistema = null) =>
            (string.IsNullOrEmpty(sistema) ? "" : $"[{sistema}]") + $"{codigo}:{descricao}";
    }

    // ====================================================================== padrão

    public enum FormatoClassificacao { CodigoDescricao, Separados }

    /// <summary>Uma classificação do padrão: em qual property set e propriedade fica no desenho e de qual planilha vem a lista.</summary>
    public class SistemaClassificacao
    {
        public string Nome { get; set; } = "Metrô SP";
        public string Edicao { get; set; }
        public FormatoClassificacao Formato { get; set; } = FormatoClassificacao.CodigoDescricao;
        public string PropertySet { get; set; } = ClassificacaoCore.PsetPadrao;
        public string PropriedadeCodigo { get; set; } = "ClassificationCode";
        public string PropriedadeDescricao { get; set; }
        public bool GravarNome { get; set; }
        public MapaColunas Planilha { get; set; } = new MapaColunas();

        [JsonIgnore] public ClassificationList Lista { get; set; }
        [JsonIgnore] public string Erro { get; set; }
        [JsonIgnore] public string Rotulo => $"{Nome} ({PropertySet} › {PropriedadeCodigo})";
        [JsonIgnore]
        public string Propriedades => PropertySet + " › " + PropriedadeCodigo +
            (Formato == FormatoClassificacao.Separados && !string.IsNullOrWhiteSpace(PropriedadeDescricao) ? " + " + PropriedadeDescricao : "");
        [JsonIgnore]
        public string Descricao => Formato == FormatoClassificacao.Separados && !string.IsNullOrWhiteSpace(PropriedadeDescricao) ? PropriedadeDescricao : null;

        public SistemaClassificacao Clonar()
        {
            var c = (SistemaClassificacao)MemberwiseClone();
            c.Planilha = Planilha?.Clonar() ?? new MapaColunas();
            c.Lista = null;
            c.Erro = null;
            return c;
        }

        public static readonly CampoPlanilha CampoCodigo = new CampoPlanilha("codigo", "Código", true,
            "Coluna com o código do item (ex.: VP.01.02).", "código", "codigo", "cód", "cod", "code", "identification", "number", "número", "numero", "item");
        public static readonly CampoPlanilha CampoDescricao = new CampoPlanilha("descricao", "Descrição", false,
            "Coluna com a descrição oficial. Sem ela, só o código é conferido.", "descrição", "descricao", "description", "nome", "name", "título", "titulo", "title");
        public static readonly CampoPlanilha CampoCategorias = new CampoPlanilha("categorias", "Tipos ou layers", false,
            "Opcional. Tipos de objeto (Corredor, Tubo, Sólido 3D...) ou layers (aceita *) em que o item vale, separados por ; (vazio = todos).",
            "categorias", "categoria", "tipos", "tipo", "layers", "layer", "objetos", "categories");

        public static List<CampoPlanilha> Campos => new List<CampoPlanilha> { CampoCodigo, CampoDescricao, CampoCategorias };

        public void Carregar(Dictionary<string, Tabela> cache)
        {
            Lista = null;
            Erro = null;
            try
            {
                if (string.IsNullOrEmpty(Planilha?.Arquivo)) { Erro = "sem planilha"; return; }
                if (!File.Exists(Planilha.Arquivo)) { Erro = "planilha não encontrada: " + Planilha.Arquivo; return; }
                if (!cache.TryGetValue(Planilha.Arquivo, out Tabela t)) cache[Planilha.Arquivo] = t = LeitorPlanilha.Ler(Planilha.Arquivo);
                if (Planilha.Coluna(CampoCodigo.Chave) == null) { Erro = "falta indicar a coluna do código"; return; }
                Lista = ClassificationList.De(t, Planilha, Nome);
                if (Lista.Itens.Count == 0) Erro = "nenhum código na planilha";
            }
            catch (Exception ex) { Erro = ex.Message; }
        }
    }

    /// <summary>Modelos com os nomes da Autodesk (exportador IFC e Classification Manager), gravados em property sets.</summary>
    public class ModeloAutodesk
    {
        public string Rotulo { get; set; }
        public string Dica { get; set; }
        public string Nome { get; set; }
        public FormatoClassificacao Formato { get; set; }
        public string Codigo { get; set; }
        public string Descricao { get; set; }
        public override string ToString() => Rotulo;

        public static List<ModeloAutodesk> Todos()
        {
            var m = new List<ModeloAutodesk>();
            for (int i = 1; i <= 4; i++)
            {
                string p = i == 1 ? "ClassificationCode" : $"ClassificationCode({i})";
                m.Add(new ModeloAutodesk
                {
                    Rotulo = "Exportador IFC · " + p,
                    Dica = $"Propriedade {p} com \"Código:Descrição\" (ou \"[Nome]Código:Descrição\"), o mesmo formato do exportador IFC da Autodesk " +
                           "no Revit. Use (2), (3)... para mais de uma classificação no mesmo objeto.",
                    Nome = i == 1 ? "Metrô SP" : "Classificação " + i,
                    Formato = FormatoClassificacao.CodigoDescricao,
                    Codigo = p,
                });
            }
            foreach (var (nome, tabela) in new[] { ("Uniformat", "Uniformat"), ("OmniClass 23", "OmniClass.23"), ("Uniclass Pr", "Uniclass.Pr"), ("Uniclass Ss", "Uniclass.Ss") })
                m.Add(new ModeloAutodesk
                {
                    Rotulo = "Classification Manager · " + nome,
                    Dica = $"Mesmos nomes do Classification Manager da Autodesk: Classification.{tabela}.Number e .Description.",
                    Nome = nome,
                    Formato = FormatoClassificacao.Separados,
                    Codigo = $"Classification.{tabela}.Number",
                    Descricao = $"Classification.{tabela}.Description",
                });
            return m;
        }
    }

    /// <summary>Padrão de verificação: uma ou mais classificações. Fica em %AppData%\MetroSP\Civil3D\Classificacao\[nome].json.</summary>
    public class PadraoClassificacao
    {
        public string Nome { get; set; } = "Padrão Metrô";
        public List<SistemaClassificacao> Sistemas { get; set; } = new List<SistemaClassificacao>();
        [JsonIgnore] public string Caminho { get; set; }
        public override string ToString() => Nome;

        public static readonly string Pasta =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MetroSP", "Civil3D", "Classificacao");

        private static readonly JsonSerializerOptions Opcoes = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public PadraoClassificacao Clonar() => new PadraoClassificacao { Nome = Nome, Caminho = Caminho, Sistemas = Sistemas.Select(s => s.Clonar()).ToList() };

        public static PadraoClassificacao Ler(string caminho)
        {
            var p = JsonSerializer.Deserialize<PadraoClassificacao>(File.ReadAllText(caminho, Encoding.UTF8), Opcoes)
                    ?? throw new InvalidDataException("Arquivo de padrão vazio.");
            p.Caminho = caminho;
            p.Sistemas ??= new List<SistemaClassificacao>();
            foreach (SistemaClassificacao s in p.Sistemas) s.Planilha ??= new MapaColunas();
            if (string.IsNullOrWhiteSpace(p.Nome)) p.Nome = Path.GetFileNameWithoutExtension(caminho);
            return p;
        }

        public static List<PadraoClassificacao> Listar()
        {
            var lista = new List<PadraoClassificacao>();
            try
            {
                if (Directory.Exists(Pasta))
                    foreach (string f in Directory.GetFiles(Pasta, "*.json"))
                    {
                        try { lista.Add(Ler(f)); } catch { }
                    }
            }
            catch { }
            return lista.OrderBy(p => p.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public void Salvar()
        {
            Directory.CreateDirectory(Pasta);
            string n = string.Concat((Nome ?? "padrao").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
            string destino = Path.Combine(Pasta, (n.Length == 0 ? "padrao" : n) + ".json");
            Exportar(destino);
            if (!string.IsNullOrEmpty(Caminho) && !string.Equals(Caminho, destino, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetDirectoryName(Caminho), Pasta, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(Caminho); } catch { }
            }
            Caminho = destino;
        }

        public void Exportar(string destino) => File.WriteAllText(destino, JsonSerializer.Serialize(this, Opcoes), new UTF8Encoding(false));

        public void Excluir()
        {
            if (!string.IsNullOrEmpty(Caminho) && File.Exists(Caminho)) File.Delete(Caminho);
        }

        public static PadraoClassificacao Inicial() =>
            new PadraoClassificacao { Nome = "Padrão Metrô", Sistemas = { new SistemaClassificacao { Planilha = new MapaColunas { LinhaCabecalho = 0 } } } };
    }

    // ====================================================================== verificação

    public class LinhaVerificacao
    {
        public string Situacao { get; set; }
        public string Acao { get; set; }
        public string Sistema { get; set; }
        public SistemaClassificacao SistemaRef { get; set; }
        public string Objeto { get; set; }
        public string Tipo { get; set; }
        public string Layer { get; set; }
        public string Handle { get; set; }
        public string Preenchido { get; set; }
        public string NaLista { get; set; }
        public string Proposta { get; set; }
        public string Bloqueio { get; set; }
        public bool PropostaSegura { get; set; }
        public ObjectId Id { get; set; }
        /// <summary>Property set ou propriedade que falta (a ação "Anexar property set" cria).</summary>
        public bool FaltaPset { get; set; }
        /// <summary>O que gravar se a proposta for aceita: propriedade → valor.</summary>
        public List<(string Propriedade, string Valor)> Gravacoes { get; } = new List<(string, string)>();
    }

    /// <summary>Lê e grava a classificação em property sets (Property Set Definitions do AutoCAD/Civil 3D).</summary>
    public static class ClassificacaoCore
    {
        public const string PsetPadrao = "Classification";

        public const string OK = "OK";
        public const string SemClassificacao = "Sem classificação";
        public const string FormatoInvalido = "Formato inválido";
        public const string ForaDaLista = "Código fora da lista";
        public const string DescricaoDiferente = "Descrição diferente da lista";
        public const string CategoriaNaoPrevista = "Tipo não previsto";
        public const string SemParametro = "Sem o property set";

        public static readonly string[] Ordem = { OK, DescricaoDiferente, ForaDaLista, FormatoInvalido, CategoriaNaoPrevista, SemClassificacao, SemParametro };

        // ------------------------------------------------------------------ objetos

        private static readonly Dictionary<string, string> Tipos = new Dictionary<string, string>
        {
            ["Corridor"] = "Corredor", ["Alignment"] = "Alinhamento", ["Profile"] = "Perfil", ["TinSurface"] = "Superfície",
            ["GridSurface"] = "Superfície", ["FeatureLine"] = "Linha característica", ["Pipe"] = "Tubo", ["Structure"] = "Estrutura",
            ["PressurePipe"] = "Tubo de pressão", ["PressureFitting"] = "Conexão de pressão", ["PressureAppurtenance"] = "Acessório de pressão",
            ["Solid3d"] = "Sólido 3D", ["SubDMesh"] = "Malha", ["PolyFaceMesh"] = "Malha", ["BlockReference"] = "Bloco",
            ["Polyline3d"] = "Polilinha 3D", ["Polyline"] = "Polilinha", ["Line"] = "Linha", ["Grading"] = "Terraplenagem",
            ["Parcel"] = "Lote", ["CogoPoint"] = "Ponto", ["Body"] = "Corpo", ["Surface"] = "Superfície",
        };

        public static string Tipo(Entity e)
        {
            string n = e.GetType().Name;
            return Tipos.TryGetValue(n, out string t) ? t : n;
        }

        public static string NomeObjeto(Entity e, Transaction tr)
        {
            try
            {
                if (e is BlockReference br) return Ifc.FiltroIfc.NomeBloco(br, tr);
                if (e is Autodesk.Civil.DatabaseServices.Entity c && !string.IsNullOrEmpty(c.Name)) return c.Name;
            }
            catch { }
            return Tipo(e);
        }

        // ------------------------------------------------------------------ property sets

        /// <summary>Property set do objeto com esse nome (objeto ou estilo), ou null.</summary>
        public static PropertySet Pset(DBObject obj, string nome, Transaction tr)
        {
            ObjectIdCollection ids;
            try { ids = PropertyDataServices.GetPropertySets(obj); }
            catch { return null; }
            foreach (ObjectId id in ids)
                if (tr.GetObject(id, OpenMode.ForRead) is PropertySet ps && string.Equals(ps.PropertySetDefinitionName, nome, StringComparison.OrdinalIgnoreCase))
                    return ps;
            return null;
        }

        /// <summary>Valor da propriedade como texto; null se o property set ou a propriedade não existem.</summary>
        public static string Valor(PropertySet ps, string propriedade, out bool existe, out bool somenteLeitura)
        {
            existe = false;
            somenteLeitura = false;
            if (ps == null || string.IsNullOrEmpty(propriedade)) return null;
            int id;
            try { id = ps.PropertyNameToId(propriedade); }
            catch { return null; }
            if (id < 0) return null;
            existe = true;
            try
            {
                var def = (PropertySetDefinition)ps.PropertySetDefinition.GetObject(OpenMode.ForRead);
                int i = def.Definitions.IndexOf(propriedade);
                if (i >= 0) somenteLeitura = def.Definitions[i].Automatic;
            }
            catch { }
            try { return Convert.ToString(ps.GetAt(id), CultureInfo.CurrentCulture) ?? ""; }
            catch { return ""; }
        }

        // ------------------------------------------------------------------ verificar

        public static List<LinhaVerificacao> Verificar(Database db, Transaction tr, IEnumerable<ObjectId> objetos, SistemaClassificacao s)
        {
            var linhas = new List<LinhaVerificacao>();
            string nomeGravar = s.GravarNome ? s.Nome : null;
            foreach (ObjectId id in objetos)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                var l = new LinhaVerificacao
                {
                    Id = id,
                    Sistema = s.Nome,
                    SistemaRef = s,
                    Objeto = NomeObjeto(e, tr),
                    Tipo = Tipo(e),
                    Layer = e.Layer,
                    Handle = e.Handle.ToString(),
                };
                linhas.Add(l);
                PropertySet ps = Pset(e, s.PropertySet, tr);
                string cod = Valor(ps, s.PropriedadeCodigo, out bool existe, out bool leitura);
                if (!existe)
                {
                    l.Situacao = SemParametro;
                    l.FaltaPset = true;
                    l.Proposta = ps == null ? $"Anexar o property set {s.PropertySet}" : $"Acrescentar {s.PropriedadeCodigo} ao property set {s.PropertySet}";
                }
                else
                {
                    if (leitura) l.Bloqueio = $"{s.PropriedadeCodigo} é automática no property set (não pode ser gravada)";
                    if (s.Formato == FormatoClassificacao.Separados) VerificarSeparados(e, ps, cod, l, s);
                    else VerificarCodigoDescricao(e, cod, l, s.Lista, s.PropriedadeCodigo, nomeGravar);
                    if (leitura) l.Gravacoes.Clear();
                }
                l.Acao = Acao(l);
            }
            return linhas;
        }

        private static string Acao(LinhaVerificacao l)
        {
            if (l.Situacao == OK) return null;
            if (l.FaltaPset) return "Anexar property set";
            if (!string.IsNullOrEmpty(l.Bloqueio)) return "Mostrar";
            if (l.Gravacoes.Count > 0) return "Corrigir";
            if (l.Situacao == SemClassificacao || l.Situacao == ForaDaLista || l.Situacao == FormatoInvalido || l.Situacao == CategoriaNaoPrevista) return "Escolher...";
            return "Mostrar";
        }

        private static void VerificarCodigoDescricao(Entity e, string valor, LinhaVerificacao l, ClassificationList lista, string prop, string nomeGravar)
        {
            l.Preenchido = valor;
            if (string.IsNullOrWhiteSpace(valor)) { l.Situacao = SemClassificacao; l.Proposta = "Escolha na lista"; return; }
            ValorClassificacao v = ValorClassificacao.Ler(valor);
            ClassItem item;
            if (!v.Valido)
            {
                item = ProcurarPorTexto(lista, valor);
                l.Situacao = FormatoInvalido;
                l.Proposta = "Formato esperado Código:Descrição";
                if (item != null)
                {
                    l.NaLista = ValorClassificacao.Escrever(item.Codigo, item.Descricao);
                    l.Gravacoes.Add((prop, ValorClassificacao.Escrever(item.Codigo, item.Descricao, nomeGravar)));
                    l.Proposta = $"Reescrever como {l.NaLista}";
                }
                return;
            }
            if (!lista.PorCodigo.TryGetValue(v.Codigo, out item))
            {
                l.Situacao = ForaDaLista;
                ClassItem parecido = Parecido(lista, v.Codigo, v.Descricao);
                if (parecido != null)
                {
                    l.NaLista = ValorClassificacao.Escrever(parecido.Codigo, parecido.Descricao);
                    l.Gravacoes.Add((prop, ValorClassificacao.Escrever(parecido.Codigo, parecido.Descricao, nomeGravar ?? v.Sistema)));
                    l.Proposta = $"Código parecido: {parecido.Codigo}";
                }
                else l.Proposta = "Escolha na lista";
                return;
            }
            l.NaLista = ValorClassificacao.Escrever(item.Codigo, item.Descricao);
            if (!item.ValePara(l.Tipo, l.Layer))
            {
                l.Situacao = CategoriaNaoPrevista;
                l.Proposta = "Item vale para " + string.Join(", ", item.Categorias);
                return;
            }
            bool descricaoOk = item.Descricao.Length == 0 || v.Descricao == item.Descricao;
            if (!descricaoOk || v.Codigo != item.Codigo)
            {
                l.Situacao = DescricaoDiferente;
                l.Proposta = "Usar a escrita da lista" + Diferenca(v.Descricao, item.Descricao);
                l.Gravacoes.Add((prop, ValorClassificacao.Escrever(item.Codigo, item.Descricao.Length == 0 ? v.Descricao : item.Descricao, nomeGravar ?? v.Sistema)));
                l.PropostaSegura = true;
                return;
            }
            l.Situacao = OK;
        }

        private static void VerificarSeparados(Entity e, PropertySet ps, string cod, LinhaVerificacao l, SistemaClassificacao s)
        {
            cod = (cod ?? "").Trim();
            string pDesc = s.Descricao;
            string desc = null;
            bool temDesc = false, descLeitura = false;
            if (pDesc != null) desc = Valor(ps, pDesc, out temDesc, out descLeitura) ?? "";
            bool descEditavel = temDesc && !descLeitura;
            l.Preenchido = pDesc == null || cod.Length == 0 ? cod : $"{cod} · {desc}";
            if (cod.Length == 0) { l.Situacao = SemClassificacao; l.Proposta = "Escolha na lista"; return; }

            void Gravar(ClassItem item, bool codigo)
            {
                if (codigo) l.Gravacoes.Add((s.PropriedadeCodigo, item.Codigo));
                if (pDesc != null && item.Descricao.Length > 0 && desc != item.Descricao && (descEditavel || !temDesc)) l.Gravacoes.Add((pDesc, item.Descricao));
            }

            if (!s.Lista.PorCodigo.TryGetValue(cod, out ClassItem it))
            {
                l.Situacao = ForaDaLista;
                ClassItem parecido = Parecido(s.Lista, cod, desc);
                if (parecido != null)
                {
                    l.NaLista = $"{parecido.Codigo} · {parecido.Descricao}";
                    l.Proposta = $"Código parecido: {parecido.Codigo}";
                    Gravar(parecido, true);
                }
                else l.Proposta = "Escolha na lista";
                return;
            }
            l.NaLista = $"{it.Codigo} · {it.Descricao}";
            if (!it.ValePara(l.Tipo, l.Layer))
            {
                l.Situacao = CategoriaNaoPrevista;
                l.Proposta = "Item vale para " + string.Join(", ", it.Categorias);
                return;
            }
            bool codigoDiferente = cod != it.Codigo;
            bool descDiferente = pDesc != null && it.Descricao.Length > 0 && desc != it.Descricao;
            if (!codigoDiferente && !descDiferente) { l.Situacao = OK; return; }
            l.Situacao = DescricaoDiferente;
            l.PropostaSegura = true;
            l.Proposta = descDiferente && temDesc && !descEditavel
                ? $"{pDesc} é automática no property set: confira a fórmula"
                : "Usar a escrita da lista" + (descDiferente ? Diferenca(desc, it.Descricao) : "") +
                  (descDiferente && !temDesc ? $" (cria {pDesc})" : "");
            Gravar(it, codigoDiferente);
        }

        private static string Diferenca(string atual, string lista)
        {
            if (atual == null || lista == null) return "";
            if (string.Equals(atual, lista, StringComparison.CurrentCultureIgnoreCase)) return " (maiúsculas/minúsculas)";
            if (MapaColunas.Normalizar(atual) == MapaColunas.Normalizar(lista))
                return Regex.IsMatch(atual, @"\s{2,}") || atual != atual.Trim() ? " (espaços)" : " (acentos ou pontuação)";
            return "";
        }

        private static string SoAlfanumerico(string s) => new string((s ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

        private static ClassItem ProcurarPorTexto(ClassificationList lista, string texto)
        {
            string t = SoAlfanumerico(texto);
            return lista.Itens.Where(i => SoAlfanumerico(i.Codigo).Length > 0 && t.StartsWith(SoAlfanumerico(i.Codigo)))
                .OrderByDescending(i => SoAlfanumerico(i.Codigo).Length).FirstOrDefault();
        }

        private static ClassItem Parecido(ClassificationList lista, string codigo, string descricao)
        {
            string c = SoAlfanumerico(codigo);
            ClassItem igual = lista.Itens.FirstOrDefault(i => SoAlfanumerico(i.Codigo) == c);
            if (igual != null) return igual;
            string d = MapaColunas.Normalizar(descricao);
            return lista.Itens
                .Select(i => (Item: i, Dist: Distancia(c, SoAlfanumerico(i.Codigo)), Desc: MapaColunas.Normalizar(i.Descricao)))
                .Where(x => x.Dist <= 2)
                .OrderBy(x => x.Dist)
                .ThenByDescending(x => d.Split(' ').Intersect(x.Desc.Split(' ')).Count(w => w.Length > 2))
                .FirstOrDefault().Item;
        }

        private static int Distancia(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }

        // ------------------------------------------------------------------ gravar

        /// <summary>
        /// Grava os valores nos property sets, criando o que faltar: a definição do property set (vale para todos os objetos),
        /// as propriedades de texto e o property set no objeto. Retorna o resumo para o usuário.
        /// </summary>
        public static string Gravar(Database db, Transaction tr, string pset, IEnumerable<(ObjectId Id, string Propriedade, string Valor)> valores)
        {
            var lista = valores.ToList();
            int ok = 0;
            var falhas = new List<string>();
            ObjectId defId = GarantirDefinicao(db, tr, pset, lista.Select(v => v.Propriedade).Distinct().ToList(), out string criado);
            foreach (var g in lista.GroupBy(v => v.Id))
            {
                try
                {
                    var obj = tr.GetObject(g.Key, OpenMode.ForWrite);
                    PropertySet ps = Pset(obj, pset, tr);
                    if (ps == null)
                    {
                        PropertyDataServices.AddPropertySet(obj, defId);
                        ps = Pset(obj, pset, tr) ?? throw new InvalidOperationException("O Civil 3D não anexou o property set.");
                    }
                    ps.UpgradeOpen();
                    foreach (var (_, prop, valor) in g)
                    {
                        int pid = ps.PropertyNameToId(prop);
                        ps.SetAt(pid, valor ?? "");
                        ok++;
                    }
                }
                catch (Exception ex) { falhas.Add($"{g.Key.Handle}: {ex.Message}"); }
            }
            var sb = new StringBuilder($"{ok} gravações feitas.");
            if (criado != null) sb.Append("\n" + criado);
            if (falhas.Count > 0)
            {
                sb.Append($"\n\n{falhas.Count} objetos não puderam ser gravados:");
                foreach (string f in falhas.Take(15)) sb.Append("\n•  " + f);
                if (falhas.Count > 15) sb.Append($"\n•  e mais {falhas.Count - 15}");
            }
            return sb.ToString();
        }

        /// <summary>Definição do property set com as propriedades de texto pedidas; cria ou completa se preciso.</summary>
        public static ObjectId GarantirDefinicao(Database db, Transaction tr, string nome, List<string> propriedades, out string criado)
        {
            criado = null;
            var dict = new DictionaryPropertySetDefinitions(db);
            PropertySetDefinition def;
            ObjectId id;
            if (dict.Has(nome, tr))
            {
                id = dict.GetAt(nome);
                def = (PropertySetDefinition)tr.GetObject(id, OpenMode.ForRead);
            }
            else
            {
                def = new PropertySetDefinition();
                def.SetToStandard(db);
                def.SubSetDatabaseDefaults(db);
                def.Description = "Classificação (MetroSP)";
                def.AppliesToAll = true;
                dict.AddNewRecord(nome, def);
                tr.AddNewlyCreatedDBObject(def, true);
                id = def.ObjectId;
                criado = $"Property set {nome} criado no desenho (vale para todos os objetos).";
            }
            var faltam = propriedades.Where(p => !Enumerable.Range(0, def.Definitions.Count).Any(i =>
                string.Equals(def.Definitions[i].Name, p, StringComparison.OrdinalIgnoreCase))).ToList();
            if (faltam.Count > 0)
            {
                def.UpgradeOpen();
                foreach (string p in faltam)
                {
                    var pd = new PropertyDefinition();
                    pd.SetToStandard(db);
                    pd.SubSetDatabaseDefaults(db);
                    pd.Name = p;
                    pd.DataType = AecDataType.Text;
                    pd.DefaultData = "";
                    def.Definitions.Add(pd);
                }
                criado = (criado == null ? "" : criado + "\n") + $"Propriedades acrescentadas a {nome}: {string.Join(", ", faltam)}.";
            }
            return id;
        }
    }
}
