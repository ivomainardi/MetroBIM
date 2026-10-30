using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MetroSP.Ids
{
    /// <summary>Uma propriedade que o exportador IFC vai gerar a partir de um parâmetro do Revit.</summary>
    public class PropertyMapping
    {
        public string PropertySet { get; set; }
        public string Property { get; set; }
        public string RevitParameter { get; set; }
        /// <summary>Tipo declarado no arquivo (Text, Label, Boolean, Length...).</summary>
        public string FileType { get; set; }
        /// <summary>true = pset de tipo (T), false = de instância (I).</summary>
        public bool OnType { get; set; }
        /// <summary>Entidades IFC às quais o pset se aplica (vazio = todas).</summary>
        public List<string> Entities { get; } = new List<string>();
        public string Source { get; set; }

        /// <summary>Tipo IFC que o exportador grava para o tipo declarado no arquivo.</summary>
        public string IfcType => ExportMapping.FileTypeToIfc(FileType);

        public bool AppliesTo(string ifcEntity) =>
            Entities.Count == 0 || Entities.Any(e => string.Equals(e, ifcEntity, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Linha da tabela de classes IFC: categoria/subcategoria do Revit → entidade IFC.</summary>
    public class CategoryMapping
    {
        public string Category { get; set; }
        public string SubCategory { get; set; }
        public string IfcClass { get; set; }
        public string PredefinedType { get; set; }
    }

    /// <summary>
    /// Configuração de exportação IFC lida dos arquivos usados no setup de exportação.
    /// </summary>
    public class ExportMapping
    {
        public List<PropertyMapping> Properties { get; } = new List<PropertyMapping>();
        public List<CategoryMapping> Categories { get; } = new List<CategoryMapping>();
        public List<string> Warnings { get; } = new List<string>();

        public IEnumerable<PropertyMapping> Find(string ifcEntity, string pset, string property) =>
            Properties.Where(p =>
                string.Equals(p.PropertySet, pset, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.Property, property, StringComparison.OrdinalIgnoreCase) &&
                p.AppliesTo(ifcEntity));

        /// <summary>
        /// Arquivo de "conjuntos de propriedades definidos pelo usuário" do exportador IFC. Formato:
        ///   PropertySet:  &lt;Nome&gt;  &lt;I|T&gt;  &lt;IfcEntidade1,IfcEntidade2&gt;
        ///       &lt;Propriedade&gt;  &lt;Tipo&gt;  [&lt;Parâmetro Revit&gt;]
        /// Linhas começando com # são comentários. Sem o parâmetro, o nome da propriedade é usado.
        ///
        /// Os avisos seguem o leitor do exportador da Autodesk (revit-ifc, PropertyMap.LoadUserDefinedPset):
        /// colunas separadas por tab; cabeçalho com 4 colunas (senão é ignorado e as propriedades seguintes caem no
        /// property set anterior); propriedade com pelo menos nome e tipo; tipo desconhecido vira Text.
        /// </summary>
        public void LoadUserDefinedPropertySets(string path)
        {
            PropertyMapping header = null;
            string arquivo = Path.GetFileName(path);
            int n = 0;
            foreach (string raw in ReadLines(path))
            {
                n++;
                string line = raw.TrimEnd();
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#")) continue;

                string[] cols = line.Split('\t').Select(c => c.Trim()).Where(c => c.Length > 0).ToArray();
                if (cols.Length == 0) continue;

                if (cols[0].StartsWith("PropertySet:", StringComparison.OrdinalIgnoreCase))
                {
                    // "PropertySet:" pode vir colado ao nome por espaço em vez de tab
                    var parts = cols.ToList();
                    string first = parts[0].Substring("PropertySet:".Length).Trim();
                    parts.RemoveAt(0);
                    if (first.Length > 0)
                    {
                        parts.Insert(0, first);
                        Warnings.Add($"{arquivo} linha {n}: depois de \"PropertySet:\" deve vir um tab; com espaço o exportador ignora o property set.");
                    }

                    if (parts.Count < 2)
                    {
                        Warnings.Add($"{arquivo} linha {n}: PropertySet incompleto (faltam I/T e entidades); o exportador o ignora.");
                        header = null;
                        continue;
                    }
                    header = new PropertyMapping
                    {
                        PropertySet = parts[0],
                        OnType = parts[1].Equals("T", StringComparison.OrdinalIgnoreCase),
                    };
                    if (!parts[1].Equals("I", StringComparison.OrdinalIgnoreCase) && !parts[1].Equals("T", StringComparison.OrdinalIgnoreCase))
                        Warnings.Add($"{arquivo} linha {n}: {parts[0]} deve ter I (instância) ou T (tipo), não \"{parts[1]}\".");
                    if (parts.Count < 3)
                        Warnings.Add($"{arquivo} linha {n}: {parts[0]} sem entidades IFC; o exportador ignora o cabeçalho e as propriedades seguintes vão para o property set anterior.");
                    if (parts[0].StartsWith("Pset_", StringComparison.OrdinalIgnoreCase))
                        Warnings.Add($"{arquivo} linha {n}: nomes com \"Pset_\" são reservados aos property sets padrão do IFC; o exportador renomeia os outros.");
                    if (parts.Count > 2)
                    {
                        // O exportador separa as entidades por vírgula, ponto e vírgula ou espaço.
                        header.Entities.AddRange(parts[2].Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim()));
                        foreach (string e in header.Entities.Where(e => !e.StartsWith("Ifc", StringComparison.OrdinalIgnoreCase)))
                            Warnings.Add($"{arquivo} linha {n}: entidade \"{e}\" não parece uma entidade IFC (deve começar com Ifc).");
                    }
                    continue;
                }

                if (header == null)
                {
                    Warnings.Add($"{arquivo} linha {n}: propriedade fora de um PropertySet.");
                    continue;
                }

                if (cols.Length < 2)
                {
                    Warnings.Add($"{arquivo} linha {n}: {cols[0]} sem tipo de dado; o exportador ignora a linha.");
                    continue;
                }

                var p = new PropertyMapping
                {
                    PropertySet = header.PropertySet,
                    OnType = header.OnType,
                    Property = cols[0],
                    FileType = cols[1],
                    RevitParameter = cols.Length > 2 ? cols[2] : cols[0],
                    Source = arquivo,
                };
                p.Entities.AddRange(header.Entities);
                Properties.Add(p);
                if (!TipoAceito(p.FileType))
                    Warnings.Add($"{arquivo} linha {n}: tipo \"{p.FileType}\" de {p.Property} não existe no exportador e sai como Text" +
                                 (TipoCorreto(p.FileType) is string certo ? $" (use \"{certo}\")." : "."));
                if (cols.Length > 3)
                    Warnings.Add($"{arquivo} linha {n}: colunas a mais em {p.Property}; o exportador usa só propriedade, tipo e parâmetro (\"{cols[2]}\").");
            }
        }

        /// <summary>
        /// Tabela de classes IFC exportada pela janela "Opções de IFC" do Revit. Formato por linha:
        ///   &lt;Categoria&gt;  &lt;Subcategoria&gt;  &lt;Classe IFC&gt;  [&lt;Tipo predefinido&gt;]
        /// </summary>
        public void LoadCategoryMapping(string path)
        {
            foreach (string raw in ReadLines(path))
            {
                if (raw.Trim().Length == 0 || raw.TrimStart().StartsWith("#")) continue;
                string[] cols = raw.Split('\t').Select(c => c.Trim()).ToArray();
                if (cols.Length < 3 || cols[2].Length == 0) continue;
                Categories.Add(new CategoryMapping
                {
                    Category = cols[0],
                    SubCategory = cols[1],
                    IfcClass = cols[2],
                    PredefinedType = cols.Length > 3 && cols[3].Length > 0 ? cols[3] : null,
                });
            }
        }

        /// <summary>Classe IFC configurada para a categoria (linha sem subcategoria). null se não houver.</summary>
        public string IfcClassForCategory(string category)
        {
            CategoryMapping m = Categories.FirstOrDefault(c =>
                string.Equals(c.Category, category, StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(c.SubCategory));
            return m?.IfcClass;
        }

        /// <summary>
        /// Tipos de dado do exportador IFC da Autodesk (enum PropertyType do revit-ifc). A leitura ignora maiúsculas e
        /// aceita o prefixo "Ifc" (IfcLabel = Label); um tipo fora da lista é exportado como Text.
        /// </summary>
        public static readonly string[] TiposAceitos =
        {
            "Text", "Label", "Identifier", "Boolean", "Logical", "Integer", "Real", "Numeric", "Count",
            "Length", "PositiveLength", "NonNegativeLength", "Area", "Volume", "PlaneAngle", "PositivePlaneAngle",
            "Ratio", "PositiveRatio", "NormalisedRatio", "Mass", "MassDensity", "MassFlowRate", "MassPerLength", "MassPerUnitArea",
            "AreaDensity", "UnitWeight", "Force", "LinearForce", "PlanarForce", "Pressure", "Power", "Energy", "Torque",
            "ElectricCurrent", "ElectricVoltage", "ElectricResistance", "ElectricConductance", "ElectricCapacitance", "ElectricCharge",
            "ElectricalEfficacy", "ElectricalResistivity", "ElectricalPowerDensity", "Inductance", "Frequency", "RotationalFrequency",
            "Illuminance", "LuminousFlux", "LuminousIntensity", "Luminance", "Efficacy", "ColorTemperature",
            "ThermodynamicTemperature", "TemperatureRateOfChange", "ThermalTransmittance", "ThermalConductivity", "ThermalResistance",
            "ThermalExpansionCoefficient", "ThermalMass", "SpecificHeatCapacity", "HeatFluxDensity", "HeatingValue", "HeatCapacityPerArea",
            "VolumetricFlowRate", "LinearVelocity", "Acceleration", "AngularVelocity", "DynamicViscosity", "ModulusOfElasticity",
            "MomentOfInertia", "SectionModulus", "WarpingConstant", "LinearMoment", "LinearStiffness", "IonConcentration", "PH",
            "IsothermalMoistureCapacity", "VaporPermeability", "MoistureDiffusivity", "MolecularWeight", "SoundPower", "SoundPowerLevel",
            "SoundPressure", "RadioActivity", "Currency", "Monetary", "CostPerArea", "Time", "Duration", "Date", "DateTime", "LocalTime",
            "IntegerCountRate", "ComplexNumber", "URIReference", "ClassificationReference", "FrictionLoss", "PowerPerLength",
            "ApparentPowerDensity", "CostRateEnergy", "CostRatePower", "ThermalGradientCoefficientForMoistureCapacity",
            "AirFlowDensity", "AirFlowDividedByCoolingLoad", "AirFlowDividedByVolume", "AreaDividedByCoolingLoad", "AreaDividedByHeatingLoad",
            "CoolingLoadDividedByArea", "CoolingLoadDividedByVolume", "FlowPerPower", "HvacFriction", "HeatingLoadDividedByArea",
            "HeatingLoadDividedByVolume", "PowerPerFlow", "PipingFriction", "AreaSpringCoefficient", "LineSpringCoefficient",
            "ReinforcementAreaPerUnitLength", "RotationalLineSpringCoefficient", "RotationalPointSpringCoefficient",
            "IfcMaterial", "IfcMaterialDefinition", "IfcPerson", "IfcOrganization", "IfcTimeSeries", "IfcExternalReference",
            "IfcDocumentReference", "IfcClassificationReference", "IfcCalendarDate", "IfcDateAndTime", "IfcLocalTime", "IfcCostValue", "IfcRelaxation",
        };

        // Tipos mais usados primeiro, para a lista do editor.
        public static readonly string[] TiposComuns =
        {
            "Text", "Label", "Identifier", "Boolean", "Integer", "Real", "Count", "Length", "PositiveLength", "Area", "Volume",
            "PlaneAngle", "Ratio", "PositiveRatio", "Mass", "Pressure", "Power", "ElectricCurrent", "ElectricVoltage", "LuminousFlux",
            "Illuminance", "ColorTemperature", "ThermodynamicTemperature", "VolumetricFlowRate", "ClassificationReference",
        };

        /// <summary>O tipo como o exportador lê: sem "Property&lt;Valor&gt;." na frente, o primeiro de "A/B" e sem "Ifc".</summary>
        private static string TipoBase(string tipo)
        {
            string t = (tipo ?? "").Trim();
            int ponto = t.IndexOf('.');
            if (ponto >= 0) t = t.Substring(ponto + 1);
            int barra = t.IndexOf('/');
            if (barra >= 0) t = t.Substring(0, barra);
            return t.Trim();
        }

        /// <summary>true se o exportador reconhece o tipo (sem virar Text por engano).</summary>
        public static bool TipoAceito(string tipo)
        {
            string t = TipoBase(tipo);
            if (t.Length == 0) return false;
            if (TiposAceitos.Contains(t, StringComparer.OrdinalIgnoreCase)) return true;
            return t.StartsWith("Ifc", StringComparison.OrdinalIgnoreCase) && TiposAceitos.Contains(t.Substring(3), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Tipo certo para erros comuns ("Lenght", "Number", "Wattage"...). null se não houver sugestão.</summary>
        public static string TipoCorreto(string tipo)
        {
            string t = TipoBase(tipo);
            if (t.StartsWith("Ifc", StringComparison.OrdinalIgnoreCase)) t = t.Substring(3);
            switch (t.ToLowerInvariant())
            {
                case "lenght": case "lengh": case "lentgh": return "Length";
                case "number": case "double": case "decimal": return "Real";
                case "int": return "Integer";
                case "bool": case "yesno": return "Boolean";
                case "string": return "Text";
                case "wattage": case "watts": return "Power";
                case "temperature": return "ThermodynamicTemperature";
                case "electricalvoltage": case "voltage": return "ElectricVoltage";
                case "electricalcurrent": case "current": return "ElectricCurrent";
                case "percent": case "percentage": case "slope": return "Ratio";
                case "material": return "Text";
                case "weight": return "Mass";
                case "angle": return "PlaneAngle";
                default: return null;
            }
        }

        public static string FileTypeToIfc(string fileType)
        {
            switch ((fileType ?? "").ToLowerInvariant())
            {
                case "text": return "IFCTEXT";
                case "label": return "IFCLABEL";
                case "identifier": return "IFCIDENTIFIER";
                case "boolean": return "IFCBOOLEAN";
                case "logical": return "IFCLOGICAL";
                case "integer": return "IFCINTEGER";
                case "real": return "IFCREAL";
                case "length": return "IFCLENGTHMEASURE";
                case "positivelength": return "IFCPOSITIVELENGTHMEASURE";
                case "area": return "IFCAREAMEASURE";
                case "volume": return "IFCVOLUMEMEASURE";
                case "count": return "IFCCOUNTMEASURE";
                case "positiveratio": return "IFCPOSITIVERATIOMEASURE";
                case "ratio": return "IFCRATIOMEASURE";
                case "planeangle": return "IFCPLANEANGLEMEASURE";
                case "thermaltransmittance": return "IFCTHERMALTRANSMITTANCEMEASURE";
                case "massdensity": return "IFCMASSDENSITYMEASURE";
                case "powermeasure": return "IFCPOWERMEASURE";
                case "electricalcurrent": return "IFCELECTRICCURRENTMEASURE";
                case "electricalvoltage": return "IFCELECTRICVOLTAGEMEASURE";
                case "volumetricflowrate": return "IFCVOLUMETRICFLOWRATEMEASURE";
                case "pressure": return "IFCPRESSUREMEASURE";
                case "massflowrate": return "IFCMASSFLOWRATEMEASURE";
                case "frequency": return "IFCFREQUENCYMEASURE";
                case "illuminance": return "IFCILLUMINANCEMEASURE";
                case "luminousflux": return "IFCLUMINOUSFLUXMEASURE";
                case "force": return "IFCFORCEMEASURE";
                case "currency": return "IFCMONETARYMEASURE";
                case "thermodynamictemperature": return "IFCTHERMODYNAMICTEMPERATUREMEASURE";
                default: return null;
            }
        }

        /// <summary>Lê texto detectando BOM (os arquivos do Revit às vezes vêm em UTF-16).</summary>
        private static IEnumerable<string> ReadLines(string path)
        {
            using (var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                string line;
                while ((line = reader.ReadLine()) != null) yield return line;
            }
        }
    }
}
