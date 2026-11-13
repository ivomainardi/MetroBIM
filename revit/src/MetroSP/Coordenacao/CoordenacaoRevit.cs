using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using MetroSP.Ids;

namespace MetroSP.Coordenacao
{
    /// <summary>
    /// O arquivo de coordenação dentro do modelo e no Exportar IFC.
    ///
    /// Guardado no modelo: o arquivo inteiro (JSON), o perfil escolhido e o nome do arquivo de onde veio, em Extensible
    /// Storage nas Informações do projeto. Assim a coordenação "fica salva com o arquivo": quem abre o modelo de novo
    /// continua no mesmo perfil, mesmo sem o .coord-metrosp por perto.
    ///
    /// Na exportação: o perfil ativo é escrito numa pasta só deste modelo (%AppData%\MetroSP\Coordenacao\&lt;modelo&gt;),
    /// com a mesma estrutura das outras (Setups, PropertySets, Mapeamentos, Categorias). Essa pasta entra na frente das
    /// outras em ClassesIfc, então o Exportar IFC, a tabela de categorias e a conferência usam o que veio da coordenação.
    /// Um arquivo que o usuário salvar depois de receber a coordenação (mais novo) vale sobre ela.
    /// </summary>
    public static class CoordenacaoRevit
    {
        /// <summary>Coordenação em uso no modelo ativo.</summary>
        public class Ativa
        {
            public ArquivoCoordenacao Arquivo { get; set; }
            public PerfilCoordenacao Perfil { get; set; }
            /// <summary>Nome do .coord-metrosp de onde veio (só para mostrar).</summary>
            public string Origem { get; set; }
            /// <summary>Pasta com os arquivos do perfil ativo, na estrutura das pastas de configuração IFC.</summary>
            public string Pasta { get; set; }
            /// <summary>Quando os arquivos do perfil foram recebidos neste computador (mais antigo dos arquivos da pasta).</summary>
            public string Modelo { get; set; }
            public string Titulo => $"Coordenação rev. {Arquivo.Revisao}" + (Arquivo.Perfis.Count > 1 ? " · " + Perfil.Nome : "");
            public string Resumo => $"{Arquivo.Autor ?? "?"} · {DataCurta(Arquivo.Data)}" + (string.IsNullOrEmpty(Origem) ? "" : " · " + Origem);
        }

        private static Ativa _atual;

        /// <summary>Coordenação do último modelo ativado (null = nenhuma).</summary>
        public static Ativa Atual => _atual;

        /// <summary>Pasta da coordenação do modelo ativo, para entrar na frente das outras (null = nenhuma).</summary>
        public static string PastaAtiva => _atual?.Pasta;

        /// <summary>Coordenação do documento, ativando-o se preciso (null = nenhuma).</summary>
        public static Ativa De(Document doc)
        {
            if (doc == null) return null;
            if (_atual == null || _atual.Modelo != ChaveModelo(doc)) Ativar(doc);
            return _atual;
        }

        public static string ClasseAtiva(Document doc) => De(doc)?.Perfil?.Classe;

        public static string Raiz =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MetroSP", "Coordenacao");

        // ------------------------------------------------------------------ guardado no modelo

        private static readonly Guid GuidSchema = new Guid("8c3e5f7a-2b4d-4e91-b6a8-5d0f1c9e7a42");
        private const string CampoJson = "Json", CampoPerfil = "Perfil", CampoOrigem = "Origem";

        private static Schema Esquema()
        {
            Schema s = Schema.Lookup(GuidSchema);
            if (s != null) return s;
            var b = new SchemaBuilder(GuidSchema);
            b.SetSchemaName("MetroSP_Coordenacao");
            b.SetVendorId("METROSP");
            b.SetReadAccessLevel(AccessLevel.Public);
            b.SetWriteAccessLevel(AccessLevel.Public);
            b.SetDocumentation("Arquivo de coordenação MetroSP (JSON), o perfil escolhido e o nome do arquivo de origem.");
            b.AddSimpleField(CampoJson, typeof(string));
            b.AddSimpleField(CampoPerfil, typeof(string));
            b.AddSimpleField(CampoOrigem, typeof(string));
            return b.Finish();
        }

        /// <summary>A coordenação guardada no modelo, ou null.</summary>
        public static ArquivoCoordenacao Ler(Document doc, out string perfil, out string origem)
        {
            perfil = origem = null;
            try
            {
                Schema s = Schema.Lookup(GuidSchema);
                ProjectInfo info = doc?.ProjectInformation;
                if (info == null || s == null) return null;
                Entity e = info.GetEntity(s);
                if (e == null || !e.IsValid()) return null;
                string json = e.Get<string>(CampoJson);
                if (string.IsNullOrWhiteSpace(json)) return null;
                perfil = e.Get<string>(CampoPerfil);
                origem = e.Get<string>(CampoOrigem);
                return ArquivoCoordenacao.DeJson(json);
            }
            catch { return null; }
        }

        /// <summary>Guarda a coordenação no modelo e a ativa. Precisa de transação aberta.</summary>
        public static void Gravar(Document doc, ArquivoCoordenacao a, string perfil, string origem)
        {
            var e = new Entity(Esquema());
            e.Set(CampoJson, a.ParaJson());
            e.Set(CampoPerfil, a.Perfil(perfil)?.Nome ?? "");
            e.Set(CampoOrigem, origem ?? "");
            doc.ProjectInformation.SetEntity(e);
        }

        /// <summary>Tira a coordenação do modelo. Precisa de transação aberta.</summary>
        public static void Remover(Document doc)
        {
            Schema s = Schema.Lookup(GuidSchema);
            if (s != null && doc.ProjectInformation.GetEntity(s)?.IsValid() == true) doc.ProjectInformation.DeleteEntity(s);
        }

        /// <summary>Troca o perfil ativo do modelo (com transação própria). Retorna false se não deu.</summary>
        public static bool TrocarPerfil(Document doc, string perfil, out string erro)
        {
            erro = null;
            ArquivoCoordenacao a = Ler(doc, out _, out string origem);
            if (a == null) { erro = "O modelo não tem arquivo de coordenação."; return false; }
            if (!Executar(doc, "MetroSP: perfil da coordenação", () => Gravar(doc, a, perfil, origem), out erro)) return false;
            Ativar(doc);
            return true;
        }

        /// <summary>Executa numa transação própria (ou dentro da que estiver aberta).</summary>
        public static bool Executar(Document doc, string nome, Action acao, out string erro)
        {
            erro = null;
            try
            {
                if (doc.IsModifiable) { acao(); return true; }
                using (var t = new Transaction(doc, nome))
                {
                    t.Start();
                    acao();
                    t.Commit();
                }
                return true;
            }
            catch (Exception ex) { erro = ex.Message; return false; }
        }

        // ------------------------------------------------------------------ ativar (pasta do perfil)

        /// <summary>Lê a coordenação do modelo e escreve os arquivos do perfil ativo. Chamar quando o modelo ativo muda.</summary>
        public static void Ativar(Document doc)
        {
            if (doc == null || doc.IsFamilyDocument) { _atual = null; return; }
            string chave = ChaveModelo(doc);
            ArquivoCoordenacao a = Ler(doc, out string perfil, out string origem);
            if (a == null) { _atual = null; return; }
            PerfilCoordenacao p = a.Perfil(perfil);
            if (p == null) { _atual = null; return; }
            string pasta = Path.Combine(Raiz, Assinatura(chave));
            try { Materializar(doc, p, pasta, a.Comum?.Nomenclatura); }
            catch { /* sem pasta gravável: segue sem os arquivos, mas mostra a coordenação */ }
            _atual = new Ativa { Arquivo = a, Perfil = p, Origem = origem, Pasta = pasta, Modelo = chave };
        }

        private static string ChaveModelo(Document doc)
        {
            try { return string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName; }
            catch { return doc.Title; }
        }

        private static string Assinatura(string texto) => ArquivoCoordenacao.Assinatura(texto?.ToLowerInvariant());

        /// <summary>Escreve setup, .txt e tabelas do perfil na pasta. Só regrava o que mudou (a data do arquivo diz quando chegou).</summary>
        internal static void Materializar(Document doc, PerfilCoordenacao p, string pasta, string nomenclatura = null)
        {
            string classe = NomeArquivo(p.Classe ?? "Coordenacao");
            var desejados = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (p.Exportador != null)
            {
                var setup = (JsonObject)JsonNode.Parse(p.Exportador.ToJsonString());
                setup.Remove("ActivePhaseId");
                long fase = FasePeloNome(doc, p.Fase);
                if (fase > 0) setup["ActivePhaseId"] = fase;
                desejados[Path.Combine(FormaPaths.SubSetups, classe + ".json")] = setup.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
            if (p.PropertySets != null) desejados[Path.Combine(FormaPaths.SubPropertySets, classe + ".txt")] = p.PropertySets;
            if (p.Mapeamento != null) desejados[Path.Combine(FormaPaths.SubMapeamentos, classe + ".txt")] = p.Mapeamento;
            if (nomenclatura != null) desejados[ClassesIfc.ArquivoNomenclatura] = nomenclatura;
            foreach (KeyValuePair<string, List<LinhaCategoria>> t in p.Categorias.Where(k => k.Value != null && k.Value.Count > 0))
            {
                var linhas = t.Value.Select(l => new CategoriasIfc.Linha
                {
                    Categoria = l.Categoria, Subcategoria = l.Subcategoria ?? "", Classe = l.Classe ?? "", Tipo = l.Tipo ?? "", Exportar = l.Exportar,
                });
                desejados[Path.Combine(CategoriasIfc.Sub, NomeArquivo(t.Key) + ".txt")] = TextoCategorias(linhas, t.Key);
            }

            Directory.CreateDirectory(pasta);
            // Apaga o que o perfil não tem mais (troca de perfil ou nova revisão).
            foreach (string f in Directory.EnumerateFiles(pasta, "*", SearchOption.AllDirectories).ToList())
            {
                string rel = f.Substring(pasta.Length).TrimStart(Path.DirectorySeparatorChar);
                if (!desejados.ContainsKey(rel)) { try { File.Delete(f); } catch { } }
            }
            foreach (KeyValuePair<string, string> d in desejados)
            {
                string f = Path.Combine(pasta, d.Key);
                if (File.Exists(f) && File.ReadAllText(f) == d.Value) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllText(f, d.Value, new UTF8Encoding(true));
            }
        }

        /// <summary>Tabela de categorias no formato do .txt, sem a linha de data (para não regravar à toa).</summary>
        private static string TextoCategorias(IEnumerable<CategoriasIfc.Linha> linhas, string esquema)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Categorias do Revit → classes IFC ({esquema}) - do arquivo de coordenação MetroSP");
            sb.AppendLine("# Categoria\tSubcategoria\tClasse IFC\tTipo predefinido\tExportar");
            foreach (CategoriasIfc.Linha l in linhas)
                sb.AppendLine($"{l.Categoria}\t{l.Subcategoria}\t{l.Classe}\t{l.Tipo}\t{(l.Exportar ? "Sim" : "Não")}");
            return sb.ToString();
        }

        private static string NomeArquivo(string nome)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) nome = nome.Replace(c, '_');
            return nome.Trim();
        }

        public static long FasePeloNome(Document doc, string nome)
        {
            if (doc == null || string.IsNullOrWhiteSpace(nome)) return -1;
            foreach (Phase f in doc.Phases)
                if (string.Equals(f.Name?.Trim(), nome.Trim(), StringComparison.CurrentCultureIgnoreCase)) return Compat.Numero(f.Id);
            return -1;
        }

        internal static string DataCurta(string iso) =>
            DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime d)
                ? d.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : iso ?? "";

        // ------------------------------------------------------------------ capturar (o que está no modelo agora)

        /// <summary>
        /// Monta um perfil com tudo como está agora para a classe: o setup, a fase, as tabelas de categorias, os .txt
        /// e as classes IFC preenchidas nos tipos (e, se pedido, nas instâncias).
        /// </summary>
        public static PerfilCoordenacao Capturar(Document doc, string classe, string nome, bool porElemento, long fase = -1)
        {
            ClassesIfc.Classe c = ClassesIfc.Obter(classe);
            if (c.Setup == null) throw new InvalidOperationException($"A classe {classe} não tem setup (.json). Escolha o setup no Exportar IFC ou em Arquivos IFC.");
            var p = new PerfilCoordenacao { Nome = nome, Classe = classe };

            JsonObject setup = JsonNode.Parse(File.ReadAllText(c.Setup), documentOptions: new System.Text.Json.JsonDocumentOptions
            {
                AllowTrailingCommas = true, CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
            }) as JsonObject ?? throw new InvalidDataException("O setup " + c.Setup + " não é um objeto JSON.");
            if (fase <= 0 && setup.TryGetPropertyValue("ActivePhaseId", out JsonNode f) && f != null && long.TryParse(f.ToString(), out long n)) fase = n;
            setup.Remove("ActivePhaseId");
            p.Exportador = setup;
            if (fase > 0 && doc.GetElement(Compat.NovoId(fase)) is Phase ph) p.Fase = ph.Name;

            if (c.PropertySets != null && File.Exists(c.PropertySets)) p.PropertySets = File.ReadAllText(c.PropertySets);
            if (c.Mapeamento != null && File.Exists(c.Mapeamento)) p.Mapeamento = File.ReadAllText(c.Mapeamento);

            // As outras escolhas do Exportar IFC: as da última exportação neste computador.
            IfcExportSettings ultima = IfcExportSettings.Load();
            p.Exportacao = new OpcoesExportacao
            {
                Elementos = ultima.Vista ?? OpcoesExportacao.ModeloInteiro,
                NomeArquivo = ultima.NomeArquivo ?? OpcoesExportacao.NomeDoModelo,
                EpsgEmBranco = ultima.EpsgEmBranco,
                SempreEixos = ultima.SempreEixos,
            };

            foreach (string esquema in new[] { CategoriasIfc.Ifc4, CategoriasIfc.Ifc4x3 })
            {
                string arq = CategoriasIfc.Arquivo(esquema);
                if (arq == null) continue;   // sem tabela salva: vale o padrão MetroSP de quem exporta
                p.Categorias[esquema] = CategoriasIfc.Ler(arq).Select(l => new LinhaCategoria
                {
                    Categoria = l.Categoria, Subcategoria = Nulo(l.Subcategoria), Classe = l.Classe ?? "", Tipo = Nulo(l.Tipo), Exportar = l.Exportar,
                }).ToList();
            }

            foreach (ElementType t in new FilteredElementCollector(doc).WhereElementIsElementType().Cast<ElementType>())
            {
                Category cat = t.Category;
                if (cat == null || cat.CategoryType != CategoryType.Model) continue;
                var a = new AjusteFamilia { Categoria = cat.Name, Familia = t.FamilyName, Tipo = t.Name };
                if (!LerAjuste(t, true, a)) continue;
                p.Familias.Add(a);
            }
            p.Familias = p.Familias.OrderBy(a => a.Categoria).ThenBy(a => a.Chave).ToList();

            if (porElemento)
            {
                foreach (Element e in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    Category cat = e.Category;
                    if (cat == null || cat.CategoryType != CategoryType.Model || e.ViewSpecific) continue;
                    var a = new AjusteElemento { UniqueId = e.UniqueId };
                    if (!LerAjuste(e, false, a)) continue;
                    a.Descricao = $"{cat.Name} · {(doc.GetElement(e.GetTypeId()) as ElementType)?.FamilyName} : {e.Name} · id {Compat.Numero(e.Id)}";
                    p.Elementos.Add(a);
                }
            }
            return p;
        }

        /// <summary>As regras de Nomenclatura em uso (classe pelo nome do modelo), ou null.</summary>
        public static string CapturarNomenclatura()
        {
            try { string f = ClassesIfc.ArquivoDeRegras; return f != null ? File.ReadAllText(f) : null; }
            catch { return null; }
        }

        /// <summary>Os dados de Projeto e Local do modelo, no formato dos dados salvos.</summary>
        public static JsonObject CapturarDadosProjeto(Document doc)
        {
            try { return JsonNode.Parse(DadosProjetoRevit.Ler(doc).ParaJson()) as JsonObject; }
            catch { return null; }
        }

        private static string Nulo(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        // Parâmetros do Revit (2023+). No 2022 o "Exportar para IFC como" é o compartilhado IfcExportAs (ver Compat).
        private const string PredefInst = "IFC_EXPORT_PREDEFINEDTYPE", PredefTipo = "IFC_EXPORT_PREDEFINEDTYPE_TYPE";
        private const string ExportarInst = "IFC_EXPORT_ELEMENT", ExportarTipo = "IFC_EXPORT_ELEMENT_TYPE";

        private static Parameter Param(Element e, string bipNome) =>
            e != null && Enum.TryParse(bipNome, out BuiltInParameter bip) ? e.get_Parameter(bip) : null;

        /// <summary>Preenche o ajuste com o que o elemento tem; false se não tem nada preenchido.</summary>
        private static bool LerAjuste(Element e, bool tipo, AjusteIfc a)
        {
            a.Classe = Nulo(Compat.ExportarComo(e, tipo)?.AsString());
            a.TipoPredefinido = Nulo(Param(e, tipo ? PredefTipo : PredefInst)?.AsString());
            Parameter x = Param(e, tipo ? ExportarTipo : ExportarInst);
            int exp = x != null && x.StorageType == StorageType.Integer && x.HasValue ? x.AsInteger() : 0;
            a.Exportar = exp != 0 ? AjusteIfc.ExportarTexto(exp) : null;
            return a.Classe != null || a.TipoPredefinido != null || a.Exportar != null;
        }

        // ------------------------------------------------------------------ aplicar (famílias e elementos)

        /// <summary>
        /// Grava no modelo as classes por família e por elemento do perfil. Precisa de transação aberta.
        /// Retorna quantos tipos e elementos mudaram e o que não foi achado.
        /// </summary>
        public static (int Tipos, int Elementos, List<string> NaoAchados) AplicarAjustes(Document doc, PerfilCoordenacao p)
        {
            int tipos = 0, elementos = 0;
            var naoAchados = new List<string>();
            if (p == null) return (0, 0, naoAchados);
            if (p.Familias.Count > 0)
            {
                var porChave = new Dictionary<string, List<ElementType>>(StringComparer.CurrentCultureIgnoreCase);
                foreach (ElementType t in new FilteredElementCollector(doc).WhereElementIsElementType().Cast<ElementType>())
                {
                    if (t.Category == null || t.Category.CategoryType != CategoryType.Model) continue;
                    string k = (t.FamilyName ?? "").Trim() + " : " + (t.Name ?? "").Trim();
                    if (!porChave.TryGetValue(k, out List<ElementType> l)) porChave[k] = l = new List<ElementType>();
                    l.Add(t);
                }
                foreach (AjusteFamilia a in p.Familias)
                {
                    if (!porChave.TryGetValue(a.Chave, out List<ElementType> l)) { naoAchados.Add(a.Chave); continue; }
                    // Mesmo nome em categorias diferentes: vale a categoria do arquivo.
                    IEnumerable<ElementType> alvo = l.Count > 1 && a.Categoria != null
                        ? l.Where(t => string.Equals(t.Category.Name, a.Categoria, StringComparison.CurrentCultureIgnoreCase)) : l;
                    foreach (ElementType t in alvo) if (Gravar(t, true, a)) tipos++;
                }
            }
            foreach (AjusteElemento a in p.Elementos)
            {
                Element e = doc.GetElement(a.UniqueId);
                if (e == null) { naoAchados.Add(a.Descricao ?? a.UniqueId); continue; }
                if (Gravar(e, false, a)) elementos++;
            }
            return (tipos, elementos, naoAchados);
        }

        private static bool Gravar(Element e, bool tipo, AjusteIfc a)
        {
            bool mudou = false;
            mudou |= GravarTexto(Compat.ExportarComo(e, tipo), a.Classe);
            mudou |= GravarTexto(Param(e, tipo ? PredefTipo : PredefInst), a.TipoPredefinido);
            int? exp = a.ExportarValor;
            Parameter x = Param(e, tipo ? ExportarTipo : ExportarInst);
            if (exp.HasValue && x != null && !x.IsReadOnly && x.StorageType == StorageType.Integer && x.AsInteger() != exp.Value)
                mudou |= x.Set(exp.Value);
            return mudou;
        }

        private static bool GravarTexto(Parameter p, string valor)
        {
            if (valor == null || p == null || p.IsReadOnly || p.StorageType != StorageType.String) return false;
            if (string.Equals(p.AsString() ?? "", valor, StringComparison.Ordinal)) return false;
            return p.Set(valor);
        }

        /// <summary>
        /// Na exportação, dentro da transação do Exportar IFC (que é desfeita no fim): aplica as classes por família e
        /// por elemento do perfil ativo. Retorna a linha para o resumo, ou null sem coordenação.
        /// </summary>
        public static string AplicarNaExportacao(Document doc)
        {
            Ativa c = De(doc);
            if (c == null) return null;
            string titulo = c.Titulo + " (" + c.Resumo + ")";
            if (c.Perfil.Familias.Count == 0 && c.Perfil.Elementos.Count == 0) return titulo + ".";
            using (var sub = new SubTransaction(doc))
            {
                try
                {
                    sub.Start();
                    var (tipos, elementos, naoAchados) = AplicarAjustes(doc, c.Perfil);
                    sub.Commit();
                    return titulo + $": classe IFC ajustada em {tipos} tipo(s)" + (elementos > 0 ? $" e {elementos} elemento(s)" : "") +
                           (naoAchados.Count == 0 ? "." : $"; {naoAchados.Count} não encontrado(s) neste modelo: " +
                               string.Join(", ", naoAchados.Take(5)) + (naoAchados.Count > 5 ? "..." : "") + ".");
                }
                catch (Exception ex)
                {
                    try { if (sub.HasStarted() && !sub.HasEnded()) sub.RollBack(); } catch { }
                    return titulo + ": não foi possível aplicar as classes por família (" + ex.Message + ").";
                }
            }
        }
    }
}
