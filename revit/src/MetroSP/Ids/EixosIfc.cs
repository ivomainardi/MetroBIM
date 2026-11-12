using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace MetroSP.Ids
{
    /// <summary>
    /// "Sempre exportar os eixos": deixa os Grids visíveis na vista que filtra a exportação e exportáveis na tabela de
    /// categorias, só durante a exportação. Chamar dentro da transação da exportação; <see cref="Restaurar"/> devolve a vista
    /// como estava.
    /// </summary>
    internal sealed class EixosIfc
    {
        private View _vista;
        private bool _categoriaOculta;
        private List<ElementId> _ocultos = new List<ElementId>();

        /// <summary>Eixos (Grids) no modelo.</summary>
        public int Total { get; private set; }
        /// <summary>O que foi preciso mudar (para o resumo), ou null.</summary>
        public string Aviso { get; private set; }

        public static EixosIfc Preparar(Document doc, ElementId vistaId)
        {
            var r = new EixosIfc();
            List<ElementId> eixos = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Grids)
                .WhereElementIsNotElementType().ToElementIds().ToList();
            r.Total = eixos.Count;
            if (eixos.Count == 0) return r;

            var feito = new List<string>();
            try { if (CategoriasIfc.GarantirEixos(doc)) feito.Add("a categoria Grids foi exportada como IfcGrid"); }
            catch (Exception ex) { feito.Add("não deu para ligar a categoria Grids na tabela (" + ex.Message + ")"); }

            if (vistaId != null && vistaId != ElementId.InvalidElementId && doc.GetElement(vistaId) is View v)
            {
                r._vista = v;
                try
                {
                    Category cat = Category.GetCategory(doc, BuiltInCategory.OST_Grids);
                    if (cat != null && v.CanCategoryBeHidden(cat.Id) && v.GetCategoryHidden(cat.Id))
                    {
                        v.SetCategoryHidden(cat.Id, false);
                        r._categoriaOculta = true;
                        feito.Add($"os eixos foram mostrados na vista \"{v.Name}\"");
                    }
                }
                catch (Exception ex) { feito.Add($"a vista \"{v.Name}\" não deixou mostrar a categoria Grids ({ex.Message})"); }
                try
                {
                    r._ocultos = eixos.Where(id => doc.GetElement(id)?.IsHidden(v) == true).ToList();
                    if (r._ocultos.Count > 0)
                    {
                        v.UnhideElements(r._ocultos);
                        feito.Add($"{r._ocultos.Count} eixo(s) ocultos na vista foram mostrados");
                    }
                }
                catch (Exception ex) { r._ocultos.Clear(); feito.Add("não deu para mostrar os eixos ocultos na vista (" + ex.Message + ")"); }
            }
            if (feito.Count > 0) r.Aviso = "Eixos: " + string.Join("; ", feito) + " (só nesta exportação).";
            return r;
        }

        /// <summary>Devolve a vista como estava (ainda dentro da transação).</summary>
        public void Restaurar()
        {
            if (_vista == null) return;
            try { if (_ocultos.Count > 0) _vista.HideElements(_ocultos); } catch { /* fica visível */ }
            try
            {
                if (_categoriaOculta)
                    _vista.SetCategoryHidden(Category.GetCategory(_vista.Document, BuiltInCategory.OST_Grids).Id, true);
            }
            catch { /* fica visível */ }
        }

        /// <summary>Quantas entidades IfcGrid o arquivo tem.</summary>
        public static int ContarNoIfc(string arquivo)
        {
            try { return File.ReadLines(arquivo).Count(l => l.IndexOf("=IFCGRID(", StringComparison.OrdinalIgnoreCase) >= 0); }
            catch { return -1; }
        }
    }
}
