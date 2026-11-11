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
            if (faseSetup > 0 && doc.GetElement(Compat.NovoId(faseSetup)) is Phase) return Compat.NovoId(faseSetup);
            return doc.Phases.Size > 0 ? doc.Phases.get_Item(doc.Phases.Size - 1).Id : ElementId.InvalidElementId;
        }

        /// <summary>Por que o elemento não vai para o IFC, ou null se vai.</summary>
        /// <param name="tiposComSolido">
        /// Opcional: tipos de família já vistos com sólido. Instâncias do mesmo tipo têm a mesma geometria,
        /// então o teste de geometria (a parte lenta) é feito uma vez por tipo, e não por instância.
        /// </param>
        public static string MotivoFora(Document doc, Element e, ElementId fase, HashSet<ElementId> tiposComSolido = null)
        {
            if (e == null || e is ElementType) return "Tipo";
            if (e.Category == null || e.Category.CategoryType != CategoryType.Model) return "Não é elemento de modelo";
            if (e.ViewSpecific || e is View || e is CurveElement || e is DatumPlane || e is SketchPlane || e is Group
                || e is AssemblyInstance || e is RevitLinkInstance || e is ImportInstance)
                return "Não é elemento de modelo";
            if (NaoExportadas.Contains(e.Category.BuiltInCategory)) return "Categoria que não vai para o IFC";
            // Origem interna, ponto base e ponto de levantamento só posicionam o modelo (vão para o IfcSite, não como elemento).
            if (e is BasePoint || e is InternalOrigin) return "Ponto de referência";
            // Aberturas (shaft, por face, corte de abertura estrutural) saem como vazio dentro do elemento que cortam.
            if (e is Opening) return "Abertura (sai como vazio no elemento)";

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
            // Famílias (inclusive as no local) e formas diretas podem ter só linhas, planos ou vazios: aparecem como
            // contorno no Revit, mas o exportador não leva nada. Paredes, pisos e outros elementos de sistema têm sólido.
            if ((e is FamilyInstance || e is DirectShape) && !TemSolido(e, tiposComSolido)) return "Sem geometria 3D";
            return null;
        }

        /// <summary>O elemento tem algum sólido com volume ou malha (no nível de detalhe Alto).</summary>
        public static bool TemSolido(Element e) => TemSolido(e, null);

        private static bool TemSolido(Element e, HashSet<ElementId> tiposComSolido)
        {
            ElementId tipo = tiposComSolido != null && e is FamilyInstance ? e.GetTypeId() : null;
            if (tipo != null && tiposComSolido.Contains(tipo)) return true;
            bool tem = TemSolidoGeometria(e);
            if (tem && tipo != null) tiposComSolido.Add(tipo);
            return tem;
        }

        private static bool TemSolidoGeometria(Element e)
        {
            try
            {
                GeometryElement g = e.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false });
                return g != null && TemSolido(g, 0);
            }
            catch { return true; } // na dúvida, conta o elemento
        }

        private static bool TemSolido(GeometryElement g, int nivel)
        {
            foreach (GeometryObject o in g)
            {
                switch (o)
                {
                    case Solid s when s.Volume > 1e-9: return true;
                    case Mesh m when m.NumTriangles > 0: return true;
                    case GeometryInstance gi when nivel < 6:
                        // Geometria do tipo, sem transformar: bem mais rápida que GetInstanceGeometry e o volume é o mesmo.
                        GeometryElement dentro = gi.GetSymbolGeometry();
                        if (dentro != null && TemSolido(dentro, nivel + 1)) return true;
                        break;
                }
            }
            return false;
        }

        public static bool VaiParaIfc(Document doc, Element e, ElementId fase, HashSet<ElementId> tiposComSolido = null) =>
            MotivoFora(doc, e, fase, tiposComSolido) == null;

        /// <summary>Parâmetro "Exportar para IFC": 0 = pelo tipo, 1 = sim, 2 = não.</summary>
        private static bool ExportarParaIfcNao(Document doc, Element e)
        {
#if REVIT_ANTES_2023
            // Revit 2022: não existe o parâmetro "Exportar para IFC"; vale o IfcExportAs = DontExport.
            string como = Compat.ExportarComo(e, false)?.AsString() ?? Compat.ExportarComo(doc.GetElement(e.GetTypeId()), true)?.AsString();
            if (como != null && como.Trim().StartsWith("DontExport", StringComparison.OrdinalIgnoreCase)) return true;
#endif
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
