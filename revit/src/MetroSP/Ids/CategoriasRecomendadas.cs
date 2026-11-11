using System;
using System.Collections.Generic;
using DB = Autodesk.Revit.DB;

namespace MetroSP.Ids
{
    /// <summary>
    /// Tabela recomendada pelo MetroSP: classe IFC de cada categoria (e subcategoria) de modelo do Revit, com base nas
    /// categorias do Revit 2027, só com classes e tipos que existem no IFC4 ADD2 TC1 (ISO 16739-1:2018) e no IFC 4.3 ADD2
    /// (ISO 16739-1:2024). Usa a classe específica sempre que o esquema tem uma e nunca IfcBuildingElementProxy:
    /// onde a categoria é genérica, vai para IfcCivilElement / IfcBuiltElement ou IfcFurnishingElement.
    /// A categoria vem pelo BuiltInCategory (nome no idioma do Revit; subcategoria resolvida pela categoria-mãe);
    /// as que a versão do Revit não tem são puladas. O "Exportar para IFC como" de uma família vale antes da tabela.
    /// A mesma tabela gera a planilha de revisão (docs/categorias-ifc-recomendadas.xlsx).
    /// </summary>
    public static class CategoriasRecomendadas
    {
        /// <summary>
        /// BuiltInCategory, nome em inglês (só para a planilha), classe IFC4 ("Classe" ou "Classe.TIPO"),
        /// classe IFC 4.3 (null = a mesma do IFC4), exportar e observação.
        /// </summary>
        internal static readonly (string Bic, string Nome, string Ifc4, string Ifc43, bool Exportar, string Nota)[] Tabela =
        {
            // ---------------------------------------------------------------- Arquitetura
            ("OST_Walls", "Walls", "IfcWall", null, true, "O exportador usa IfcWall e o tipo pela função da parede."),
            ("OST_Floors", "Floors", "IfcSlab.FLOOR", null, true, ""),
            ("OST_Roofs", "Roofs", "IfcRoof", null, true, ""),
            ("OST_Ceilings", "Ceilings", "IfcCovering.CEILING", null, true, ""),
            ("OST_Doors", "Doors", "IfcDoor", null, true, ""),
            ("OST_Windows", "Windows", "IfcWindow", null, true, ""),
            ("OST_Columns", "Columns", "IfcColumn", null, true, "Pilar arquitetônico."),
            ("OST_Stairs", "Stairs", "IfcStair", null, true, ""),
            ("OST_StairsRuns", "Runs", "IfcStairFlight", null, true, "Subcategoria de Stairs."),
            ("OST_StairsLandings", "Landings", "IfcSlab.LANDING", null, true, "Subcategoria de Stairs."),
            ("OST_StairsSupports", "Supports", "IfcMember.STRINGER", null, true, "Subcategoria de Stairs."),
            ("OST_MultistoryStairs", "Multistory Stairs", "IfcStair", null, true, ""),
            ("OST_StairsRailing", "Railings (Stairs)", "IfcRailing", null, true, ""),
            ("OST_Railings", "Railings", "IfcRailing", null, true, ""),
            ("OST_RailingTopRail", "Top Rails", "IfcRailing.HANDRAIL", null, true, ""),
            ("OST_RailingHandRail", "Handrails", "IfcRailing.HANDRAIL", null, true, ""),
            ("OST_RailingSupport", "Supports (Railings)", "IfcMember.POST", null, true, ""),
            ("OST_RailingTermination", "Terminations", "IfcMember", null, true, ""),
            ("OST_Ramps", "Ramps", "IfcRamp", null, true, ""),
            ("OST_CurtaSystem", "Curtain Systems", "IfcCurtainWall", null, true, ""),
            ("OST_CurtainWallPanels", "Curtain Panels", "IfcPlate.CURTAIN_PANEL", null, true, ""),
            ("OST_CurtainWallMullions", "Curtain Wall Mullions", "IfcMember.MULLION", null, true, ""),
            ("OST_Cornices", "Wall Sweeps", "IfcCovering.MOLDING", null, true, ""),
            ("OST_Fascia", "Fascias", "IfcCovering.CLADDING", null, true, ""),
            ("OST_RoofSoffit", "Roof Soffits", "IfcCovering.CEILING", null, true, ""),
            ("OST_Gutter", "Gutters", "IfcPipeSegment.GUTTER", null, true, ""),
            ("OST_EdgeSlab", "Slab Edges", "IfcBeam", "IfcBeam.EDGEBEAM", true, "EDGEBEAM só existe no 4.3."),
            ("OST_Furniture", "Furniture", "IfcFurniture", null, true, ""),
            ("OST_FurnitureSystems", "Furniture Systems", "IfcSystemFurnitureElement", null, true, ""),
            ("OST_Casework", "Casework", "IfcFurniture", null, true, ""),
            ("OST_Rooms", "Rooms", "IfcSpace", null, true, ""),
            ("OST_MEPSpaces", "Spaces", "IfcSpace", null, true, ""),
            ("OST_GenericModel", "Generic Models", "IfcCivilElement", "IfcBuiltElement", true, "Categoria genérica: IfcCivilElement no IFC4 e IfcBuiltElement no 4.3. Melhor ainda: defina a classe certa por família."),
            ("OST_SpecialityEquipment", "Specialty Equipment", "IfcFurnishingElement", null, true, "Junta coisas diferentes: defina por família quando houver classe melhor (ex.: bloqueio como IfcDoor TURNSTILE no 4.3)."),
            ("OST_Parts", "Parts", "IfcBuildingElementPart", null, true, ""),
            ("OST_Assemblies", "Assemblies", "IfcElementAssembly", null, true, ""),
            ("OST_Signage", "Signage", "IfcFurnishingElement", "IfcSign", true, "IfcSign só existe no 4.3."),
            ("OST_VerticalCirculation", "Vertical Circulation", "IfcTransportElement", null, true, "Elevadores e escadas rolantes: tipo por família."),
            ("OST_Hardscape", "Hardscape", "IfcSlab", "IfcPavement", true, "IfcPavement só existe no 4.3."),
            ("OST_TemporaryStructure", "Temporary Structures", "IfcCivilElement", null, true, "Não há classe para estrutura provisória: elemento civil."),
            ("OST_Parking", "Parking", "IfcSpace.PARKING", null, true, "Vaga como espaço do tipo PARKING."),
            ("OST_Mass", "Mass", "", null, false, "Estudo de massa: não exportar."),
            ("OST_Entourage", "Entourage", "", null, false, "Entourage: não exportar."),

            // ---------------------------------------------------------------- Terreno
            ("OST_Topography", "Topography", "IfcSite", null, true, "O exportador põe o terreno no IfcSite."),
            ("OST_Toposolid", "Toposolid", "IfcGeographicElement.TERRAIN", null, true, ""),
            ("OST_BuildingPad", "Pads", "IfcSlab.BASESLAB", null, true, ""),
            ("OST_Roads", "Roads", "IfcSlab", "IfcPavement", true, "IfcPavement só existe no 4.3."),
            ("OST_Planting", "Planting", "IfcGeographicElement", "IfcGeographicElement.VEGETATION", true, "VEGETATION só existe no 4.3."),
            ("OST_Site", "Site", "IfcCivilElement", null, true, "Componentes de implantação variados: defina por família quando houver classe melhor."),

            // ---------------------------------------------------------------- Estrutura
            ("OST_StructuralColumns", "Structural Columns", "IfcColumn", null, true, ""),
            ("OST_StructuralFraming", "Structural Framing", "IfcBeam", null, true, "Contraventamentos e terças vêm pelas subcategorias abaixo."),
            ("OST_Girder", "Girder", "IfcBeam", null, true, "Subcategoria de Structural Framing."),
            ("OST_Joist", "Joist", "IfcBeam.JOIST", null, true, "Subcategoria de Structural Framing."),
            ("OST_Purlin", "Purlin", "IfcMember.PURLIN", null, true, "Subcategoria de Structural Framing."),
            ("OST_HorizontalBracing", "Horizontal Bracing", "IfcMember.BRACE", null, true, "Subcategoria de Structural Framing."),
            ("OST_VerticalBracing", "Vertical Bracing", "IfcMember.BRACE", null, true, "Subcategoria de Structural Framing."),
            ("OST_KickerBracing", "Kicker Bracing", "IfcMember.BRACE", null, true, "Subcategoria de Structural Framing."),
            ("OST_StructuralFramingOther", "Other", "IfcBeam", null, true, "Subcategoria de Structural Framing."),
            ("OST_StructuralFoundation", "Structural Foundations", "IfcFooting", null, true, "Radier pode ir por família como IfcSlab.BASESLAB; estaca como IfcPile."),
            ("OST_StructuralTruss", "Structural Trusses", "IfcElementAssembly.TRUSS", null, true, ""),
            ("OST_StructuralFramingSystem", "Structural Beam Systems", "IfcElementAssembly.BEAM_GRID", null, true, ""),
            ("OST_StructConnections", "Structural Connections", "IfcElementAssembly.ACCESSORY_ASSEMBLY", null, true, ""),
            ("OST_StructConnectionPlates", "Plates", "IfcPlate", null, true, "Subcategoria de Structural Connections."),
            ("OST_StructConnectionBolts", "Bolts", "IfcMechanicalFastener.BOLT", null, true, "Subcategoria de Structural Connections."),
            ("OST_StructConnectionAnchors", "Anchors", "IfcMechanicalFastener.ANCHORBOLT", null, true, "Subcategoria de Structural Connections."),
            ("OST_StructConnectionShearStuds", "Shear Studs", "IfcMechanicalFastener.SHEARCONNECTOR", null, true, "Subcategoria de Structural Connections."),
            ("OST_StructConnectionWelds", "Welds", "IfcFastener.WELD", null, true, "Subcategoria de Structural Connections."),
            ("OST_StructConnectionOthers", "Other (Connections)", "IfcDiscreteAccessory", null, true, "Subcategoria de Structural Connections."),
            ("OST_StructuralStiffener", "Structural Stiffeners", "IfcPlate", "IfcPlate.STIFFENER_PLATE", true, "STIFFENER_PLATE só existe no 4.3."),
            ("OST_StructuralTendons", "Structural Tendons", "IfcTendon", null, true, ""),
            ("OST_Rebar", "Structural Rebar", "IfcReinforcingBar", null, true, ""),
            ("OST_AreaRein", "Structural Area Reinforcement", "IfcReinforcingBar", null, true, ""),
            ("OST_PathRein", "Structural Path Reinforcement", "IfcReinforcingBar", null, true, ""),
            ("OST_FabricReinforcement", "Structural Fabric Reinforcement", "IfcReinforcingMesh", null, true, ""),
            ("OST_FabricAreas", "Structural Fabric Areas", "IfcGroup", null, true, "Grupo das telas da área."),
            ("OST_Coupler", "Structural Rebar Couplers", "IfcMechanicalFastener", "IfcMechanicalFastener.COUPLER", true, "COUPLER só existe no 4.3."),
            ("OST_VibrationManagement", "Vibration Management", "IfcVibrationIsolator", null, true, ""),
            ("OST_VibrationIsolators", "Vibration Isolators", "IfcVibrationIsolator", null, true, ""),
            ("OST_VibrationDampers", "Vibration Dampers", "IfcVibrationIsolator", "IfcVibrationDamper", true, "IfcVibrationDamper só existe no 4.3."),

            // ---------------------------------------------------------------- Pontes (IFC4 não tem classes de ponte)
            ("OST_BridgeAbutments", "Abutments", "IfcElementAssembly", "IfcElementAssembly.ABUTMENT", true, "O tipo ABUTMENT só existe no 4.3."),
            ("OST_AbutmentFoundations", "Abutment Foundations", "IfcFooting", null, true, ""),
            ("OST_AbutmentPiles", "Abutment Piles", "IfcPile", null, true, ""),
            ("OST_AbutmentWalls", "Abutment Walls", "IfcWall", null, true, ""),
            ("OST_ApproachSlabs", "Approach Slabs", "IfcSlab", "IfcSlab.APPROACH_SLAB", true, ""),
            ("OST_BridgePiers", "Piers", "IfcElementAssembly", "IfcElementAssembly.PIER", true, "O tipo PIER só existe no 4.3."),
            ("OST_PierCaps", "Pier Caps", "IfcBeam", "IfcBeam.PIERCAP", true, ""),
            ("OST_PierColumns", "Pier Columns", "IfcColumn", "IfcColumn.PIERSTEM", true, ""),
            ("OST_PierPiles", "Pier Piles", "IfcPile", null, true, ""),
            ("OST_PierWalls", "Pier Walls", "IfcWall", null, true, ""),
            ("OST_BridgeTowers", "Towers", "IfcElementAssembly", "IfcElementAssembly.PYLON", true, "O tipo PYLON só existe no 4.3."),
            ("OST_BridgeCables", "Bridge Cables", "IfcMember", "IfcMember.STAY_CABLE", true, ""),
            ("OST_BridgeArches", "Bridge Arches", "IfcElementAssembly.ARCH", null, true, ""),
            ("OST_BridgeDecks", "Bridge Decks", "IfcSlab", "IfcElementAssembly.DECK", true, ""),
            ("OST_BridgeFoundations", "Bridge Foundations", "IfcFooting", null, true, ""),
            ("OST_BridgeGirders", "Bridge Girders", "IfcBeam", "IfcBeam.GIRDER_SEGMENT", true, ""),
            ("OST_BridgeBearings", "Bridge Bearings", "IfcDiscreteAccessory", "IfcBearing", true, "IfcBearing só existe no 4.3."),
            ("OST_BridgeFraming", "Bridge Framing", "IfcBeam", null, true, ""),
            ("OST_BridgeFramingCrossBracing", "Cross Bracing", "IfcMember.BRACE", null, true, ""),
            ("OST_BridgeFramingDiaphragms", "Diaphragms", "IfcBeam", "IfcBeam.DIAPHRAGM", true, ""),
            ("OST_BridgeFramingTrusses", "Trusses (Bridge)", "IfcElementAssembly.TRUSS", null, true, ""),
            ("OST_ExpansionJoints", "Expansion Joints", "IfcDiscreteAccessory", "IfcDiscreteAccessory.EXPANSION_JOINT_DEVICE", true, "O tipo EXPANSION_JOINT_DEVICE só existe no 4.3."),

            // ---------------------------------------------------------------- Mecânica (ar)
            ("OST_DuctCurves", "Ducts", "IfcDuctSegment.RIGIDSEGMENT", null, true, ""),
            ("OST_FlexDuctCurves", "Flex Ducts", "IfcDuctSegment.FLEXIBLESEGMENT", null, true, ""),
            ("OST_DuctFitting", "Duct Fittings", "IfcDuctFitting", null, true, ""),
            ("OST_DuctAccessory", "Duct Accessories", "IfcDamper", null, true, "Maioria são dampers; atenuador por família como IfcDuctSilencer."),
            ("OST_DuctTerminal", "Air Terminals", "IfcAirTerminal", null, true, ""),
            ("OST_DuctInsulations", "Duct Insulations", "IfcCovering.INSULATION", null, true, ""),
            ("OST_DuctLinings", "Duct Linings", "IfcCovering.INSULATION", null, true, ""),
            ("OST_MechanicalEquipment", "Mechanical Equipment", "IfcUnitaryEquipment", null, true, "Junta coisas diferentes: ventilador como IfcFan, chiller como IfcChiller, por família."),
            ("OST_MechanicalControlDevices", "Mechanical Control Devices", "IfcUnitaryControlElement", null, true, ""),
            ("OST_HVAC_Zones", "HVAC Zones", "IfcZone", null, true, "Grupo de espaços."),
            ("OST_FabricationDuctwork", "MEP Fabrication Ductwork", "IfcDuctSegment", null, true, ""),
            ("OST_FabricationDuctworkInsulation", "MEP Fabrication Ductwork Insulation", "IfcCovering.INSULATION", null, true, ""),
            ("OST_FabricationDuctworkLining", "MEP Fabrication Ductwork Lining", "IfcCovering.INSULATION", null, true, ""),
            ("OST_FabricationDuctworkStiffeners", "MEP Fabrication Ductwork Stiffeners", "IfcDiscreteAccessory", null, true, ""),

            // ---------------------------------------------------------------- Hidráulica
            ("OST_PipeCurves", "Pipes", "IfcPipeSegment.RIGIDSEGMENT", null, true, ""),
            ("OST_FlexPipeCurves", "Flex Pipes", "IfcPipeSegment.FLEXIBLESEGMENT", null, true, ""),
            ("OST_PipeFitting", "Pipe Fittings", "IfcPipeFitting", null, true, ""),
            ("OST_PipeAccessory", "Pipe Accessories", "IfcValve", null, true, "Maioria são válvulas; filtro, medidor por família."),
            ("OST_PipeInsulations", "Pipe Insulations", "IfcCovering.INSULATION", null, true, ""),
            ("OST_PlumbingFixtures", "Plumbing Fixtures", "IfcSanitaryTerminal", null, true, ""),
            ("OST_PlumbingEquipment", "Plumbing Equipment", "IfcPump", null, true, "Maioria são bombas; reservatório por família como IfcTank."),
            ("OST_Sprinklers", "Sprinklers", "IfcFireSuppressionTerminal.SPRINKLER", null, true, ""),
            ("OST_FireProtection", "Fire Protection", "IfcFireSuppressionTerminal", null, true, "Hidrante, mangotinho: tipo por família."),
            ("OST_FabricationPipework", "MEP Fabrication Pipework", "IfcPipeSegment", null, true, ""),
            ("OST_FabricationPipeworkInsulation", "MEP Fabrication Pipework Insulation", "IfcCovering.INSULATION", null, true, ""),
            ("OST_FabricationHangers", "MEP Fabrication Hangers", "IfcDiscreteAccessory.BRACKET", null, true, ""),
            ("OST_MEPAncillaryFraming", "MEP Ancillary Framing", "IfcMember", null, true, ""),

            // ---------------------------------------------------------------- Elétrica e sistemas
            ("OST_ElectricalEquipment", "Electrical Equipment", "IfcElectricDistributionBoard", null, true, "Quadros; transformador por família como IfcTransformer."),
            ("OST_ElectricalFixtures", "Electrical Fixtures", "IfcOutlet", null, true, ""),
            ("OST_LightingFixtures", "Lighting Fixtures", "IfcLightFixture", null, true, ""),
            ("OST_LightingDevices", "Lighting Devices", "IfcSwitchingDevice", null, true, ""),
            ("OST_CableTray", "Cable Trays", "IfcCableCarrierSegment.CABLETRAYSEGMENT", null, true, ""),
            ("OST_CableTrayFitting", "Cable Tray Fittings", "IfcCableCarrierFitting", null, true, ""),
            ("OST_Conduit", "Conduits", "IfcCableCarrierSegment.CONDUITSEGMENT", null, true, ""),
            ("OST_ConduitFitting", "Conduit Fittings", "IfcCableCarrierFitting", null, true, ""),
            ("OST_Cable", "Cables", "IfcCableSegment.CABLESEGMENT", null, true, ""),
            ("OST_FabricationContainment", "MEP Fabrication Containment", "IfcCableCarrierSegment", null, true, ""),
            ("OST_CommunicationDevices", "Communication Devices", "IfcCommunicationsAppliance", null, true, ""),
            ("OST_DataDevices", "Data Devices", "IfcCommunicationsAppliance", null, true, ""),
            ("OST_TelephoneDevices", "Telephone Devices", "IfcAudioVisualAppliance.TELEPHONE", null, true, ""),
            ("OST_NurseCallDevices", "Nurse Call Devices", "IfcCommunicationsAppliance", null, true, ""),
            ("OST_FireAlarmDevices", "Fire Alarm Devices", "IfcAlarm", null, true, "Detector por família como IfcSensor."),
            ("OST_SecurityDevices", "Security Devices", "IfcSensor", null, true, "Câmera por família como IfcAudioVisualAppliance.CAMERA."),
            ("OST_AudioVisualDevices", "Audio Visual Devices", "IfcAudioVisualAppliance", null, true, ""),
            ("OST_MedicalEquipment", "Medical Equipment", "IfcMedicalDevice", null, true, ""),
            ("OST_FoodServiceEquipment", "Food Service Equipment", "IfcElectricAppliance", null, true, ""),
        };

        /// <summary>Uma linha da tabela recomendada, já com os nomes no idioma do Revit.</summary>
        public class Item
        {
            public string Categoria;
            public string Subcategoria = "";
            public string Classe;
            public string Tipo;
            public bool Exportar;
            public string Chave => Categoria + "|" + Subcategoria;
        }

        /// <summary>
        /// Classe e tipo recomendados do IFC schema, por "Categoria|Subcategoria". Com o modelo, a subcategoria é resolvida
        /// pela categoria-mãe; sem ele, vale o nome da categoria (sem subcategoria).
        /// </summary>
        public static Dictionary<string, Item> Para(string esquema, DB.Document doc)
        {
            var r = new Dictionary<string, Item>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var t in Tabela)
            {
                if (!Enum.TryParse(t.Bic, out DB.BuiltInCategory bic)) continue;   // categoria que esta versão do Revit não tem
                string categoria, sub = "";
                try
                {
                    DB.Category c = doc != null ? DB.Category.GetCategory(doc, bic) : null;
                    if (c != null)
                    {
                        if (c.Parent != null) { categoria = c.Parent.Name; sub = c.Name; }
                        else categoria = c.Name;
                    }
                    else if (doc != null) continue;  // o modelo não tem esta categoria
                    else categoria = DB.LabelUtils.GetLabelFor(bic);
                }
                catch { continue; }
                if (string.IsNullOrWhiteSpace(categoria)) continue;
                string valor = (esquema == CategoriasIfc.Ifc4x3 ? t.Ifc43 ?? t.Ifc4 : t.Ifc4) ?? "";
                int ponto = valor.IndexOf('.');
                var item = new Item
                {
                    Categoria = categoria, Subcategoria = sub, Exportar = t.Exportar,
                    Classe = ponto < 0 ? valor : valor.Substring(0, ponto),
                    Tipo = ponto < 0 ? "" : valor.Substring(ponto + 1),
                };
                if (!r.ContainsKey(item.Chave)) r[item.Chave] = item;
            }
            return r;
        }
    }
}
