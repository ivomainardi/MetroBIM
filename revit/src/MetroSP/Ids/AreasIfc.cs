using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace MetroSP.Ids
{
    /// <summary>
    /// Áreas do Revit (categoria Areas, dos planos de área) nunca vão para o IFC do Metrô: o exportador as levaria como
    /// IfcSpace, duplicando os ambientes. Os ambientes (Rooms) e os espaços (Spaces) seguem normais.
    /// Na exportação a categoria fica desligada na tabela de categorias e cada área recebe "Exportar para IFC = Não",
    /// só durante a exportação. Chamar dentro da transação da exportação; <see cref="Restaurar"/> devolve as áreas como estavam.
    /// </summary>
    internal sealed class AreasIfc
    {
        private readonly List<(Element Area, Parameter Par, int? Inteiro, string Texto)> _antes = new List<(Element, Parameter, int?, string)>();

        /// <summary>Áreas no modelo.</summary>
        public int Total { get; private set; }
        /// <summary>Linha para o resumo da exportação, ou null.</summary>
        public string Aviso { get; private set; }

        public static AreasIfc Preparar(Document doc)
        {
            var r = new AreasIfc();
            List<Element> areas = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Areas)
                .WhereElementIsNotElementType().ToElements().ToList();
            r.Total = areas.Count;
            try { CategoriasIfc.DesligarAreas(doc); } catch { /* sem tabela ativa: vale o parâmetro de cada área */ }
            if (areas.Count == 0) return r;

            int marcadas = 0;
            foreach (Element a in areas)
            {
                try
                {
#if REVIT_ANTES_2023
                    // Revit 2022: não existe "Exportar para IFC"; vale o IfcExportAs = DontExport.
                    Parameter p = a.LookupParameter("IfcExportAs");
                    if (p == null || p.IsReadOnly || p.StorageType != StorageType.String) continue;
                    r._antes.Add((a, p, null, p.AsString()));
                    p.Set("DontExport");
#else
                    Parameter p = a.get_Parameter(BuiltInParameter.IFC_EXPORT_ELEMENT);
                    if (p == null || p.IsReadOnly || p.StorageType != StorageType.Integer) continue;
                    if (p.HasValue && p.AsInteger() == 2) { marcadas++; continue; }
                    r._antes.Add((a, p, p.HasValue ? p.AsInteger() : (int?)null, null));
                    p.Set(2);   // 2 = Não
#endif
                    marcadas++;
                }
                catch { /* fica só com a tabela de categorias */ }
            }
            r.Aviso = $"Áreas: {areas.Count} área(s) do Revit ficaram fora do IFC (não saem como IfcSpace)" +
                (marcadas < areas.Count ? $"; {areas.Count - marcadas} sem o parâmetro \"Exportar para IFC\", valeu a tabela de categorias" : "") + ".";
            return r;
        }

        /// <summary>Devolve o parâmetro de cada área como estava (ainda dentro da transação).</summary>
        public void Restaurar()
        {
            foreach (var (_, par, inteiro, texto) in _antes)
            {
                try
                {
                    if (par.StorageType == StorageType.String) par.Set(texto ?? string.Empty);
                    else par.Set(inteiro ?? 0);
                }
                catch { /* fica fora do IFC */ }
            }
            _antes.Clear();
        }
    }
}
