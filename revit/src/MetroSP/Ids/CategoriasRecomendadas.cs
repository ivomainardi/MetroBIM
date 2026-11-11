using System;
using System.Collections.Generic;
using DB = Autodesk.Revit.DB;

namespace MetroSP.Ids
{
    /// <summary>
    /// Tabela recomendada pelo MetroSP: classe IFC de cada categoria de modelo do Revit, só com classes e tipos que existem
    /// no IFC4 ADD2 TC1 (ISO 16739-1:2018) e no IFC 4.3 ADD2 (ISO 16739-1:2024). Usa a classe específica sempre que o
    /// esquema tem uma; IfcBuildingElementProxy só onde não há classe adequada (Modelo genérico, Equipamento especializado...).
    /// A categoria vem pelo BuiltInCategory (o nome sai no idioma do Revit); categorias que a versão do Revit não tem são puladas.
    /// Uma família pode ir para outra classe pelo "Exportar para IFC como" do tipo, que vale antes da tabela.
    /// </summary>
    public static class CategoriasRecomendadas
    {
        /// <summary>BuiltInCategory, classe IFC4 ("Classe" ou "Classe.TIPO") e classe IFC 4.3 (null = a mesma do IFC4).</summary>
        private static readonly (string Bic, string Ifc4, string Ifc43)[] Tabela =
        {
            // Arquitetura
            ("OST_Walls", "IfcWall", null),
            ("OST_Floors", "IfcSlab.FLOOR", null),
            ("OST_Roofs", "IfcRoof", null),
            ("OST_Ceilings", "IfcCovering.CEILING", null),
            ("OST_Doors", "IfcDoor", null),
            ("OST_Windows", "IfcWindow", null),
            ("OST_Columns", "IfcColumn", null),
            ("OST_Stairs", "IfcStair", null),
            ("OST_StairsRuns", "IfcStairFlight", null),
            ("OST_StairsLandings", "IfcSlab.LANDING", null),
            ("OST_StairsSupports", "IfcMember.STRINGER", null),
            ("OST_StairsRailing", "IfcRailing", null),
            ("OST_Railings", "IfcRailing", null),
            ("OST_RailingTopRail", "IfcRailing.HANDRAIL", null),
            ("OST_RailingHandRail", "IfcRailing.HANDRAIL", null),
            ("OST_RailingSupport", "IfcMember.POST", null),
            ("OST_Ramps", "IfcRamp", null),
            ("OST_CurtaSystem", "IfcCurtainWall", null),
            ("OST_CurtainWallPanels", "IfcPlate.CURTAIN_PANEL", null),
            ("OST_CurtainWallMullions", "IfcMember.MULLION", null),
            ("OST_Cornices", "IfcCovering.MOLDING", null),
            ("OST_Fascia", "IfcCovering.CLADDING", null),
            ("OST_RoofSoffit", "IfcCovering.CEILING", null),
            ("OST_Gutter", "IfcPipeSegment.GUTTER", null),
            ("OST_EdgeSlab", "IfcBeam", "IfcBeam.EDGEBEAM"),
            ("OST_Furniture", "IfcFurniture", null),
            ("OST_FurnitureSystems", "IfcSystemFurnitureElement", null),
            ("OST_Casework", "IfcFurniture", null),
            ("OST_Rooms", "IfcSpace", null),
            ("OST_MEPSpaces", "IfcSpace", null),
            ("OST_GenericModel", "IfcBuildingElementProxy", null),
            ("OST_SpecialityEquipment", "IfcBuildingElementProxy", null),
            ("OST_Parts", "IfcBuildingElementPart", null),
            ("OST_Assemblies", "IfcElementAssembly", null),
            ("OST_Signage", "IfcBuildingElementProxy", "IfcSign"),
            ("OST_VerticalCirculation", "IfcTransportElement", null),
            ("OST_Hardscape", "IfcSlab", "IfcPavement"),
            ("OST_TemporaryStructure", "IfcBuildingElementProxy", null),

            // Terreno
            ("OST_Topography", "IfcSite", null),
            ("OST_Toposolid", "IfcGeographicElement.TERRAIN", null),
            ("OST_Planting", "IfcGeographicElement", "IfcGeographicElement.VEGETATION"),
            ("OST_Site", "IfcBuildingElementProxy", null),

            // Estrutura
            ("OST_StructuralColumns", "IfcColumn", null),
            ("OST_StructuralFraming", "IfcBeam", null),
            ("OST_StructuralFoundation", "IfcFooting", null),
            ("OST_StructuralTruss", "IfcElementAssembly.TRUSS", null),
            ("OST_StructuralFramingSystem", "IfcElementAssembly.BEAM_GRID", null),
            ("OST_StructConnections", "IfcElementAssembly.ACCESSORY_ASSEMBLY", null),
            ("OST_StructConnectionPlates", "IfcPlate", null),
            ("OST_StructConnectionBolts", "IfcMechanicalFastener.BOLT", null),
            ("OST_StructConnectionAnchors", "IfcMechanicalFastener.ANCHORBOLT", null),
            ("OST_StructConnectionShearStuds", "IfcMechanicalFastener.SHEARCONNECTOR", null),
            ("OST_StructConnectionWelds", "IfcFastener", null),
            ("OST_StructuralStiffener", "IfcPlate", "IfcPlate.STIFFENER_PLATE"),
            ("OST_StructuralTendons", "IfcTendon", null),
            ("OST_Rebar", "IfcReinforcingBar", null),
            ("OST_AreaRein", "IfcReinforcingBar", null),
            ("OST_PathRein", "IfcReinforcingBar", null),
            ("OST_FabricReinforcement", "IfcReinforcingMesh", null),
            ("OST_FabricAreas", "IfcGroup", null),
            ("OST_Coupler", "IfcMechanicalFastener", "IfcMechanicalFastener.COUPLER"),

            // Mecânica (ar)
            ("OST_DuctCurves", "IfcDuctSegment.RIGIDSEGMENT", null),
            ("OST_FlexDuctCurves", "IfcDuctSegment.FLEXIBLESEGMENT", null),
            ("OST_DuctFitting", "IfcDuctFitting", null),
            ("OST_DuctAccessory", "IfcDamper", null),
            ("OST_DuctTerminal", "IfcAirTerminal", null),
            ("OST_DuctInsulations", "IfcCovering.INSULATION", null),
            ("OST_DuctLinings", "IfcCovering.INSULATION", null),
            ("OST_MechanicalEquipment", "IfcUnitaryEquipment", null),
            ("OST_HVAC_Zones", "IfcZone", null),

            // Hidráulica
            ("OST_PipeCurves", "IfcPipeSegment.RIGIDSEGMENT", null),
            ("OST_FlexPipeCurves", "IfcPipeSegment.FLEXIBLESEGMENT", null),
            ("OST_PipeFitting", "IfcPipeFitting", null),
            ("OST_PipeAccessory", "IfcValve", null),
            ("OST_PipeInsulations", "IfcCovering.INSULATION", null),
            ("OST_PlumbingFixtures", "IfcSanitaryTerminal", null),
            ("OST_PlumbingEquipment", "IfcPump", null),
            ("OST_Sprinklers", "IfcFireSuppressionTerminal.SPRINKLER", null),
            ("OST_FireProtection", "IfcFireSuppressionTerminal", null),

            // Elétrica e sistemas
            ("OST_ElectricalEquipment", "IfcElectricDistributionBoard", null),
            ("OST_ElectricalFixtures", "IfcOutlet", null),
            ("OST_LightingFixtures", "IfcLightFixture", null),
            ("OST_LightingDevices", "IfcSwitchingDevice", null),
            ("OST_CableTray", "IfcCableCarrierSegment.CABLETRAYSEGMENT", null),
            ("OST_CableTrayFitting", "IfcCableCarrierFitting", null),
            ("OST_Conduit", "IfcCableCarrierSegment.CONDUITSEGMENT", null),
            ("OST_ConduitFitting", "IfcCableCarrierFitting", null),
            ("OST_CommunicationDevices", "IfcCommunicationsAppliance", null),
            ("OST_DataDevices", "IfcCommunicationsAppliance", null),
            ("OST_TelephoneDevices", "IfcAudioVisualAppliance.TELEPHONE", null),
            ("OST_NurseCallDevices", "IfcCommunicationsAppliance", null),
            ("OST_FireAlarmDevices", "IfcAlarm", null),
            ("OST_SecurityDevices", "IfcSensor", null),
            ("OST_AudioVisualDevices", "IfcAudioVisualAppliance", null),
            ("OST_MedicalEquipment", "IfcMedicalDevice", null),
            ("OST_FoodServiceEquipment", "IfcElectricAppliance", null),
        };

        /// <summary>Uma linha da tabela recomendada, já com o nome da categoria no idioma do Revit.</summary>
        public class Item
        {
            public string Categoria;
            public string Classe;
            public string Tipo;
        }

        /// <summary>Classe e tipo recomendados do IFC schema, por nome de categoria (sem diferenciar maiúsculas).</summary>
        public static Dictionary<string, Item> Para(string esquema)
        {
            var r = new Dictionary<string, Item>(StringComparer.CurrentCultureIgnoreCase);
            foreach ((string bic, string ifc4, string ifc43) in Tabela)
            {
                if (!Enum.TryParse(bic, out DB.BuiltInCategory cat)) continue;   // categoria que esta versão do Revit não tem
                string nome;
                try { nome = DB.LabelUtils.GetLabelFor(cat); } catch { continue; }
                if (string.IsNullOrWhiteSpace(nome) || r.ContainsKey(nome)) continue;
                string valor = esquema == CategoriasIfc.Ifc4x3 ? ifc43 ?? ifc4 : ifc4;
                int ponto = valor.IndexOf('.');
                r[nome] = new Item
                {
                    Categoria = nome,
                    Classe = ponto < 0 ? valor : valor.Substring(0, ponto),
                    Tipo = ponto < 0 ? "" : valor.Substring(ponto + 1),
                };
            }
            return r;
        }

        /// <summary>Para o teste: cada valor da tabela (BuiltInCategory, esquema, classe, tipo).</summary>
        internal static IEnumerable<(string Bic, string Esquema, string Classe, string Tipo)> Valores()
        {
            foreach ((string bic, string ifc4, string ifc43) in Tabela)
                foreach ((string e, string v) in new[] { (CategoriasIfc.Ifc4, ifc4), (CategoriasIfc.Ifc4x3, ifc43 ?? ifc4) })
                {
                    int p = v.IndexOf('.');
                    yield return (bic, e, p < 0 ? v : v.Substring(0, p), p < 0 ? "" : v.Substring(p + 1));
                }
        }
    }
}
