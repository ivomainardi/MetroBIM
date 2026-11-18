// Gerado a partir dos esquemas oficiais IFC4 (ADD2 TC1) e IFC4X3_ADD2 (IfcOpenShell) por tools/gerar-correcoes-ifc.py.
// Não editar à mão.
// Simples: classes de elemento com 8 atributos + PredefinedType, e o tipo IfcXxxType com 9 + PredefinedType
// (a troca de classe só muda o nome e o PredefinedType). Elementos e Distribuicao: subclasses concretas de IfcElement e
// IfcDistributionElement (os dois esquemas, em maiúsculas). Sistemas: valores do IfcDistributionSystemEnum.
namespace MetroSP.Ifc
{
    public static partial class CorrecoesIfc
    {
        private static readonly string[] Simples4 =
        {
            "IfcActuator", "IfcAirTerminal", "IfcAirTerminalBox", "IfcAirToAirHeatRecovery", "IfcAlarm",
            "IfcAudioVisualAppliance", "IfcBeam", "IfcBoiler", "IfcBuildingElementPart", "IfcBuildingElementProxy", "IfcBurner",
            "IfcCableCarrierFitting", "IfcCableCarrierSegment", "IfcCableFitting", "IfcCableSegment", "IfcChiller", "IfcChimney",
            "IfcCoil", "IfcColumn", "IfcCommunicationsAppliance", "IfcCompressor", "IfcCondenser", "IfcController",
            "IfcCooledBeam", "IfcCoolingTower", "IfcCovering", "IfcCurtainWall", "IfcDamper", "IfcDiscreteAccessory",
            "IfcDistributionChamberElement", "IfcDuctFitting", "IfcDuctSegment", "IfcDuctSilencer", "IfcElectricAppliance",
            "IfcElectricDistributionBoard", "IfcElectricFlowStorageDevice", "IfcElectricGenerator", "IfcElectricMotor",
            "IfcElectricTimeControl", "IfcEngine", "IfcEvaporativeCooler", "IfcEvaporator", "IfcFan", "IfcFastener", "IfcFilter",
            "IfcFireSuppressionTerminal", "IfcFlowInstrument", "IfcFlowMeter", "IfcFooting", "IfcGeographicElement",
            "IfcHeatExchanger", "IfcHumidifier", "IfcInterceptor", "IfcJunctionBox", "IfcLamp", "IfcLightFixture",
            "IfcMedicalDevice", "IfcMember", "IfcMotorConnection", "IfcOutlet", "IfcPipeFitting", "IfcPipeSegment", "IfcPlate",
            "IfcProtectiveDevice", "IfcProtectiveDeviceTrippingUnit", "IfcPump", "IfcRailing", "IfcRamp", "IfcRampFlight",
            "IfcRoof", "IfcSanitaryTerminal", "IfcSensor", "IfcShadingDevice", "IfcSlab", "IfcSolarDevice", "IfcSpaceHeater",
            "IfcStackTerminal", "IfcStair", "IfcSwitchingDevice", "IfcSystemFurnitureElement", "IfcTank", "IfcTransformer",
            "IfcTransportElement", "IfcTubeBundle", "IfcUnitaryControlElement", "IfcUnitaryEquipment", "IfcValve",
            "IfcVibrationIsolator", "IfcWall", "IfcWasteTerminal"
        };
        private static readonly string[] Simples4x3 =
        {
            "IfcActuator", "IfcAirTerminal", "IfcAirTerminalBox", "IfcAirToAirHeatRecovery", "IfcAlarm",
            "IfcAudioVisualAppliance", "IfcBeam", "IfcBearing", "IfcBoiler", "IfcBuildingElementPart", "IfcBuildingElementProxy",
            "IfcBurner", "IfcCableCarrierFitting", "IfcCableCarrierSegment", "IfcCableFitting", "IfcCableSegment",
            "IfcCaissonFoundation", "IfcChiller", "IfcChimney", "IfcCoil", "IfcColumn", "IfcCommunicationsAppliance",
            "IfcCompressor", "IfcCondenser", "IfcController", "IfcConveyorSegment", "IfcCooledBeam", "IfcCoolingTower",
            "IfcCourse", "IfcCovering", "IfcCurtainWall", "IfcDamper", "IfcDiscreteAccessory", "IfcDistributionBoard",
            "IfcDistributionChamberElement", "IfcDuctFitting", "IfcDuctSegment", "IfcDuctSilencer", "IfcElectricAppliance",
            "IfcElectricDistributionBoard", "IfcElectricFlowStorageDevice", "IfcElectricFlowTreatmentDevice",
            "IfcElectricGenerator", "IfcElectricMotor", "IfcElectricTimeControl", "IfcEngine", "IfcEvaporativeCooler",
            "IfcEvaporator", "IfcFan", "IfcFastener", "IfcFilter", "IfcFireSuppressionTerminal", "IfcFlowInstrument",
            "IfcFlowMeter", "IfcFooting", "IfcGeographicElement", "IfcHeatExchanger", "IfcHumidifier",
            "IfcImpactProtectionDevice", "IfcInterceptor", "IfcJunctionBox", "IfcKerb", "IfcLamp", "IfcLightFixture",
            "IfcLiquidTerminal", "IfcMedicalDevice", "IfcMember", "IfcMobileTelecommunicationsAppliance", "IfcMooringDevice",
            "IfcMotorConnection", "IfcNavigationElement", "IfcOutlet", "IfcPavement", "IfcPipeFitting", "IfcPipeSegment",
            "IfcPlate", "IfcProtectiveDevice", "IfcProtectiveDeviceTrippingUnit", "IfcPump", "IfcRail", "IfcRailing", "IfcRamp",
            "IfcRampFlight", "IfcRoof", "IfcSanitaryTerminal", "IfcSensor", "IfcShadingDevice", "IfcSign", "IfcSignal",
            "IfcSlab", "IfcSolarDevice", "IfcSpaceHeater", "IfcStackTerminal", "IfcStair", "IfcSwitchingDevice",
            "IfcSystemFurnitureElement", "IfcTank", "IfcTrackElement", "IfcTransformer", "IfcTransportElement", "IfcTubeBundle",
            "IfcUnitaryControlElement", "IfcUnitaryEquipment", "IfcValve", "IfcVehicle", "IfcVibrationDamper",
            "IfcVibrationIsolator", "IfcWall", "IfcWasteTerminal"
        };
        private static readonly string[] Elementos =
        {
            "IFCACTUATOR", "IFCAIRTERMINAL", "IFCAIRTERMINALBOX", "IFCAIRTOAIRHEATRECOVERY", "IFCALARM",
            "IFCAUDIOVISUALAPPLIANCE", "IFCBEAM", "IFCBEAMSTANDARDCASE", "IFCBEARING", "IFCBOILER", "IFCBOREHOLE",
            "IFCBUILDINGELEMENTPART", "IFCBUILDINGELEMENTPROXY", "IFCBUILTELEMENT", "IFCBURNER", "IFCCABLECARRIERFITTING",
            "IFCCABLECARRIERSEGMENT", "IFCCABLEFITTING", "IFCCABLESEGMENT", "IFCCAISSONFOUNDATION", "IFCCHILLER", "IFCCHIMNEY",
            "IFCCIVILELEMENT", "IFCCOIL", "IFCCOLUMN", "IFCCOLUMNSTANDARDCASE", "IFCCOMMUNICATIONSAPPLIANCE", "IFCCOMPRESSOR",
            "IFCCONDENSER", "IFCCONTROLLER", "IFCCONVEYORSEGMENT", "IFCCOOLEDBEAM", "IFCCOOLINGTOWER", "IFCCOURSE",
            "IFCCOVERING", "IFCCURTAINWALL", "IFCDAMPER", "IFCDEEPFOUNDATION", "IFCDISCRETEACCESSORY", "IFCDISTRIBUTIONBOARD",
            "IFCDISTRIBUTIONCHAMBERELEMENT", "IFCDISTRIBUTIONCONTROLELEMENT", "IFCDISTRIBUTIONELEMENT",
            "IFCDISTRIBUTIONFLOWELEMENT", "IFCDOOR", "IFCDOORSTANDARDCASE", "IFCDUCTFITTING", "IFCDUCTSEGMENT",
            "IFCDUCTSILENCER", "IFCEARTHWORKSCUT", "IFCEARTHWORKSELEMENT", "IFCEARTHWORKSFILL", "IFCELECTRICAPPLIANCE",
            "IFCELECTRICDISTRIBUTIONBOARD", "IFCELECTRICFLOWSTORAGEDEVICE", "IFCELECTRICFLOWTREATMENTDEVICE",
            "IFCELECTRICGENERATOR", "IFCELECTRICMOTOR", "IFCELECTRICTIMECONTROL", "IFCELEMENTASSEMBLY",
            "IFCENERGYCONVERSIONDEVICE", "IFCENGINE", "IFCEVAPORATIVECOOLER", "IFCEVAPORATOR", "IFCFAN", "IFCFASTENER",
            "IFCFILTER", "IFCFIRESUPPRESSIONTERMINAL", "IFCFLOWCONTROLLER", "IFCFLOWFITTING", "IFCFLOWINSTRUMENT",
            "IFCFLOWMETER", "IFCFLOWMOVINGDEVICE", "IFCFLOWSEGMENT", "IFCFLOWSTORAGEDEVICE", "IFCFLOWTERMINAL",
            "IFCFLOWTREATMENTDEVICE", "IFCFOOTING", "IFCFURNISHINGELEMENT", "IFCFURNITURE", "IFCGEOGRAPHICELEMENT",
            "IFCGEOMODEL", "IFCGEOSLICE", "IFCGEOTECHNICALSTRATUM", "IFCHEATEXCHANGER", "IFCHUMIDIFIER",
            "IFCIMPACTPROTECTIONDEVICE", "IFCINTERCEPTOR", "IFCJUNCTIONBOX", "IFCKERB", "IFCLAMP", "IFCLIGHTFIXTURE",
            "IFCLIQUIDTERMINAL", "IFCMECHANICALFASTENER", "IFCMEDICALDEVICE", "IFCMEMBER", "IFCMEMBERSTANDARDCASE",
            "IFCMOBILETELECOMMUNICATIONSAPPLIANCE", "IFCMOORINGDEVICE", "IFCMOTORCONNECTION", "IFCNAVIGATIONELEMENT",
            "IFCOPENINGELEMENT", "IFCOPENINGSTANDARDCASE", "IFCOUTLET", "IFCPAVEMENT", "IFCPILE", "IFCPIPEFITTING",
            "IFCPIPESEGMENT", "IFCPLATE", "IFCPLATESTANDARDCASE", "IFCPROJECTIONELEMENT", "IFCPROTECTIVEDEVICE",
            "IFCPROTECTIVEDEVICETRIPPINGUNIT", "IFCPUMP", "IFCRAIL", "IFCRAILING", "IFCRAMP", "IFCRAMPFLIGHT",
            "IFCREINFORCEDSOIL", "IFCREINFORCINGBAR", "IFCREINFORCINGMESH", "IFCROOF", "IFCSANITARYTERMINAL", "IFCSENSOR",
            "IFCSHADINGDEVICE", "IFCSIGN", "IFCSIGNAL", "IFCSLAB", "IFCSLABELEMENTEDCASE", "IFCSLABSTANDARDCASE",
            "IFCSOLARDEVICE", "IFCSPACEHEATER", "IFCSTACKTERMINAL", "IFCSTAIR", "IFCSTAIRFLIGHT", "IFCSURFACEFEATURE",
            "IFCSWITCHINGDEVICE", "IFCSYSTEMFURNITUREELEMENT", "IFCTANK", "IFCTENDON", "IFCTENDONANCHOR", "IFCTENDONCONDUIT",
            "IFCTRACKELEMENT", "IFCTRANSFORMER", "IFCTRANSPORTELEMENT", "IFCTUBEBUNDLE", "IFCUNITARYCONTROLELEMENT",
            "IFCUNITARYEQUIPMENT", "IFCVALVE", "IFCVEHICLE", "IFCVIBRATIONDAMPER", "IFCVIBRATIONISOLATOR", "IFCVIRTUALELEMENT",
            "IFCVOIDINGFEATURE", "IFCWALL", "IFCWALLELEMENTEDCASE", "IFCWALLSTANDARDCASE", "IFCWASTETERMINAL", "IFCWINDOW",
            "IFCWINDOWSTANDARDCASE"
        };
        private static readonly string[] Distribuicao =
        {
            "IFCACTUATOR", "IFCAIRTERMINAL", "IFCAIRTERMINALBOX", "IFCAIRTOAIRHEATRECOVERY", "IFCALARM",
            "IFCAUDIOVISUALAPPLIANCE", "IFCBOILER", "IFCBURNER", "IFCCABLECARRIERFITTING", "IFCCABLECARRIERSEGMENT",
            "IFCCABLEFITTING", "IFCCABLESEGMENT", "IFCCHILLER", "IFCCOIL", "IFCCOMMUNICATIONSAPPLIANCE", "IFCCOMPRESSOR",
            "IFCCONDENSER", "IFCCONTROLLER", "IFCCONVEYORSEGMENT", "IFCCOOLEDBEAM", "IFCCOOLINGTOWER", "IFCDAMPER",
            "IFCDISTRIBUTIONBOARD", "IFCDISTRIBUTIONCHAMBERELEMENT", "IFCDISTRIBUTIONCONTROLELEMENT", "IFCDISTRIBUTIONELEMENT",
            "IFCDISTRIBUTIONFLOWELEMENT", "IFCDUCTFITTING", "IFCDUCTSEGMENT", "IFCDUCTSILENCER", "IFCELECTRICAPPLIANCE",
            "IFCELECTRICDISTRIBUTIONBOARD", "IFCELECTRICFLOWSTORAGEDEVICE", "IFCELECTRICFLOWTREATMENTDEVICE",
            "IFCELECTRICGENERATOR", "IFCELECTRICMOTOR", "IFCELECTRICTIMECONTROL", "IFCENERGYCONVERSIONDEVICE", "IFCENGINE",
            "IFCEVAPORATIVECOOLER", "IFCEVAPORATOR", "IFCFAN", "IFCFILTER", "IFCFIRESUPPRESSIONTERMINAL", "IFCFLOWCONTROLLER",
            "IFCFLOWFITTING", "IFCFLOWINSTRUMENT", "IFCFLOWMETER", "IFCFLOWMOVINGDEVICE", "IFCFLOWSEGMENT",
            "IFCFLOWSTORAGEDEVICE", "IFCFLOWTERMINAL", "IFCFLOWTREATMENTDEVICE", "IFCHEATEXCHANGER", "IFCHUMIDIFIER",
            "IFCINTERCEPTOR", "IFCJUNCTIONBOX", "IFCLAMP", "IFCLIGHTFIXTURE", "IFCLIQUIDTERMINAL", "IFCMEDICALDEVICE",
            "IFCMOBILETELECOMMUNICATIONSAPPLIANCE", "IFCMOTORCONNECTION", "IFCOUTLET", "IFCPIPEFITTING", "IFCPIPESEGMENT",
            "IFCPROTECTIVEDEVICE", "IFCPROTECTIVEDEVICETRIPPINGUNIT", "IFCPUMP", "IFCSANITARYTERMINAL", "IFCSENSOR", "IFCSIGNAL",
            "IFCSOLARDEVICE", "IFCSPACEHEATER", "IFCSTACKTERMINAL", "IFCSWITCHINGDEVICE", "IFCTANK", "IFCTRANSFORMER",
            "IFCTUBEBUNDLE", "IFCUNITARYCONTROLELEMENT", "IFCUNITARYEQUIPMENT", "IFCVALVE", "IFCWASTETERMINAL"
        };
        private static readonly string[] Sistemas4 =
        {
            "AIRCONDITIONING", "AUDIOVISUAL", "CHEMICAL", "CHILLEDWATER", "COMMUNICATION", "COMPRESSEDAIR", "CONDENSERWATER",
            "CONTROL", "CONVEYING", "DATA", "DISPOSAL", "DOMESTICCOLDWATER", "DOMESTICHOTWATER", "DRAINAGE", "EARTHING",
            "ELECTRICAL", "ELECTROACOUSTIC", "EXHAUST", "FIREPROTECTION", "FUEL", "GAS", "HAZARDOUS", "HEATING", "LIGHTING",
            "LIGHTNINGPROTECTION", "MUNICIPALSOLIDWASTE", "OIL", "OPERATIONAL", "POWERGENERATION", "RAINWATER", "REFRIGERATION",
            "SECURITY", "SEWAGE", "SIGNAL", "STORMWATER", "TELEPHONE", "TV", "VACUUM", "VENT", "VENTILATION", "WASTEWATER",
            "WATERSUPPLY", "USERDEFINED", "NOTDEFINED"
        };
        private static readonly string[] Sistemas4x3 =
        {
            "AIRCONDITIONING", "AUDIOVISUAL", "CATENARY_SYSTEM", "CHEMICAL", "CHILLEDWATER", "COMMUNICATION", "COMPRESSEDAIR",
            "CONDENSERWATER", "CONTROL", "CONVEYING", "DATA", "DISPOSAL", "DOMESTICCOLDWATER", "DOMESTICHOTWATER", "DRAINAGE",
            "EARTHING", "ELECTRICAL", "ELECTROACOUSTIC", "EXHAUST", "FIREPROTECTION", "FIXEDTRANSMISSIONNETWORK", "FUEL", "GAS",
            "HAZARDOUS", "HEATING", "LIGHTING", "LIGHTNINGPROTECTION", "MOBILENETWORK", "MONITORINGSYSTEM",
            "MUNICIPALSOLIDWASTE", "OIL", "OPERATIONAL", "OPERATIONALTELEPHONYSYSTEM", "OVERHEAD_CONTACTLINE_SYSTEM",
            "POWERGENERATION", "RAINWATER", "REFRIGERATION", "RETURN_CIRCUIT", "SECURITY", "SEWAGE", "SIGNAL", "STORMWATER",
            "TELEPHONE", "TV", "VACUUM", "VENT", "VENTILATION", "WASTEWATER", "WATERSUPPLY", "USERDEFINED", "NOTDEFINED"
        };
    }
}
