using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using MetroSP.Classificacao;
using MetroSP.Ids;
using MetroSP.Parametros;

namespace MetroSP.Conferencia
{
    /// <summary>
    /// Confere, numa passada só, o que o Metrô pede para a entrega:
    ///   1. Arquivos do pacote (IDS, setup, psets, classes, planilha, MetroShared) achados e válidos;
    ///   2. Parâmetros e mapeamento (o parâmetro de cada campo existe nas categorias, com tipo compatível e GUID do MetroShared);
    ///   3. Classe IFC dos elementos (proxies e elementos sem nenhum campo do Metrô);
    ///   4. Dados preenchidos (matriz categoria × campo exigido);
    ///   5. Classificação (preenchida, no formato e na planilha).
    /// Reaproveita os leitores e regras das outras ferramentas (MappingAnalyzer, ClassificationCore, ReferenceLibrary).
    /// Só lê o modelo; nada é gravado.
    /// </summary>
    public class ConferenciaEntrega
    {
        public const string ProblemaSemParametro = "sem o parâmetro";
        public const string ProblemaVazio = "vazio";
        public const string ProblemaForaDoExigido = "fora do exigido";

        private readonly Document _doc;
        private readonly PacoteMetro _pacote;
        private readonly ElementId _vista;

        private IdsDocument _ids;
        private IfcExportSetup _setup;
        private readonly ExportMapping _mapping = new ExportMapping();
        private ClassificationList _planilha;
        private SharedParamTxt _metroShared;
        private bool _temPsets;

        private MappingAnalyzer _analisador;
        private readonly Dictionary<string, List<Exigencia>> _exigenciasPorEntidade = new Dictionary<string, List<Exigencia>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Pendencia> _problemasMapeamento = new Dictionary<string, Pendencia>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Campo exigido para uma entidade IFC e o parâmetro do Revit de onde ele sai.</summary>
        private class Exigencia
        {
            public string Campo;
            public string Parametro;
            public bool? NoTipo;
            public string TipoArquivo;
            public IdsValue Valor;
            /// <summary>Pset padrão (Pset_/Qto_) sem mapeamento: sem o parâmetro, o exportador pode calcular. Não é pendência.</summary>
            public bool PadraoBuildingSmart;
            public bool SemMapeamento;
        }

        /// <param name="vista">Vista que limita os elementos (null = modelo inteiro).</param>
        public ConferenciaEntrega(Document doc, PacoteMetro pacote, ElementId vista)
        {
            _doc = doc;
            _pacote = pacote;
            _vista = vista;
        }

        public ResultadoConferencia Executar()
        {
            var r = new ResultadoConferencia
            {
                Documento = _doc.Title,
                Escopo = _vista == null ? "modelo inteiro" : "vista " + (_doc.GetElement(_vista) as View)?.Name,
            };

            Ui.Progresso.Atual?.Etapa("Lendo os arquivos dos padrões");
            EtapaConferencia arquivos = ConferirArquivos();
            _analisador = new MappingAnalyzer(_doc, _mapping, checkValues: true);

            Ui.Progresso.Atual?.Etapa("Separando os elementos que vão para o IFC");
            List<(Element E, string Entidade)> exportados = ElementosExportados();
            // Regra de todas as ferramentas: só entram na conferência os elementos de classes que fazem parte dos padrões
            // (com algum campo exigido). Os demais aparecem só na etapa 3, para trocar a classe se for o caso.
            bool temPadrao = _temPsets || _ids != null;
            List<(Element E, string Entidade)> elementos = temPadrao ? exportados.Where(x => Exigencias(x.Entidade).Count > 0).ToList() : exportados;
            List<(Element E, string Entidade)> foraDoPadrao = temPadrao ? exportados.Where(x => Exigencias(x.Entidade).Count == 0).ToList() : new List<(Element, string)>();
            r.TotalElementos = elementos.Count;
            r.ForaDoPadrao = foraDoPadrao.Count;
            r.ClassesDoPadrao.AddRange(ClassesDoPadrao());
            r.Esquema = CategoriasIfc.EsquemaDaVersao(_setup?.Versao.ToString()) ?? (_setup == null ? CategoriasIfc.Ifc4x3 : CategoriasIfc.Ifc4);
            r.Ids = _ids;

            EtapaConferencia parametros = new EtapaConferencia
            {
                Numero = 2, Titulo = "Parâmetros e mapeamento",
                Descricao = "Cada campo exigido sai de um parâmetro que existe nas categorias, com tipo compatível e o GUID do MetroShared.",
                Correcao = Correcao.ParametrosDeProjeto,
            };
            EtapaConferencia classes = ConferirClasses(elementos, foraDoPadrao);
            EtapaConferencia dados = ConferirDados(elementos, r, parametros);
            EtapaConferencia classificacao = ConferirClassificacao(elementos.Select(x => x.E).ToList(), out int classOk, out int classTotal);

            if (!_temPsets && _ids == null)
            {
                parametros.Sinal = Sinal.Cinza;
                parametros.Resumo = "Sem .txt de property sets nem IDS nos padrões: nada para conferir.";
                dados.Sinal = Sinal.Cinza;
                dados.Resumo = parametros.Resumo;
            }
            else
            {
                parametros.CalcularSinal();
                parametros.Contagem = parametros.Pendencias.Count(p => p.Sinal != Sinal.Verde && p.Sinal != Sinal.Cinza);
                if (parametros.Resumo == null)
                    parametros.Resumo = parametros.Contagem == 0
                        ? "Todos os parâmetros dos campos exigidos existem e conferem."
                        : $"{parametros.Contagem} problema(s) de parâmetro ou de mapeamento.";
            }

            r.Etapas.Add(arquivos);
            r.Etapas.Add(parametros);
            r.Etapas.Add(classes);
            r.Etapas.Add(dados);
            r.Etapas.Add(classificacao);

            int total = r.Matriz.Values.Where(c => c.NaoConferido == null).Sum(c => c.Total) + classTotal;
            int ok = r.Matriz.Values.Where(c => c.NaoConferido == null).Sum(c => c.Ok) + classOk;
            r.Progresso = total == 0 ? 0 : (double)ok / total;
            return r;
        }

        /// <summary>Lê o IDS do pacote e roda a verificação completa do Verificar IDS (para a janela de detalhes).</summary>
        public static (IdsDocument Ids, List<CheckResult> Resultados) DetalhesIds(Document doc, PacoteMetro pacote, ElementId vista)
        {
            var c = new ConferenciaEntrega(doc, pacote, vista);
            c.ConferirArquivos();
            if (c._ids == null) return (null, null);
            c._analisador = new MappingAnalyzer(doc, c._mapping, checkValues: true);
            List<Element> elementos = c.ElementosExportados().Select(x => x.E).ToList();
            List<CheckResult> res = c._analisador.Run(c._ids, elementos);
            foreach (string aviso in c._mapping.Warnings)
                res.Add(new CheckResult { Especificacao = "(arquivo de mapeamento)", Status = CheckStatus.Aviso, Detalhe = aviso });
            return (c._ids, res);
        }

        // ================================================================== 1. arquivos

        private EtapaConferencia ConferirArquivos()
        {
            var etapa = new EtapaConferencia
            {
                Numero = 1, Titulo = "Arquivos do Metrô",
                Descricao = "Os arquivos dos padrões foram achados neste computador e estão no formato certo.",
                Correcao = Correcao.EditarPacote,
            };

            void Linha(Sinal s, string assunto, string problema, string fazer = null, Correcao correcao = Correcao.EditarPacote) =>
                etapa.Pendencias.Add(new Pendencia { Sinal = s, Assunto = assunto, Problema = problema, OQueFazer = fazer, Correcao = correcao });

            string Nome(string c) => Path.GetFileName(c);

            // IDS (opcional)
            if (string.IsNullOrWhiteSpace(_pacote.Ids))
                Linha(Sinal.Cinza, "IDS", "Sem IDS nos padrões: os campos exigidos vêm do .txt de property sets.");
            else if (!File.Exists(_pacote.Ids))
                Linha(Sinal.Vermelho, "IDS", $"Arquivo não encontrado: {_pacote.Ids}", "Confira o caminho nos padrões (ou se o Desktop Connector sincronizou a pasta).");
            else
            {
                try
                {
                    _ids = IdsReader.Load(_pacote.Ids);
                    Linha(Sinal.Verde, "Padrões Metrô (.ids)", $"{Nome(_pacote.Ids)}: {_ids.Specifications.Count} especificações" + (string.IsNullOrEmpty(_ids.Title) ? "" : $" ({_ids.Title})"));
                }
                catch (Exception ex) { Linha(Sinal.Vermelho, "Padrões Metrô (.ids)", $"{Nome(_pacote.Ids)} não pôde ser lido: {ex.Message}"); }
            }

            // Property sets (obrigatório)
            if (string.IsNullOrWhiteSpace(_pacote.PropertySets))
                Linha(Sinal.Vermelho, "Property sets (.txt)", "Os padrões não indicam o .txt de property sets do Metrô.", "Edite os padrões e escolha o .txt.");
            else if (!File.Exists(_pacote.PropertySets))
                Linha(Sinal.Vermelho, "Property sets (.txt)", $"Arquivo não encontrado: {_pacote.PropertySets}", "Confira o caminho nos padrões.");
            else
            {
                try
                {
                    _mapping.LoadUserDefinedPropertySets(_pacote.PropertySets);
                    _temPsets = _mapping.Properties.Count > 0;
                    int psets = _mapping.Properties.Select(p => p.PropertySet).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                    Linha(_temPsets ? Sinal.Verde : Sinal.Vermelho, "Property sets (.txt)",
                        _temPsets ? $"{Nome(_pacote.PropertySets)}: {psets} property sets, {_mapping.Properties.Count} propriedades"
                                  : $"{Nome(_pacote.PropertySets)} não tem nenhuma propriedade.");
                    foreach (string aviso in _mapping.Warnings)
                        Linha(Sinal.Amarelo, "Property sets (.txt)", aviso, "Corrija a linha no .txt (Editar property sets).", Correcao.EditarPropertySets);
                    foreach (var tipo in _mapping.Properties
                                 .Where(p => ExportMapping.FileTypeToIfc(p.FileType) == null)
                                 .GroupBy(p => p.FileType, StringComparer.OrdinalIgnoreCase))
                    {
                        string exemplos = string.Join(", ", tipo.Take(4).Select(p => $"{p.PropertySet}.{p.Property}")) + (tipo.Count() > 4 ? "…" : "");
                        Linha(Sinal.Amarelo, "Property sets (.txt)",
                            $"Tipo \"{tipo.Key}\" desconhecido pelo exportador; a propriedade sai como texto ({exemplos}).",
                            "Use um tipo válido: Text, Label, Boolean, Integer, Real, Length, Area, Volume, Ratio... (Editar property sets).",
                            Correcao.EditarPropertySets);
                    }
                }
                catch (Exception ex) { Linha(Sinal.Vermelho, "Property sets (.txt)", $"{Nome(_pacote.PropertySets)} não pôde ser lido: {ex.Message}"); }
            }

            // Setup de exportação (obrigatório para exportar)
            if (string.IsNullOrWhiteSpace(_pacote.Setup))
                Linha(Sinal.Vermelho, "Setup de exportação (.json)", "Os padrões não indicam o setup de exportação IFC.", "Edite os padrões e escolha o .json.");
            else if (!File.Exists(_pacote.Setup))
                Linha(Sinal.Vermelho, "Setup de exportação (.json)", $"Arquivo não encontrado: {_pacote.Setup}", "Confira o caminho nos padrões.");
            else
            {
                try
                {
                    _setup = IfcExportSetup.Load(_pacote.Setup, Path.GetDirectoryName(_pacote.Setup));
                    Linha(Sinal.Verde, "Setup de exportação (.json)", $"{Nome(_pacote.Setup)}: {_setup.Nome} ({_setup.DescricaoVersao})");
                    if (_temPsets && !_setup.ExportaPsetsUsuario)
                        Linha(Sinal.Vermelho, "Setup de exportação (.json)",
                            "O setup não exporta os property sets definidos pelo usuário: os dados do Metrô não vão para o IFC.",
                            "No exportador do Revit, marque \"Exportar conjuntos de propriedades definidos pelo usuário\" e salve o setup de novo.");
                    if (_setup.SoVisiveisNaVista)
                        Linha(Sinal.Cinza, "Setup de exportação (.json)",
                            "O setup exporta só o que está visível na vista escolhida: confira com o escopo \"Vista ativa\".");
                }
                catch (Exception ex) { Linha(Sinal.Vermelho, "Setup de exportação (.json)", $"{Nome(_pacote.Setup)} não pôde ser lido: {ex.Message}"); }
            }

            // Tabela de classes IFC (opcional)
            if (string.IsNullOrWhiteSpace(_pacote.ClassesIfc))
                Linha(Sinal.Cinza, "Tabela de classes IFC (.txt)", "Sem tabela de classes: usa as classes padrão do exportador e o \"Exportar para IFC como\" dos elementos.");
            else if (!File.Exists(_pacote.ClassesIfc))
                Linha(Sinal.Vermelho, "Tabela de classes IFC (.txt)", $"Arquivo não encontrado: {_pacote.ClassesIfc}", "Confira o caminho nos padrões.");
            else
            {
                try
                {
                    _mapping.LoadCategoryMapping(_pacote.ClassesIfc);
                    Linha(Sinal.Verde, "Tabela de classes IFC (.txt)", $"{Nome(_pacote.ClassesIfc)}: {_mapping.Categories.Count} linhas");
                }
                catch (Exception ex) { Linha(Sinal.Vermelho, "Tabela de classes IFC (.txt)", $"{Nome(_pacote.ClassesIfc)} não pôde ser lido: {ex.Message}"); }
            }

            // Planilha de classificação
            if (string.IsNullOrWhiteSpace(_pacote.Planilha))
                Linha(Sinal.Amarelo, "Planilha de classificação", "Sem planilha: a classificação só é conferida quanto a estar preenchida e no formato Código:Descrição.",
                    "Edite os padrões e escolha a planilha (.xlsx ou .csv).");
            else if (!File.Exists(_pacote.Planilha))
                Linha(Sinal.Vermelho, "Planilha de classificação", $"Arquivo não encontrado: {_pacote.Planilha}", "Confira o caminho nos padrões.");
            else
            {
                try
                {
                    _planilha = ClassificationList.Ler(_pacote.Planilha);
                    Linha(_planilha.Itens.Count > 0 ? Sinal.Verde : Sinal.Vermelho, "Planilha de classificação",
                        $"{Nome(_pacote.Planilha)}: {_planilha.Itens.Count} itens" + (string.IsNullOrEmpty(_planilha.Edicao) ? "" : $" (edição {_planilha.Edicao})"));
                    foreach (string aviso in _planilha.Avisos.Take(10))
                        Linha(Sinal.Amarelo, "Planilha de classificação", aviso);
                    if (_planilha.Itens.Count == 0) _planilha = null;
                }
                catch (Exception ex) { Linha(Sinal.Vermelho, "Planilha de classificação", $"{Nome(_pacote.Planilha)} não pôde ser lida: {ex.Message}"); }
            }

            // MetroShared (base de parâmetros compartilhados)
            if (string.IsNullOrWhiteSpace(_pacote.MetroShared))
                Linha(Sinal.Amarelo, "MetroShared (.txt)", "Sem a base MetroShared: os GUIDs dos parâmetros não são conferidos.", "Edite os padrões e escolha o .txt do MetroShared.");
            else if (!File.Exists(_pacote.MetroShared))
                Linha(Sinal.Vermelho, "MetroShared (.txt)", $"Arquivo não encontrado: {_pacote.MetroShared}", "Confira o caminho nos padrões.");
            else
            {
                SharedParamTxt txt = SharedParamTxt.Ler(_pacote.MetroShared);
                if (txt.Erro != null || txt.Definicoes.Count == 0)
                    Linha(Sinal.Vermelho, "MetroShared (.txt)", $"{Nome(_pacote.MetroShared)}: " + (txt.Erro ?? "nenhum parâmetro compartilhado no arquivo."));
                else
                {
                    _metroShared = txt;
                    Linha(Sinal.Verde, "MetroShared (.txt)", $"{Nome(_pacote.MetroShared)}: {txt.Definicoes.Count} parâmetros");
                }
            }

            etapa.CalcularSinal();
            etapa.Contagem = etapa.Pendencias.Count(p => p.Sinal == Sinal.Vermelho || p.Sinal == Sinal.Amarelo);
            etapa.Resumo = etapa.Contagem == 0
                ? "Arquivos encontrados e válidos."
                : $"{etapa.Pendencias.Count(p => p.Sinal == Sinal.Vermelho)} problema(s) e {etapa.Pendencias.Count(p => p.Sinal == Sinal.Amarelo)} aviso(s) nos arquivos.";
            return etapa;
        }

        // ================================================================== elementos


        /// <summary>Elementos que vão para o IFC (modelo ou vista), com a classe IFC de cada um.</summary>
        private List<(Element E, string Entidade)> ElementosExportados()
        {
            var lista = new List<(Element, string)>();
            ElementId fase = FiltroIfc.Fase(_doc, _setup?.Fase ?? -1, _vista);
            // Categorias marcadas "não exportar" em Categorias IFC não vão para o IFC: não entram como proxy nem como erro.
            HashSet<string> naoExportadas = CategoriasIfc.NaoExportadas(_doc,
                CategoriasIfc.EsquemaDaVersao(_setup?.Versao.ToString()) ?? (_setup == null ? CategoriasIfc.Ifc4x3 : CategoriasIfc.Ifc4));
            foreach (Element e in ClassificationCore.Elementos(_doc, null, _vista, soIfc: false))
            {
                if (!FiltroIfc.VaiParaIfc(_doc, e, fase)) continue;
                if (naoExportadas.Contains(e.Category.Name) && !TemClasseNoElemento(e)) continue;
                string entidade = _analisador.ResolveEntity(e);
                if (entidade == null) continue;
                lista.Add((e, entidade));
            }
            return lista;
        }

        /// <summary>"Exportar para IFC como" preenchido na instância ou no tipo: vale mais que a tabela de categorias.</summary>
        private bool TemClasseNoElemento(Element e)
        {
            string valor = Compat.ExportarComo(e, false)?.AsString();
            if (string.IsNullOrWhiteSpace(valor)) valor = Compat.ExportarComo(_doc.GetElement(e.GetTypeId()), true)?.AsString();
            return !string.IsNullOrWhiteSpace(valor) && !valor.Trim().StartsWith("DontExport", StringComparison.OrdinalIgnoreCase);
        }

        // ================================================================== 3. classes IFC

        private EtapaConferencia ConferirClasses(List<(Element E, string Entidade)> elementos, List<(Element E, string Entidade)> foraDoPadrao)
        {
            var etapa = new EtapaConferencia
            {
                Numero = 3, Titulo = "Classe IFC dos elementos",
                Descricao = "Só entram na conferência os elementos de classes que fazem parte dos padrões. Os que saem como proxy ou numa " +
                            "classe sem campos do Metrô aparecem aqui: escolha a classe certa e clique em Definir classe.",
                Correcao = Correcao.CategoriasIfc,
            };

            foreach (var g in foraDoPadrao.GroupBy(x => (Cat: x.E.Category.Name, x.Entidade)).OrderByDescending(g => g.Count()))
            {
                bool proxy = g.Key.Entidade.Equals("IfcBuildingElementProxy", StringComparison.OrdinalIgnoreCase);
                etapa.Pendencias.Add(new Pendencia
                {
                    Sinal = proxy ? Sinal.Amarelo : Sinal.Cinza, Categoria = g.Key.Cat, Assunto = g.Key.Entidade,
                    Problema = proxy
                        ? $"{g.Count()} elemento(s) vão para o IFC como proxy (sem classe definida): ficam fora da conferência."
                        : $"{g.Key.Entidade} não faz parte dos padrões: {g.Count()} elemento(s) ficam fora da conferência.",
                    OQueFazer = proxy
                        ? "Defina a classe certa (grava o \"Exportar para IFC como\" do tipo) ou mude a categoria inteira em Categorias IFC."
                        : "Normal se o Metrô não pede dados desse elemento; senão, defina a classe certa abaixo.",
                    Elementos = g.Select(x => x.E.Id).ToList(),
                });
            }

            if (_ids != null)
            {
                var presentes = new HashSet<string>(elementos.Select(x => x.Entidade), StringComparer.OrdinalIgnoreCase);
                foreach (IdsSpecification spec in _ids.Specifications)
                {
                    List<IdsEntityFacet> ents = spec.ApplicableEntities.ToList();
                    if (ents.Count == 0 || presentes.Any(p => ents.Any(f => Bate(f.Name, p)))) continue;
                    etapa.Pendencias.Add(new Pendencia
                    {
                        Sinal = Sinal.Cinza, Assunto = string.Join(", ", ents.Select(f => f.ToString())),
                        Problema = $"Nenhum elemento exporta como a entidade da especificação \"{spec.Name}\" dos padrões.",
                        OQueFazer = "Normal se este modelo não tem esse tipo de elemento; senão, confira a classe IFC.",
                    });
                }
            }

            etapa.CalcularSinal();
            int proxies = etapa.Pendencias.Where(p => p.Sinal == Sinal.Amarelo).Sum(p => p.QtdElementos);
            etapa.Contagem = proxies;
            int outros = foraDoPadrao.Count - proxies;
            etapa.Resumo = foraDoPadrao.Count == 0
                ? $"{elementos.Count} elementos em classes dos padrões."
                : $"{elementos.Count} nos padrões; {proxies} proxy(s) e {outros} em classes fora dos padrões.";
            return etapa;
        }

        /// <summary>Classes IFC com algum campo nos padrões (.txt de property sets e IDS). O exportador aceita o nome em qualquer caixa.</summary>
        private IEnumerable<string> ClassesDoPadrao()
        {
            var classes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PropertyMapping m in _mapping.Properties)
                foreach (string v in m.Entities)
                    if (v.StartsWith("Ifc", StringComparison.OrdinalIgnoreCase)) classes.Add(v.Trim());
            if (_ids != null)
                foreach (IdsSpecification spec in _ids.Specifications)
                    foreach (IdsEntityFacet f in spec.ApplicableEntities)
                        foreach (string v in f.Name?.FixedValues ?? Enumerable.Empty<string>())
                            if (!string.IsNullOrWhiteSpace(v)) classes.Add(v.Trim());
            return classes.OrderBy(c => c, StringComparer.OrdinalIgnoreCase);
        }

        private static bool EhEspaco(string entidade) =>
            entidade.Equals("IfcSpace", StringComparison.OrdinalIgnoreCase) || entidade.Equals("IfcZone", StringComparison.OrdinalIgnoreCase);

        // ================================================================== 4. dados (e 2. parâmetros)

        private EtapaConferencia ConferirDados(List<(Element E, string Entidade)> elementos, ResultadoConferencia r, EtapaConferencia parametros)
        {
            var etapa = new EtapaConferencia
            {
                Numero = 4, Titulo = "Dados preenchidos",
                Descricao = "Os campos exigidos pelo Metrô estão preenchidos" + (_ids != null ? " e com os valores aceitos pelos padrões." : "."),
                Correcao = Correcao.EditarEmMassa,
            };

            // Parâmetros conferidos uma vez: tipo de dado e GUID.
            var parametroConferido = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Faltas de parâmetro: (parâmetro, categoria) → elementos.
            var semParametro = new Dictionary<(string Param, string Cat), List<ElementId>>();
            var camposDoParametro = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var campos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Ui.Progresso.Atual?.Etapa($"Conferindo os dados de {elementos.Count} elementos", elementos.Count);
            foreach (var (e, entidade) in elementos)
            {
                Ui.Progresso.Atual?.Avancar(e.Category.Name);
                List<Exigencia> exigencias = Exigencias(entidade);
                if (exigencias.Count == 0) continue;
                string categoria = e.Category.Name;
                string nome = null;

                foreach (Exigencia x in exigencias)
                {
                    campos.Add(x.Campo);
                    if (!r.Matriz.TryGetValue((categoria, x.Campo), out CelulaMatriz c))
                    {
                        c = new CelulaMatriz { Categoria = categoria, Campo = x.Campo, Parametro = x.Parametro, NoTipo = x.NoTipo == true, ValorExigido = x.Valor?.ToString() };
                        if (x.Valor != null) c.Opcoes.AddRange(x.Valor.FixedValues.Where(v => !string.IsNullOrEmpty(v)));
                        r.Matriz[(categoria, x.Campo)] = c;
                    }

                    if (x.SemMapeamento)
                    {
                        c.SemMapeamento = true;
                        c.Total++;
                        continue;
                    }

                    Parameter prm = Procurar(e, x.Parametro, x.NoTipo);
                    if (prm == null)
                    {
                        if (x.PadraoBuildingSmart) continue; // o exportador pode calcular/usar parâmetro interno
                        c.Total++;
                        c.Pendentes.Add(new ElementoPendente { Id = e.Id, Tipo = nome ??= ClassificationCore.NomeElemento(_doc, e), Problema = ProblemaSemParametro });
                        var k = (x.Parametro, categoria);
                        if (!semParametro.TryGetValue(k, out List<ElementId> ids)) semParametro[k] = ids = new List<ElementId>();
                        ids.Add(e.Id);
                        if (!camposDoParametro.TryGetValue(x.Parametro, out HashSet<string> cs)) camposDoParametro[x.Parametro] = cs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        cs.Add(x.Campo);
                        continue;
                    }

                    c.Total++;
                    if (parametroConferido.Add(x.Parametro + "|" + x.TipoArquivo)) ConferirParametro(prm, x, parametros);

                    string valor = MappingAnalyzer.ValueAsString(prm);
                    if (string.IsNullOrWhiteSpace(valor))
                        c.Pendentes.Add(new ElementoPendente { Id = e.Id, Tipo = nome ??= ClassificationCore.NomeElemento(_doc, e), Problema = ProblemaVazio });
                    else if (x.Valor != null && !MappingAnalyzer.ValueMatches(x.Valor, valor))
                        c.Pendentes.Add(new ElementoPendente { Id = e.Id, Tipo = nome ??= ClassificationCore.NomeElemento(_doc, e), Problema = ProblemaForaDoExigido, Valor = valor });
                    else
                        c.Ok++;
                }
            }

            foreach (CelulaMatriz c in r.Matriz.Values)
            {
                if (c.Total == 0) c.NaoConferido = "Pset padrão sem o parâmetro: o exportador calcula ou usa um parâmetro interno.";
            }

            // Linhas e colunas da matriz: categorias com mais elementos primeiro; campos na ordem do .txt/IDS.
            foreach (var g in elementos.GroupBy(x => x.E.Category.Name).OrderByDescending(g => g.Count()))
            {
                string classe = string.Join(", ", g.Select(x => x.Entidade).Distinct(StringComparer.OrdinalIgnoreCase).Take(3));
                if (r.Matriz.Keys.Any(k => k.Categoria == g.Key)) r.Categorias.Add((g.Key, classe, g.Count()));
            }
            r.Campos.AddRange(OrdemDosCampos(campos));

            // Pendências da etapa 4: uma por célula com problema.
            foreach (CelulaMatriz c in r.Matriz.Values.Where(c => c.TemPendencia && !c.SemMapeamento)
                                                       .OrderBy(c => c.Percentual).ThenBy(c => c.Categoria))
            {
                int vazios = c.Pendentes.Count(p => p.Problema == ProblemaVazio);
                int fora = c.Pendentes.Count(p => p.Problema == ProblemaForaDoExigido);
                int sem = c.Pendentes.Count(p => p.Problema == ProblemaSemParametro);
                var partes = new List<string>();
                if (vazios > 0) partes.Add($"{vazios} vazio(s)");
                if (fora > 0) partes.Add($"{fora} fora do exigido ({c.ValorExigido})");
                if (sem > 0) partes.Add($"{sem} sem o parâmetro");
                etapa.Pendencias.Add(new Pendencia
                {
                    Sinal = c.Sinal == Sinal.Verde ? Sinal.Amarelo : c.Sinal,
                    Categoria = c.Categoria,
                    Assunto = c.Campo,
                    Problema = $"{c.Ok} de {c.Total} ok · " + string.Join(", ", partes),
                    OQueFazer = sem > 0 && sem == c.Pendentes.Count
                        ? $"Vincule o parâmetro \"{c.Parametro}\" à categoria (Parâmetros de projeto)."
                        : $"Preencha \"{c.Parametro}\"" + (c.NoTipo ? " nos tipos." : ".") + (fora > 0 ? $" Valores aceitos: {c.ValorExigido}." : ""),
                    Elementos = c.Pendentes.Select(p => p.Id).ToList(),
                    Parametro = c.Parametro,
                    Correcao = sem > 0 && sem == c.Pendentes.Count ? Correcao.ParametrosDeProjeto : Correcao.EditarEmMassa,
                });
            }

            // Etapa 2: parâmetros que faltam nas categorias e problemas de mapeamento.
            // O parâmetro é de projeto? Então dá para vincular à categoria; senão, o caminho é o .txt (ou a família).
            var deProjeto = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            DefinitionBindingMapIterator it = _doc.ParameterBindings.ForwardIterator();
            while (it.MoveNext()) if (it.Key != null) deProjeto.Add(it.Key.Name);
            string txt = Path.GetFileName(_pacote.PropertySets ?? "");
            foreach (var k in semParametro.OrderByDescending(k => k.Value.Count))
            {
                string lidos = camposDoParametro.TryGetValue(k.Key.Param, out HashSet<string> cs) ? string.Join(", ", cs) : "";
                int importados = k.Value.Count(id => _doc.GetElement(id) is DirectShape);
                bool projeto = deProjeto.Contains(k.Key.Param);
                parametros.Pendencias.Add(new Pendencia
                {
                    Sinal = Sinal.Vermelho, Categoria = k.Key.Cat, Assunto = k.Key.Param, Campo = lidos, Origem = txt,
                    Problema = $"O .txt lê \"{k.Key.Param}\" para {lidos}, mas {k.Value.Count} elemento(s) não têm parâmetro com esse nome." +
                               (importados > 0 ? $" {importados} são DirectShape (geometria importada), que não têm os parâmetros das famílias." : ""),
                    OQueFazer = projeto
                        ? $"\"{k.Key.Param}\" é parâmetro de projeto, mas não está vinculado a {k.Key.Cat}: vincule à categoria."
                        : $"\"{k.Key.Param}\" não é parâmetro de projeto (é interno do Revit ou de família) e esses elementos não o têm. " +
                          $"Aponte {lidos} para um parâmetro que eles têm (Ajustar o .txt), ou crie um parâmetro de projeto com esse nome e vincule a {k.Key.Cat}.",
                    Elementos = k.Value,
                    Correcao = projeto ? Correcao.ParametrosDeProjeto : Correcao.EditarPropertySets,
                });
            }
            foreach (Pendencia p in _problemasMapeamento.Values)
            {
                p.Elementos = r.Matriz.Values.Where(c => c.Campo == p.Assunto && c.SemMapeamento)
                    .SelectMany(c => elementos.Where(x => x.E.Category.Name == c.Categoria).Select(x => x.E.Id)).Distinct().ToList();
                parametros.Pendencias.Add(p);
            }

            int elementosComPendencia = etapa.Pendencias.SelectMany(p => p.Elementos).Distinct().Count();
            etapa.CalcularSinal();
            if (etapa.Sinal != Sinal.Cinza)
            {
                etapa.Contagem = elementosComPendencia;
                etapa.Resumo = r.Matriz.Count == 0
                    ? "Nenhum campo exigido se aplica aos elementos."
                    : elementosComPendencia == 0
                        ? $"{campos.Count} campos conferidos em {r.Categorias.Count} categorias: tudo preenchido."
                        : $"{elementosComPendencia} elemento(s) com campo vazio, fora do exigido ou sem o parâmetro.";
            }
            return etapa;
        }

        /// <summary>Tipo de dado compatível com o .txt e GUID igual ao do MetroShared.</summary>
        private void ConferirParametro(Parameter prm, Exigencia x, EtapaConferencia parametros)
        {
            if (x.TipoArquivo != null && !MappingAnalyzer.TypesCompatible(x.TipoArquivo, prm))
            {
                parametros.Pendencias.Add(new Pendencia
                {
                    Sinal = Sinal.Vermelho, Assunto = x.Parametro, Campo = x.Campo, Origem = Path.GetFileName(_pacote.PropertySets ?? ""),
                    Problema = $"No Revit o parâmetro é {MappingAnalyzer.RevitTypeName(prm)}, mas o .txt exporta como \"{x.TipoArquivo}\" ({x.Campo}).",
                    OQueFazer = "Ajuste o tipo no .txt de property sets ou use um parâmetro do tipo certo.",
                    Correcao = Correcao.EditarPropertySets,
                });
            }

            if (_metroShared == null || prm.Id.Value < 0) return; // parâmetros internos do Revit não vêm do MetroShared
            _metroShared.PorNome.TryGetValue(x.Parametro, out SharedParamDef def);
            if (def == null) return;
            if (prm.IsShared && prm.GUID != def.Guid)
            {
                parametros.Pendencias.Add(new Pendencia
                {
                    Sinal = Sinal.Amarelo, Assunto = x.Parametro,
                    Problema = $"GUID diferente do MetroShared ({prm.GUID} no modelo, {def.Guid} na base). O IFC sai pelo nome, mas o parâmetro não é o do padrão.",
                    OQueFazer = "Veja em Parâmetros › Compartilhados (coluna Padrão) e troque pelo parâmetro do MetroShared.",
                    Correcao = Correcao.ListarCompartilhados,
                });
            }
            else if (!prm.IsShared)
            {
                parametros.Pendencias.Add(new Pendencia
                {
                    Sinal = Sinal.Amarelo, Assunto = x.Parametro,
                    Problema = "O parâmetro existe no MetroShared, mas no modelo não é compartilhado (é de projeto ou de família).",
                    OQueFazer = "Troque pelo parâmetro compartilhado do MetroShared.",
                    Correcao = Correcao.ListarCompartilhados,
                });
            }
        }

        /// <summary>Campos exigidos para a entidade: do IDS (obrigatórios) se houver, senão todos do .txt que se aplicam a ela.</summary>
        private List<Exigencia> Exigencias(string entidade)
        {
            if (_exigenciasPorEntidade.TryGetValue(entidade, out List<Exigencia> cache)) return cache;
            var lista = new List<Exigencia>();
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (_ids != null)
            {
                foreach (IdsSpecification spec in _ids.Specifications)
                {
                    if (!spec.ApplicableEntities.Any(f => Bate(f.Name, entidade))) continue;
                    foreach (IdsPropertyFacet p in spec.Requirements.OfType<IdsPropertyFacet>())
                    {
                        if (p.Cardinality != IdsCardinality.Required) continue;
                        string pset = p.PropertySet?.Simple, prop = p.BaseName?.Simple;
                        if (pset == null || prop == null) continue;
                        string campo = $"{pset}.{prop}";
                        if (!vistos.Add(campo)) continue;

                        PropertyMapping map = _mapping.Find(entidade, pset, prop).FirstOrDefault();
                        if (map == null)
                        {
                            bool padrao = pset.StartsWith("Pset_", StringComparison.OrdinalIgnoreCase) || pset.StartsWith("Qto_", StringComparison.OrdinalIgnoreCase);
                            lista.Add(new Exigencia { Campo = campo, Parametro = prop, Valor = p.Value, PadraoBuildingSmart = padrao, SemMapeamento = !padrao });
                            if (!padrao && !_problemasMapeamento.ContainsKey(campo))
                                _problemasMapeamento[campo] = new Pendencia
                                {
                                    Sinal = Sinal.Vermelho, Assunto = campo,
                                    Problema = $"O padrão Metrô exige {campo}, mas nenhuma linha do .txt de property sets gera esse campo para {entidade}.",
                                    OQueFazer = "Inclua a propriedade no .txt de property sets (com o parâmetro do Revit de onde ela sai).",
                                    Correcao = Correcao.EditarPropertySets,
                                };
                            continue;
                        }
                        if (p.DataType != null && map.IfcType != null && !string.Equals(p.DataType, map.IfcType, StringComparison.OrdinalIgnoreCase)
                            && !_problemasMapeamento.ContainsKey(campo + "|tipo"))
                        {
                            _problemasMapeamento[campo + "|tipo"] = new Pendencia
                            {
                                Sinal = Sinal.Vermelho, Assunto = campo,
                                Problema = $"O padrão Metrô pede {p.DataType}, mas o .txt exporta {map.IfcType} (tipo \"{map.FileType}\").",
                                OQueFazer = "Ajuste o tipo da propriedade no .txt de property sets.",
                                Correcao = Correcao.EditarPropertySets,
                            };
                        }
                        lista.Add(new Exigencia { Campo = campo, Parametro = map.RevitParameter, NoTipo = map.OnType, TipoArquivo = map.FileType, Valor = p.Value });
                    }
                }
            }
            else
            {
                foreach (PropertyMapping map in _mapping.Properties.Where(m => m.AppliesTo(entidade)))
                {
                    string campo = $"{map.PropertySet}.{map.Property}";
                    if (!vistos.Add(campo)) continue;
                    lista.Add(new Exigencia { Campo = campo, Parametro = map.RevitParameter, NoTipo = map.OnType, TipoArquivo = map.FileType });
                }
            }

            _exigenciasPorEntidade[entidade] = lista;
            return lista;
        }

        /// <summary>Colunas na ordem em que aparecem no IDS ou no .txt.</summary>
        private IEnumerable<string> OrdemDosCampos(HashSet<string> campos)
        {
            var ordem = new List<string>();
            if (_ids != null)
                ordem.AddRange(_ids.Specifications.SelectMany(s => s.Requirements.OfType<IdsPropertyFacet>())
                    .Where(p => p.PropertySet?.Simple != null && p.BaseName?.Simple != null)
                    .Select(p => $"{p.PropertySet.Simple}.{p.BaseName.Simple}"));
            ordem.AddRange(_mapping.Properties.Select(m => $"{m.PropertySet}.{m.Property}"));
            return ordem.Where(campos.Contains).Distinct(StringComparer.OrdinalIgnoreCase)
                        .Concat(campos.OrderBy(c => c)).Distinct(StringComparer.OrdinalIgnoreCase);
        }

        // ================================================================== 5. classificação

        private EtapaConferencia ConferirClassificacao(List<Element> elementos, out int ok, out int total)
        {
            string parametro = string.IsNullOrWhiteSpace(_pacote.ParametroClassificacao) ? "ClassificationCode" : _pacote.ParametroClassificacao;
            var etapa = new EtapaConferencia
            {
                Numero = 5, Titulo = "Classificação",
                Descricao = $"O parâmetro {parametro} está preenchido no formato Código:Descrição" + (_planilha != null ? " e com um item da planilha." : "."),
                Correcao = Correcao.VerificarClassificacao,
            };

            Ui.Progresso.Atual?.Etapa("Conferindo a classificação");
            var linhas = new List<(string Situacao, string Categoria, ElementId Id)>();
            if (_planilha != null)
            {
                foreach (LinhaVerificacao l in ClassificationCore.Verificar(_doc, elementos, _planilha, parametro))
                    linhas.Add((l.Situacao, l.Categoria, l.ElementoId));
            }
            else
            {
                foreach (Element e in elementos)
                {
                    AlvoClassificacao a = ClassificationCore.Alvo(_doc, e, parametro);
                    string s = a.Parametro == null ? ClassificationCore.SemParametro
                        : string.IsNullOrWhiteSpace(a.Valor) ? ClassificationCore.SemClassificacao
                        : !ValorClassificacao.Ler(a.Valor).Valido ? ClassificationCore.FormatoInvalido
                        : ClassificationCore.OK;
                    linhas.Add((s, e.Category?.Name, e.Id));
                }
            }

            total = linhas.Count;
            ok = linhas.Count(l => l.Situacao == ClassificationCore.OK);

            foreach (var g in linhas.Where(l => l.Situacao != ClassificationCore.OK)
                                    .GroupBy(l => (l.Situacao, l.Categoria))
                                    .OrderBy(g => Gravidade(g.Key.Situacao)).ThenByDescending(g => g.Count()))
            {
                string s = g.Key.Situacao;
                etapa.Pendencias.Add(new Pendencia
                {
                    Sinal = s == ClassificationCore.DescricaoDiferente || s == ClassificationCore.CategoriaNaoPrevista ? Sinal.Amarelo : Sinal.Vermelho,
                    Categoria = g.Key.Categoria,
                    Assunto = s,
                    Problema = $"{g.Count()} elemento(s)",
                    OQueFazer =
                        s == ClassificationCore.SemParametro ? $"Vincule o parâmetro {parametro} à categoria (Aplicar Classificação cria o vínculo)."
                        : s == ClassificationCore.SemClassificacao ? "Selecione os elementos e aplique a classificação."
                        : "Abra Verificar Classificação: ela propõe a correção.",
                    Elementos = g.Select(l => l.Id).ToList(),
                    Correcao = s == ClassificationCore.SemClassificacao || s == ClassificationCore.SemParametro
                        ? Correcao.AplicarClassificacao : Correcao.VerificarClassificacao,
                });
            }

            etapa.CalcularSinal();
            etapa.Contagem = total - ok;
            int sem = linhas.Count(l => l.Situacao == ClassificationCore.SemClassificacao || l.Situacao == ClassificationCore.SemParametro);
            etapa.Resumo = etapa.Contagem == 0
                ? $"{total} elementos classificados" + (_planilha != null ? " com itens da planilha." : ".")
                : $"{sem} sem classificação; {etapa.Contagem - sem} com código ou escrita a corrigir.";
            return etapa;
        }

        private static int Gravidade(string situacao) =>
            situacao == ClassificationCore.SemParametro ? 0
            : situacao == ClassificationCore.SemClassificacao ? 1
            : situacao == ClassificationCore.FormatoInvalido ? 2
            : situacao == ClassificationCore.ForaDaLista ? 3
            : 4;

        // ================================================================== utilitários

        /// <summary>Parâmetro na instância e/ou no tipo (na ordem que o .txt indica).</summary>
        private Parameter Procurar(Element e, string nome, bool? noTipo)
        {
            Element tipo = _doc.GetElement(e.GetTypeId());
            if (noTipo == true) return tipo?.LookupParameter(nome) ?? e.LookupParameter(nome);
            return e.LookupParameter(nome) ?? tipo?.LookupParameter(nome);
        }

        /// <summary>A entidade do IDS (nome ou enumeração, em qualquer caixa) bate com a classe do elemento.</summary>
        private static bool Bate(IdsValue v, string entidade)
        {
            if (v == null) return true;
            if (v.FixedValues.Any()) return v.FixedValues.Any(x => string.Equals(x, entidade, StringComparison.OrdinalIgnoreCase));
            if (v.Pattern != null)
                return System.Text.RegularExpressions.Regex.IsMatch(entidade, "^(?:" + v.Pattern + ")$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return false;
        }
    }
}
