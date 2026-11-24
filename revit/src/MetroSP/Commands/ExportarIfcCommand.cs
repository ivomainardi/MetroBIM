using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MetroSP.Ids;

namespace MetroSP.Commands
{
    /// <summary>
    /// Exporta o IFC com um setup .json e o .txt de property sets guardados numa pasta local ou no Autodesk Forma.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ExportarIfcCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            Exportar(commandData.Application);

        /// <summary>Abre a janela e exporta. Chamar dentro do contexto da API (comando ou ExternalEvent).</summary>
        public static Result Exportar(UIApplication uiapp)
        {
            Document doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null || doc.IsFamilyDocument)
            {
                TaskDialog.Show("MetroSP", "Abra um projeto (.rvt) para exportar o IFC.");
                return Result.Cancelled;
            }

            IfcExportSettings settings = IfcExportSettings.Load();
            var janela = new ExportarIfcWindow(doc, settings, SincronizarForma());
            new WindowInteropHelper(janela).Owner = uiapp.MainWindowHandle;
            bool ok = janela.ShowDialog() == true;
            settings.Save();
            // Cancelado ou Falhou faz o Revit desfazer tudo do comando, inclusive o Location gravado pela janela.
            Result Fim(Result r) => janela.AlteracoesLocalizacao.Count > 0 ? Result.Succeeded : r;
            if (janela.AbrirPassoAPasso) { Painel.EntregaWindow.Abrir(uiapp, Painel.EntregaWindow.PassoExportar); return Fim(Result.Succeeded); }
            if (!ok) return Fim(Result.Cancelled);
            return Fim(ExportarCom(uiapp, janela));
        }

        /// <summary>Pasta do Forma online: baixa o que mudou lá desde a última vez (sem rede, vale a última cópia). Null se não usa.</summary>
        internal static string SincronizarForma()
        {
            if (!FormaOnline.Conectado || FormaOnline.PastaEscolhida() == null) return null;
            using (var p = Ui.Progresso.Abrir("Forma online"))
            {
                p.Etapa("Conferindo atualizações das configurações de IFC no Forma");
                return FormaOnline.Sincronizar().ToString();
            }
        }

        /// <summary>
        /// Exporta com o que foi escolhido na janela (aberta direto ou embutida no passo Exportar da Entrega) e mostra o resultado.
        /// Chamar dentro do contexto da API.
        /// </summary>
        public static Result ExportarCom(UIApplication uiapp, ExportarIfcWindow janela)
        {
            Document doc = janela.Documento;
            IfcExportSetup setup = janela.Setup;
            string destino = Path.Combine(janela.PastaSaida, janela.NomeArquivo + ".ifc");
            // O exportador grava e o MetroSP completa o IFC numa pasta temporária local; no fim o arquivo vai para a pasta
            // escolhida de uma vez. Numa pasta do Desktop Connector ou da rede, cada gravação seria um envio inteiro.
            string pastaTemp = Path.Combine(Path.GetTempPath(), "MetroSP-IFC-" + Guid.NewGuid().ToString("N"));
            string arquivo = Path.Combine(pastaTemp, janela.NomeArquivo + ".ifc");
            Stopwatch relogio = Stopwatch.StartNew();
            TimeSpan tPreparo = TimeSpan.Zero, tExportador = TimeSpan.Zero;

            // A localização é gravada na etapa 1 (Localização IFC); aqui o IFC sai com o que está no Revit.
            LocalizacaoIfc local = janela.Localizacao;
            LocalizacaoIfc.Escolha escolha = local.Atual();
            List<string> alteracoes = janela.AlteracoesLocalizacao;

            bool exportou;
            string avisoPsets = null, avisoCategorias = null, avisoCoordenacao = null, avisoAco = null;
            string interrompida = RastroExportacao.Interrompida();
            string passo = "montar as opções do setup";
            RastroExportacao.Passo(passo);
            EixosIfc eixos = null;
            AreasIfc areas = null;
            AcoIfc aco = null;
            Dictionary<long, string> classesPedidas = null;
            TimeSpan tCorrecoes = TimeSpan.Zero;
            try
            {
                Directory.CreateDirectory(pastaTemp);
                IFCExportOptions opcoes = setup.CriarOpcoes(doc, janela.PropertySetsEscolhido, janela.MapeamentoEscolhido, janela.Vista, janela.EpsgEmBranco, janela.Fase);

                // O exportador precisa de uma transação aberta; ela é desfeita no fim,
                // a não ser que o setup grave o GUID IFC nos elementos (como faz a janela do Revit).
                using (var t = new Transaction(doc, "Exportar IFC"))
                {
                    t.Start();
                    FailureHandlingOptions falhas = t.GetFailureHandlingOptions();
                    falhas.SetClearAfterRollback(true);
                    t.SetFailureHandlingOptions(falhas);

                    // Revit 2027+: o exportador lê os psets do modelo, não do .txt.
                    RastroExportacao.Passo(passo = "carregar os property sets no modelo");
                    if (!string.IsNullOrEmpty(janela.PropertySetsEscolhido))
                        avisoPsets = PsetsNoModelo.Carregar(doc, janela.PropertySetsEscolhido);

                    // Tabela de categorias → classe IFC do esquema do setup (janela Categorias IFC), se houver.
                    RastroExportacao.Passo(passo = "aplicar a tabela de categorias IFC");
                    avisoCategorias = CategoriasIfc.AplicarNaExportacao(doc, setup.Versao.ToString());

                    // Arquivo de coordenação: classes IFC por família e por elemento do perfil ativo, só nesta exportação.
                    RastroExportacao.Passo(passo = "aplicar as classes por família do arquivo de coordenação");
                    avisoCoordenacao = Coordenacao.CoordenacaoRevit.AplicarNaExportacao(doc);

                    // Áreas do Revit (planos de área) nunca vão para o IFC: sairiam como IfcSpace. Só nesta exportação.
                    passo = "deixar as áreas fora do IFC";
                    areas = AreasIfc.Preparar(doc);

                    // Sempre exportar os eixos: Grids visíveis na vista filtrada e exportáveis na tabela, só nesta exportação.
                    if (janela.SempreEixos)
                    {
                        RastroExportacao.Passo(passo = "preparar os eixos (Grids)");
                        eixos = EixosIfc.Preparar(doc, janela.Vista);
                    }

                    // Ligações metálicas: parafusos, chapas e soldas como elementos próprios (o exportador só separa no IFC 2x3).
                    RastroExportacao.Passo(passo = "separar as peças das ligações metálicas");
                    if (setup.VersaoAceita && AcoIfc.TemAco(doc))
                    {
                        try { aco = AcoIfc.Preparar(doc, janela.Vista, FiltroIfc.Fase(doc, janela.Fase, janela.Vista)); }
                        catch (Exception ex) { aco = null; avisoAco = "Ligações metálicas: não deu para separar as peças (" + ex.Message + "); saíram como o exportador do Revit faz."; }
                    }

                    using (var progresso = Ui.Progresso.Abrir("Exportar IFC"))
                    {
                        progresso.Etapa($"Exportando {janela.NomeArquivo}.ifc com o setup {setup.Nome}");
                        progresso.Item("O exportador do Revit não informa o andamento; em modelos grandes pode levar alguns minutos.");
                        RastroExportacao.Passo(passo = "exportar (exportador do Revit)");
                        tPreparo = relogio.Elapsed;
                        exportou = doc.Export(pastaTemp, janela.NomeArquivo + ".ifc", opcoes);
                        tExportador = relogio.Elapsed - tPreparo;
                    }
                    // Classe pedida dos elementos que saíram como proxy: lida ainda aqui, porque o "Exportar para IFC como" do
                    // arquivo de coordenação só existe nesta transação.
                    if (exportou && File.Exists(arquivo))
                    {
                        TimeSpan t0 = relogio.Elapsed;
                        try { classesPedidas = CorrecoesRevit.ClassesPedidas(doc, Ifc.CorrecoesIfc.TagsDeProxies(arquivo)); }
                        catch { classesPedidas = null; }
                        tCorrecoes += relogio.Elapsed - t0;
                    }
                    eixos?.Restaurar();
                    areas?.Restaurar();
                    aco?.Restaurar(doc);

                    if (exportou && setup.GravaGuid)
                    {
                        // Os parâmetros auxiliares dos psets (2027+) não ficam no modelo.
                        if (!string.IsNullOrEmpty(janela.PropertySetsEscolhido)) PsetsNoModelo.Finalizar(doc, janela.PropertySetsEscolhido);
                        t.Commit();
                    }
                    else t.RollBack();
                }
            }
            catch (Exception ex)
            {
                RastroExportacao.Fim();
                ApagarTemp(pastaTemp);
                TaskDialog.Show("MetroSP", $"Erro ao exportar o IFC, na etapa \"{passo}\":\n" + ex.Message +
                    (avisoCategorias != null ? "\n\n" + avisoCategorias : ""));
                return Result.Failed;
            }

            RastroExportacao.Fim();
            if (!exportou || !File.Exists(arquivo))
            {
                ApagarTemp(pastaTemp);
                TaskDialog.Show("MetroSP", "O Revit não conseguiu exportar o IFC. Confira o setup e tente pela janela de exportação do Revit para ver a mensagem do exportador.");
                return Result.Failed;
            }

            // Completar e conferir o IFC também leva tempo num modelo grande: a janela de andamento fica aberta.
            LocalizacaoIfc.Conferencia conferencia;
            Ifc.CorrecoesIfc.Resultado correcoes = null;
            bool semElementos = false;
            int gridsNoIfc = -1;
            var progressoIfc = Ui.Progresso.Abrir("Exportar IFC");
            try
            {
                progressoIfc.Etapa($"Completando e conferindo {janela.NomeArquivo}.ifc");
                progressoIfc.Item("Localização, classificação, status de fase e dados do projeto.");
                // Sem EPSG: se o exportador criou IfcMapConversion/IfcProjectedCRS (sistema GIS no Location), saem do arquivo.
                string georref = null;
                if (janela.EpsgEmBranco)
                {
                    try { georref = LocalizacaoIfc.RemoverGeorreferencia(arquivo); }
                    catch (Exception ex) { georref = "Não foi possível tirar o EPSG do IFC: " + ex.Message; }
                }
                // O IfcSite sai sempre com a lat/long do Project Base Point, mesmo que o Revit não tenha aceitado no Location.
                string latLong;
                try { latLong = local.CorrigirLatLongIfc(arquivo); }
                catch (Exception ex) { latLong = "Não foi possível corrigir a latitude/longitude do IfcSite: " + ex.Message; }
                // Classificação dos padrões (e Classification Manager) que o exportador deixou só em property set → IfcClassificationReference.
                // Dados do IfcProject, IfcSite e IfcBuilding (etapa 1) e Pset MetroSP_Projeto: completa o que o exportador não grava.
                var avisosClassificacao = new List<string>();
                var linhasDados = new List<string>();
                var linhasElementos = new List<string>();
                bool dadosComProblema = false;
                try
                {
                    var modelo = Ifc.StepModel.Load(arquivo);
                    bool salvar = false;
                    gridsNoIfc = modelo.OfType("IFCGRID").Count();
                    // Peças das ligações metálicas: dados, GlobalId estável e agrupamento na ligação (antes da contagem).
                    if (avisoAco != null) avisosClassificacao.Add(avisoAco);
                    if (interrompida != null) avisosClassificacao.Add("Exportação anterior: o Revit fechou no meio, na etapa " + interrompida + ".");
                    if (aco != null)
                    {
                        avisosClassificacao.AddRange(aco.Avisos);
                        if (aco.Pecas.Count > 0)
                        {
                            try { salvar |= Ifc.FixadoresIfc.Aplicar(modelo, aco.Pecas, avisosClassificacao.Add).Pecas > 0; }
                            catch (Exception ex) { avisosClassificacao.Add("Não foi possível completar as peças das ligações metálicas: " + ex.Message); }
                        }
                    }
                    // Vigas, pilares e barras com o corpo no dobro do comprimento (exportador do Revit): volta ao comprimento do Revit.
                    try
                    {
                        View3D vista3d = AcoIfc.Vista3D(doc, janela.Vista);
                        int semCorpo = 0;
                        var perfis = Ifc.PerfisIfc.Aplicar(modelo, id =>
                        {
                            Ifc.PerfilRevit p = PerfilRevit(doc, id, vista3d);
                            if (p != null && double.IsNaN(p.Ini)) semCorpo++;
                            return p;
                        });
                        if (perfis.Extrusoes > 0) salvar = true;
                        if (perfis.ToString() is string linha) avisosClassificacao.Add(linha);
                        if (semCorpo > 0)
                            avisosClassificacao.Add($"Perfis metálicos: a geometria de {semCorpo} barra(s) não foi lida pela API; elas ficaram com a barra inteira, sem os recortes das ligações.");
                    }
                    catch (Exception ex) { avisosClassificacao.Add("Não foi possível conferir o comprimento dos perfis metálicos: " + ex.Message); }
                    // Todos os objetos: largura, profundidade e altura do corpo no IFC contra o Revit (só confere, no registro).
                    try { avisosClassificacao.AddRange(Ifc.ConferenciaGeometriaIfc.Aplicar(modelo, id => CaixaRevit(doc, id)).Linhas()); }
                    catch (Exception ex) { avisosClassificacao.Add("Não foi possível conferir as medidas dos objetos: " + ex.Message); }
                    // O que saiu: elementos por classe e, se o IFC saiu só com ambientes, a causa provável (fase ou vista).
                    try
                    {
                        linhasElementos = ElementosNoIfc.Conferir(doc, ElementosNoIfc.Contar(modelo), janela.Vista,
                            FiltroIfc.Fase(doc, janela.Fase, janela.Vista), out semElementos);
                    }
                    catch (Exception ex) { linhasElementos.Add("Não foi possível contar os elementos do IFC: " + ex.Message); }
                    try { salvar |= Ifc.IfcClassificacao.Aplicar(modelo, Ifc.RegraIfc.DosPadroes(), avisosClassificacao.Add) > 0; }
                    catch (Exception ex) { avisosClassificacao.Add("Não foi possível gravar a classificação como IfcClassificationReference: " + ex.Message); }
                    // Status de fase (Pset_*Common.Status e Estado do Pset Metro) na fase exportada.
                    try
                    {
                        ElementId faseIfc = FiltroIfc.Fase(doc, janela.Fase, janela.Vista);
                        string nomeFase = doc.GetElement(faseIfc)?.Name ?? "?";
                        var (comStatus, comEstado) = Ifc.StatusFaseIfc.Aplicar(modelo, id => StatusNaFase(doc, id, faseIfc),
                            Ifc.StatusFaseIfc.ClassesComPsetMetro(janela.PropertySetsEscolhido), avisosClassificacao.Add);
                        if (comStatus + comEstado > 0)
                        {
                            salvar = true;
                            avisosClassificacao.Add($"Fase {nomeFase}: Status em {comStatus} elemento(s) (Pset_*Common) e Estado em {comEstado} (Pset Metro).");
                        }
                    }
                    catch (Exception ex) { avisosClassificacao.Add("Não foi possível gravar o status da fase: " + ex.Message); }
                    // Armaduras: Count, Length e Weight de cada barra (Qto_ReinforcingElementBaseQuantities), que o exportador
                    // não grava, inclusive nos conjuntos de comprimento variável.
                    try
                    {
                        var armaduras = Ifc.ArmaduraIfc.Aplicar(modelo, id => ArmaduraRevit.Barras(doc, id));
                        if (armaduras.Gravadas > 0) salvar = true;
                        if (armaduras.ToString() is string linha) avisosClassificacao.Add(linha);
                    }
                    catch (Exception ex) { avisosClassificacao.Add("Não foi possível gravar as quantidades das armaduras: " + ex.Message); }
                    // Correções do exportador: classe IFC dos proxies, lances de escada/rampa, telas soldadas, quantidades base,
                    // MetroQto, sistemas MEP e materiais (janela "O que o MetroSP corrige no IFC").
                    TimeSpan t1 = relogio.Elapsed;
                    try
                    {
                        string metroData = null;
                        try { metroData = MetroSP.Conferencia.PacoteMetro.Abrir()?.MetroData; } catch { }
                        var fonte = CorrecoesRevit.Fonte(doc, classesPedidas, CategoriasIfc.EsquemaDaVersao(setup.Versao.ToString()) ?? CategoriasIfc.Ifc4,
                            CorrecoesRevit.MetroQto(metroData, janela.PropertySetsEscolhido));
                        correcoes = Ifc.CorrecoesIfc.Aplicar(modelo, fonte);
                        if (correcoes.Alterou) salvar = true;
                        avisosClassificacao.AddRange(correcoes.Linhas());
                    }
                    catch (Exception ex) { avisosClassificacao.Add("Não foi possível aplicar as correções do exportador: " + ex.Message); }
                    tCorrecoes += relogio.Elapsed - t1;
                    try
                    {
                        escolha.Dados.AplicarNoIfc(modelo, out bool alterouDados);
                        salvar |= alterouDados;
                        linhasDados = escolha.Dados.Conferir(modelo, out dadosComProblema);
                    }
                    catch (Exception ex) { linhasDados.Add("Não foi possível gravar os dados do IfcProject/IfcSite/IfcBuilding: " + ex.Message); dadosComProblema = true; }
                    // Por último: Plugin e VersaoPlugin no Pset MetroSP_Projeto e a marca no cabeçalho.
                    try { Ifc.OrigemIfc.Marcar(modelo, "MetroSP Revit"); salvar = true; }
                    catch (Exception ex) { avisosClassificacao.Add("Não foi possível gravar a origem MetroSP no IFC: " + ex.Message); }
                    if (salvar) modelo.Save(arquivo);
                }
                catch (Exception ex) { avisosClassificacao.Add("Não foi possível completar o IFC (classificação e dados do projeto): " + ex.Message); }
                if (local.Erro == null)
                {
                    escolha.Latitude = local.Latitude;
                    escolha.Longitude = local.Longitude;
                }
                conferencia = local.ConferirIfc(arquivo, escolha, janela.EpsgEmBranco);
                if (latLong != null) conferencia.Linhas.Insert(0, latLong);
                conferencia.Linhas.InsertRange(0, linhasElementos);
                if (semElementos) conferencia.TemProblema = true;
                conferencia.Linhas.AddRange(linhasDados);
                if (dadosComProblema) conferencia.TemProblema = true;
                foreach (string a in avisosClassificacao) conferencia.Linhas.Add(char.ToUpper(a[0]) + a.Substring(1));
                if (georref != null) conferencia.Linhas.Insert(0, georref);
            }
            finally { progressoIfc.Dispose(); }

            // O IFC pronto vai para a pasta escolhida (com os IFCs dos vínculos, se o setup exporta vínculos).
            string erroCopia = MoverParaDestino(pastaTemp, janela.PastaSaida);
            if (erroCopia != null)
            {
                TaskDialog.Show("MetroSP", "O IFC foi exportado, mas não deu para gravá-lo na pasta escolhida:\n" + erroCopia +
                    "\n\nO arquivo ficou em:\n" + arquivo + "\n\nFeche o IFC se ele estiver aberto em outro programa e copie de lá.");
                return Result.Failed;
            }
            arquivo = destino;
            TimeSpan tTotal = relogio.Elapsed;
            conferencia.Linhas.Add($"Tempo: {Duracao(tTotal)} no total ({Duracao(tPreparo)} de preparação, {Duracao(tExportador)} no exportador do Revit, " +
                                   $"{Duracao(tTotal - tPreparo - tExportador)} para completar e conferir o IFC, dos quais {Duracao(tCorrecoes)} nas correções do exportador).");
            string log = local.GravarLog(doc, arquivo, setup.Nome, escolha, alteracoes, conferencia,
                uiapp.Application.VersionName, Ui.SobreWindow.Versao);
            // Valores do exportador que o MetroSP trocou (quantidades zeradas ou diferentes do Revit), para conferir depois.
            if (log != null && correcoes != null && correcoes.Trocas.Count > 0)
            {
                try { File.AppendAllLines(log, new[] { "", "Quantidades trocadas (exportador → Revit):" }.Concat(correcoes.Trocas)); }
                catch { /* o registro é só para consulta */ }
            }
            string avisosSetup =
                (setup.SitePlacementDiferente == null ? "" : $"\nO setup usa a base {setup.SitePlacementDiferente}; o IFC saiu com Shared Coordinates.") +
                (setup.EpsgDoSetup == null || !janela.EpsgEmBranco ? "" : $"\nO setup tem o EPSG {setup.EpsgDoSetup}; o IFC saiu sem EPSG.");

            var fim = new TaskDialog("MetroSP")
            {
                MainInstruction = semElementos ? "IFC exportado SEM elementos: só pavimentos e ambientes"
                    : conferencia.TemProblema ? "IFC exportado, com pendências na conferência" : "IFC exportado",
                MainContent = $"{arquivo}\n\nSetup: {setup.Nome} ({setup.DescricaoVersao}) · Shared Coordinates · {(janela.EpsgEmBranco ? "EPSG em branco" : "EPSG do setup/exportador")}\n" +
                    "Property sets: " + (janela.PropertySetsEscolhido ?? "nenhum") +
                    (avisoCategorias == null ? "" : "\n" + avisoCategorias) +
                    (avisoCoordenacao == null ? "" : "\n" + avisoCoordenacao) +
                    (areas?.Aviso == null ? "" : "\n" + areas.Aviso) +
                    TextoEixos(eixos, gridsNoIfc) +
                    (avisoPsets == null ? "" : "\n" + avisoPsets +
                        (setup.GravaGuid ? " Como o setup grava o GUID IFC, eles ficaram salvos no modelo." : " O modelo voltou a ficar como estava.")) +
                    "\n\nLocalização: " + (local.LocationCorreto ? "Location confere com o Project Base Point." :
                        local.Erro == null ? "o Location do Revit não confere com o Project Base Point; o IfcSite saiu com a latitude/longitude do Project Base Point." :
                        "ATENÇÃO, o Location não pôde ser conferido pelo Project Base Point.") +
                    (alteracoes.Count == 0 ? "" : "\nGravado no Revit nesta exportação:\n- " + string.Join("\n- ", alteracoes)) +
                    "\n" + conferencia + avisosSetup,
                ExpandedContent = local.Resumo(escolha) + (log == null ? "" : "\n\nRegistro: " + log),
                CommonButtons = TaskDialogCommonButtons.Close,
            };
            fim.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Abrir a pasta");
            if (fim.Show() == TaskDialogResult.CommandLink1)
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{arquivo}\"") { UseShellExecute = true }); }
                catch { /* sem Explorer: ignora */ }
            }
            return Result.Succeeded;
        }

        /// <summary>Linha do resumo sobre os eixos: quantos IfcGrid saíram e o que foi preciso mudar.</summary>
        private static string TextoEixos(EixosIfc eixos, int n)
        {
            if (eixos == null || eixos.Total == 0) return "";
            string linha = n > 0 ? $"\nEixos: {n} IfcGrid no IFC ({eixos.Total} eixos no modelo)."
                : n == 0 ? $"\nATENÇÃO: nenhum IfcGrid no IFC, embora o modelo tenha {eixos.Total} eixos. Tente exportar o modelo inteiro (sem filtrar pela vista)."
                : "";
            return linha + (eixos.Aviso == null ? "" : "\n" + eixos.Aviso);
        }

        /// <summary>
        /// Move os arquivos da pasta temporária para a pasta escolhida, substituindo os de mesmo nome, e apaga a temporária.
        /// Retorna null, ou o erro.
        /// </summary>
        private static string MoverParaDestino(string pastaTemp, string pastaSaida)
        {
            try
            {
                Directory.CreateDirectory(pastaSaida);
                foreach (string origem in Directory.GetFiles(pastaTemp))
                {
                    string alvo = Path.Combine(pastaSaida, Path.GetFileName(origem));
                    if (File.Exists(alvo)) File.Delete(alvo);
                    File.Move(origem, alvo);
                }
                ApagarTemp(pastaTemp);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        private static void ApagarTemp(string pastaTemp)
        {
            try { if (Directory.Exists(pastaTemp)) Directory.Delete(pastaTemp, true); } catch { /* fica no temp do Windows */ }
        }

        private static string Duracao(TimeSpan t) =>
            t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min {t.Seconds:00} s" : $"{Math.Max(1, (int)Math.Round(t.TotalSeconds))} s";

        /// <summary>
        /// A barra no Revit, em metros: comprimento da curva de locação (vigas, contraventamentos, pilares inclinados) ou, no
        /// pilar vertical, do nível/deslocamento da base ao do topo; e o trecho do corpo de verdade ao longo dela, a partir do
        /// meio. Null se não for barra.
        /// </summary>
        private static Ifc.PerfilRevit PerfilRevit(Document doc, long id, View3D vista)
        {
            try
            {
                Element e = doc.GetElement(Compat.NovoId(id));
                if (e == null) return null;
                XYZ meio, dir;
                double comp;
                if (e.Location is LocationCurve lc && lc.Curve != null)
                {
                    if (!(lc.Curve is Line ln)) return new Ifc.PerfilRevit { Comprimento = lc.Curve.Length * 0.3048 };
                    XYZ a = ln.GetEndPoint(0), b = ln.GetEndPoint(1);
                    comp = ln.Length;
                    meio = (a + b) / 2;
                    dir = (b - a).Normalize();
                }
                else if (e.Location is LocationPoint lp)
                {
                    double? Cota(BuiltInParameter nivel, BuiltInParameter desloc)
                    {
                        ElementId nid = e.get_Parameter(nivel)?.AsElementId();
                        if (nid == null || nid == ElementId.InvalidElementId || !(doc.GetElement(nid) is Level l)) return null;
                        return l.Elevation + (e.get_Parameter(desloc)?.AsDouble() ?? 0);
                    }
                    double? cb = Cota(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM, BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
                    double? ct = Cota(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM, BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);
                    if (cb == null || ct == null || ct <= cb) return null;
                    comp = ct.Value - cb.Value;
                    meio = new XYZ(lp.Point.X, lp.Point.Y, (cb.Value + ct.Value) / 2);
                    dir = XYZ.BasisZ;
                }
                else return null;
                var barra = new Ifc.PerfilRevit { Comprimento = comp * 0.3048, Dir = new[] { dir.X, dir.Y, dir.Z } };
                double min = double.MaxValue, max = double.MinValue;
                void Pontos(GeometryElement ge, int nivel)
                {
                    if (ge == null || nivel > 3) return;
                    foreach (GeometryObject g in ge)
                    {
                        if (g is Solid so && so.Volume > 1e-9)
                        {
                            foreach (Edge ed in so.Edges)
                                foreach (XYZ p in ed.Tessellate())
                                {
                                    double t = (p - meio).DotProduct(dir);
                                    if (t < min) min = t;
                                    if (t > max) max = t;
                                }
                        }
                        else if (g is GeometryInstance gi) Pontos(gi.GetInstanceGeometry(), nivel + 1);
                    }
                }
                Pontos(AcoIfc.Geometria(e, vista), 0);
                if (max > min) { barra.Ini = min * 0.3048; barra.Fim = max * 0.3048; }
                return barra;
            }
            catch { return null; }
        }

        /// <summary>Caixa do elemento no Revit, em metros, nas coordenadas internas, e a direção da locação se for uma linha.</summary>
        private static Ifc.ConferenciaGeometriaIfc.CaixaRevit CaixaRevit(Document doc, long id)
        {
            try
            {
                Element e = doc.GetElement(Compat.NovoId(id));
                BoundingBoxXYZ bb = e?.get_BoundingBox(null);
                if (bb == null || !bb.Enabled) return null;
                XYZ a = bb.Transform.OfPoint(bb.Min), b = bb.Transform.OfPoint(bb.Max);
                var c = new Ifc.ConferenciaGeometriaIfc.CaixaRevit
                {
                    Min = new[] { Math.Min(a.X, b.X) * 0.3048, Math.Min(a.Y, b.Y) * 0.3048, Math.Min(a.Z, b.Z) * 0.3048 },
                    Max = new[] { Math.Max(a.X, b.X) * 0.3048, Math.Max(a.Y, b.Y) * 0.3048, Math.Max(a.Z, b.Z) * 0.3048 },
                };
                if (e.Location is LocationCurve lc && lc.Curve is Line ln && ln.Length > 0.5)
                {
                    XYZ d = (ln.GetEndPoint(1) - ln.GetEndPoint(0)).Normalize();
                    c.Dir = new[] { d.X, d.Y, d.Z };
                }
                return c;
            }
            catch { return null; }
        }

        /// <summary>Status de fase do elemento na fase exportada, como no IFC (PEnum_ElementStatus), ou null.</summary>
        private static string StatusNaFase(Document doc, long id, ElementId fase)
        {
            if (fase == null || fase == ElementId.InvalidElementId) return null;
            try
            {
                Element e = doc.GetElement(Compat.NovoId(id));
                if (e == null) return null;
                switch (e.GetPhaseStatus(fase))
                {
                    case ElementOnPhaseStatus.New: return "NEW";
                    case ElementOnPhaseStatus.Existing: return "EXISTING";
                    case ElementOnPhaseStatus.Demolished: return "DEMOLISH";
                    case ElementOnPhaseStatus.Temporary: return "TEMPORARY";
                    default: return null;
                }
            }
            catch { return null; }   // elemento sem fase
        }
    }
}
