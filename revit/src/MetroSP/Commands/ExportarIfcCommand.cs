using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

            // Pasta do Forma online: baixa o que mudou lá desde a última vez (sem rede, vale a última cópia).
            string resultadoForma = null;
            if (FormaOnline.Conectado && FormaOnline.PastaEscolhida() != null)
                using (var p = Ui.Progresso.Abrir("Forma online"))
                {
                    p.Etapa("Conferindo atualizações das configurações de IFC no Forma");
                    resultadoForma = FormaOnline.Sincronizar().ToString();
                }

            var janela = new ExportarIfcWindow(doc, settings, resultadoForma);
            new WindowInteropHelper(janela).Owner = uiapp.MainWindowHandle;
            bool ok = janela.ShowDialog() == true;
            settings.Save();
            // Cancelado ou Falhou faz o Revit desfazer tudo do comando, inclusive o Location gravado pela janela.
            Result Fim(Result r) => janela.AlteracoesLocalizacao.Count > 0 ? Result.Succeeded : r;
            if (!ok) return Fim(Result.Cancelled);

            IfcExportSetup setup = janela.Setup;
            string arquivo = Path.Combine(janela.PastaSaida, janela.NomeArquivo + ".ifc");

            // A localização é gravada na etapa 1 (Localização IFC); aqui o IFC sai com o que está no Revit.
            LocalizacaoIfc local = janela.Localizacao;
            LocalizacaoIfc.Escolha escolha = local.Atual();
            List<string> alteracoes = janela.AlteracoesLocalizacao;

            bool exportou;
            string avisoPsets = null, avisoCategorias = null, avisoCoordenacao = null;
            string passo = "montar as opções do setup";
            EixosIfc eixos = null;
            try
            {
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
                    passo = "carregar os property sets no modelo";
                    if (!string.IsNullOrEmpty(janela.PropertySetsEscolhido))
                        avisoPsets = PsetsNoModelo.Carregar(doc, janela.PropertySetsEscolhido);

                    // Tabela de categorias → classe IFC do esquema do setup (janela Categorias IFC), se houver.
                    passo = "aplicar a tabela de categorias IFC";
                    avisoCategorias = CategoriasIfc.AplicarNaExportacao(doc, setup.Versao.ToString());

                    // Arquivo de coordenação: classes IFC por família e por elemento do perfil ativo, só nesta exportação.
                    passo = "aplicar as classes por família do arquivo de coordenação";
                    avisoCoordenacao = Coordenacao.CoordenacaoRevit.AplicarNaExportacao(doc);

                    // Sempre exportar os eixos: Grids visíveis na vista filtrada e exportáveis na tabela, só nesta exportação.
                    if (janela.SempreEixos)
                    {
                        passo = "preparar os eixos (Grids)";
                        eixos = EixosIfc.Preparar(doc, janela.Vista);
                    }

                    using (var progresso = Ui.Progresso.Abrir("Exportar IFC"))
                    {
                        progresso.Etapa($"Exportando {janela.NomeArquivo}.ifc com o setup {setup.Nome}");
                        progresso.Item("O exportador do Revit não informa o andamento; em modelos grandes pode levar alguns minutos.");
                        passo = "exportar (exportador do Revit)";
                        exportou = doc.Export(janela.PastaSaida, janela.NomeArquivo + ".ifc", opcoes);
                    }
                    eixos?.Restaurar();

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
                TaskDialog.Show("MetroSP", $"Erro ao exportar o IFC, na etapa \"{passo}\":\n" + ex.Message +
                    (avisoCategorias != null ? "\n\n" + avisoCategorias : ""));
                return Fim(Result.Failed);
            }

            if (!exportou || !File.Exists(arquivo))
            {
                TaskDialog.Show("MetroSP", "O Revit não conseguiu exportar o IFC. Confira o setup e tente pela janela de exportação do Revit para ver a mensagem do exportador.");
                return Fim(Result.Failed);
            }

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
            bool dadosComProblema = false, semElementos = false;
            try
            {
                var modelo = Ifc.StepModel.Load(arquivo);
                bool salvar = false;
                // O que saiu: elementos por classe e, se o IFC saiu só com ambientes, a causa provável (fase ou vista).
                try
                {
                    linhasElementos = ElementosNoIfc.Conferir(doc, ElementosNoIfc.Contar(modelo), janela.Vista,
                        FiltroIfc.Fase(doc, janela.Fase, janela.Vista), out semElementos);
                }
                catch (Exception ex) { linhasElementos.Add("Não foi possível contar os elementos do IFC: " + ex.Message); }
                try { salvar = Ifc.IfcClassificacao.Aplicar(modelo, Ifc.RegraIfc.DosPadroes(), avisosClassificacao.Add) > 0; }
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
            LocalizacaoIfc.Conferencia conferencia = local.ConferirIfc(arquivo, escolha, janela.EpsgEmBranco);
            if (latLong != null) conferencia.Linhas.Insert(0, latLong);
            conferencia.Linhas.InsertRange(0, linhasElementos);
            if (semElementos) conferencia.TemProblema = true;
            conferencia.Linhas.AddRange(linhasDados);
            if (dadosComProblema) conferencia.TemProblema = true;
            foreach (string a in avisosClassificacao) conferencia.Linhas.Add(char.ToUpper(a[0]) + a.Substring(1));
            if (georref != null) conferencia.Linhas.Insert(0, georref);
            string log = local.GravarLog(doc, arquivo, setup.Nome, escolha, alteracoes, conferencia,
                uiapp.Application.VersionName, Ui.SobreWindow.Versao);
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
                    TextoEixos(eixos, arquivo) +
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
        private static string TextoEixos(EixosIfc eixos, string arquivo)
        {
            if (eixos == null || eixos.Total == 0) return "";
            int n = EixosIfc.ContarNoIfc(arquivo);
            string linha = n > 0 ? $"\nEixos: {n} IfcGrid no IFC ({eixos.Total} eixos no modelo)."
                : n == 0 ? $"\nATENÇÃO: nenhum IfcGrid no IFC, embora o modelo tenha {eixos.Total} eixos. Tente exportar o modelo inteiro (sem filtrar pela vista)."
                : "";
            return linha + (eixos.Aviso == null ? "" : "\n" + eixos.Aviso);
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
