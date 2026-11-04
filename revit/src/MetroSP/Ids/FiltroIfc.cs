using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace MetroSP.Ids
{
    /// <summary>
    /// Separa os elementos que de fato vão para o IFC. Sem este filtro a verificação dos padrões Metrô contava
    /// "fantasmas": elementos de opção de projeto secundária, demolidos ou de outra fase, sem geometria 3D
    /// ou marcados com "Exportar para IFC = Não". Eles aparecem no Revit só como contorno ou meio-tom e não saem no IFC.
    /// </summary>
    public static class FiltroIfc
    {
        /// <summary>Categorias de modelo que o exportador IFC não leva como elemento.</summary>
        public static readonly HashSet<BuiltInCategory> NaoExportadas = new HashSet<BuiltInCategory>
        {
            BuiltInCategory.OST_Cameras, BuiltInCategory.OST_Lines, BuiltInCategory.OST_SketchLines,
            BuiltInCategory.OST_RvtLinks, BuiltInCategory.OST_IOSModelGroups, BuiltInCategory.OST_Mass,
            BuiltInCategory.OST_PointClouds, BuiltInCategory.OST_CLines, BuiltInCategory.OST_Levels, BuiltInCategory.OST_Grids,
            BuiltInCategory.OST_SectionBox, BuiltInCategory.OST_Viewers, BuiltInCategory.OST_ReferenceLines,
            BuiltInCategory.OST_RoomSeparationLines, BuiltInCategory.OST_AreaSchemeLines, BuiltInCategory.OST_MEPSpaceSeparationLines,
            BuiltInCategory.OST_StructuralFramingSystem,
        };

        /// <summary>
        /// Fase usada na exportação: a do setup, se existir no arquivo; a da vista, se a exportação for por vista;
        /// senão a última fase do projeto (padrão do exportador).
        /// </summary>
        public static ElementId Fase(Document doc, long faseSetup = -1, ElementId vista = null)
        {
            if (vista != null && vista != ElementId.InvalidElementId && doc.GetElement(vista) is View v)
            {
                Parameter p = v.get_Parameter(BuiltInParameter.VIEW_PHASE);
                if (p != null && p.AsElementId() != ElementId.InvalidElementId) return p.AsElementId();
            }
            if (faseSetup > 0 && doc.GetElement(new ElementId(faseSetup)) is Phase) return new ElementId(faseSetup);
            return doc.Phases.Size > 0 ? doc.Phases.get_Item(doc.Phases.Size - 1).Id : ElementId.InvalidElementId;
        }

        /// <summary>Por que o elemento não vai para o IFC, ou null se vai.</summary>
        public static string MotivoFora(Document doc, Element e, ElementId fase)
        {
            if (e == null || e is ElementType) return "Tipo";
            if (e.Category == null || e.Category.CategoryType != CategoryType.Model) return "Não é elemento de modelo";
            if (e.ViewSpecific || e is View || e is CurveElement || e is DatumPlane || e is SketchPlane || e is Group
                || e is AssemblyInstance || e is RevitLinkInstance || e is ImportInstance)
                return "Não é elemento de modelo";
            if (NaoExportadas.Contains(e.Category.BuiltInCategory)) return "Categoria que não vai para o IFC";

            if (e.DesignOption != null && !e.DesignOption.IsPrimary) return "Opção de projeto secundária";

            if (fase != null && fase != ElementId.InvalidElementId)
            {
                try
                {
                    switch (e.GetPhaseStatus(fase))
                    {
                        case ElementOnPhaseStatus.Demolished:
                        case ElementOnPhaseStatus.Past: return "Demolido";
                        case ElementOnPhaseStatus.Future: return "Criado numa fase posterior";
                    }
                }
                catch { /* elemento sem fase */ }
            }

            if (ExportarParaIfcNao(doc, e)) return "Exportar para IFC = Não";
            if (e.get_BoundingBox(null) == null) return "Sem geometria 3D";
            return null;
        }

        public static bool VaiParaIfc(Document doc, Element e, ElementId fase) => MotivoFora(doc, e, fase) == null;

        /// <summary>Parâmetro "Exportar para IFC": 0 = pelo tipo, 1 = sim, 2 = não.</summary>
        private static bool ExportarParaIfcNao(Document doc, Element e)
        {
            int? inst = Inteiro(e, "IFC_EXPORT_ELEMENT");
            if (inst == 2) return true;
            if (inst == 1) return false;
            return Inteiro(doc.GetElement(e.GetTypeId()), "IFC_EXPORT_ELEMENT_TYPE") == 2;
        }

        private static int? Inteiro(Element e, string bipNome)
        {
            if (e == null || !Enum.TryParse(bipNome, out BuiltInParameter bip)) return null;
            Parameter p = e.get_Parameter(bip);
            return p != null && p.StorageType == StorageType.Integer && p.HasValue ? p.AsInteger() : (int?)null;
        }

        /// <summary>Resumo dos elementos deixados de fora: "12 demolidos, 3 sem geometria 3D...".</summary>
        public static string Resumo(Dictionary<string, List<ElementId>> fora) =>
            string.Join(", ", fora.OrderByDescending(k => k.Value.Count).Select(k => $"{k.Value.Count} {k.Key.ToLowerInvariant()}"));
    }
}
