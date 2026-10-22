using System;
using System.Collections.Generic;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MetroSP.Ids;

namespace MetroSP.Commands
{
    /// <summary>
    /// Verifica se os parâmetros do modelo e o mapeamento de exportação IFC atendem a um IDS,
    /// sem exportar o IFC.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class VerificarIdsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            Document doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null || doc.IsFamilyDocument)
            {
                TaskDialog.Show("MetroSP", "Abra um projeto (.rvt) para verificar o mapeamento IDS.");
                return Result.Cancelled;
            }

            IdsSettings settings = IdsSettings.Load();
            var entrada = new VerificarIdsWindow(settings);
            new WindowInteropHelper(entrada).Owner = uiapp.MainWindowHandle;
            if (entrada.ShowDialog() != true) return Result.Cancelled;
            settings.Save();

            IdsDocument ids;
            var mapping = new ExportMapping();
            try
            {
                ids = IdsReader.Load(settings.IdsFile);
                if (!string.IsNullOrEmpty(settings.PropertySetsFile)) mapping.LoadUserDefinedPropertySets(settings.PropertySetsFile);
                if (!string.IsNullOrEmpty(settings.CategoryMappingFile)) mapping.LoadCategoryMapping(settings.CategoryMappingFile);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("MetroSP", "Erro ao ler os arquivos:\n" + ex.Message);
                return Result.Failed;
            }

            List<CheckResult> resultados = new MappingAnalyzer(doc, mapping, settings.CheckValues).Run(ids);
            foreach (string aviso in mapping.Warnings)
                resultados.Add(new CheckResult { Especificacao = "(arquivo de mapeamento)", Status = CheckStatus.Aviso, Detalhe = aviso });

            var janela = new ResultadosIdsWindow(ids, resultados);
            new WindowInteropHelper(janela).Owner = uiapp.MainWindowHandle;
            janela.Show();
            return Result.Succeeded;
        }
    }
}
