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
        /// </summary>
        public void LoadUserDefinedPropertySets(string path)
        {
            PropertyMapping header = null;
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
                    if (first.Length > 0) parts.Insert(0, first);

                    if (parts.Count < 2)
                    {
                        Warnings.Add($"{Path.GetFileName(path)} linha {n}: PropertySet incompleto.");
                        header = null;
                        continue;
                    }
                    header = new PropertyMapping
                    {
                        PropertySet = parts[0],
                        OnType = parts[1].Equals("T", StringComparison.OrdinalIgnoreCase),
                    };
                    if (parts.Count > 2 && parts[2].Contains(' ') && !parts[2].Contains(','))
                        Warnings.Add($"{Path.GetFileName(path)} linha {n}: entidades separadas por espaço (\"{parts[2]}\"); separe com vírgula.");
                    if (parts.Count > 2)
                        header.Entities.AddRange(parts[2].Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim()));
                    continue;
                }

                if (header == null)
                {
                    Warnings.Add($"{Path.GetFileName(path)} linha {n}: propriedade fora de um PropertySet.");
                    continue;
                }

                var p = new PropertyMapping
                {
                    PropertySet = header.PropertySet,
                    OnType = header.OnType,
                    Property = cols[0],
                    FileType = cols.Length > 1 ? cols[1] : "Text",
                    RevitParameter = cols.Length > 2 ? cols[2] : cols[0],
                    Source = Path.GetFileName(path),
                };
                p.Entities.AddRange(header.Entities);
                Properties.Add(p);
                if (!TipoAceito(p.FileType))
                    Warnings.Add($"{Path.GetFileName(path)} linha {n}: tipo \"{p.FileType}\" de {p.Property} não é aceito pelo exportador" +
                                 (TipoCorreto(p.FileType) is string certo ? $" (escreva \"{certo}\")." : "."));
                if (cols.Length > 3)
                    Warnings.Add($"{Path.GetFileName(path)} linha {n}: colunas a mais em {p.Property} (só valem propriedade, tipo e parâmetro).");
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

        /// <summary>Tipos de dado aceitos no .txt (cabeçalho do DefaultUserDefinedParameterSets.txt do exportador).</summary>
        public static readonly string[] TiposAceitos =
        {
            "Text", "Label", "Identifier", "Boolean", "Logical", "Integer", "Real", "Numeric", "Count",
            "Length", "PositiveLength", "Area", "Volume", "PlaneAngle", "PositivePlaneAngle", "Ratio", "PositiveRatio", "NormalisedRatio",
            "Mass", "MassDensity", "MassFlowRate", "MassPerLength", "Force", "LinearForce", "PlanarForce", "Pressure", "Power", "Energy",
            "ElectricCurrent", "ElectricVoltage", "ElectricalCurrent", "ElectricalVoltage", "ElectricalEfficacy", "Frequency",
            "Illuminance", "LuminousFlux", "LuminousIntensity", "ColorTemperature", "ThermodynamicTemperature", "ThermalTransmittance",
            "ThermalConductivity", "ThermalResistance", "ThermalExpansionCoefficient", "SpecificHeatCapacity", "HeatFluxDensity", "HeatingValue",
            "VolumetricFlowRate", "Currency", "ClassificationReference", "Time", "Torque", "Acceleration", "AngularVelocity", "AreaDensity",
            "DynamicViscosity", "IonConcentration", "IsothermalMoistureCapacity", "LinearMoment", "LinearStiffness", "LinearVelocity",
            "ModulusOfElasticity", "MoistureDiffusivity", "MomentOfInertia", "RotationalFrequency", "SoundPower", "SoundPressure",
            "VaporPermeability", "WarpingConstant",
        };

        /// <summary>true se o tipo está escrito exatamente como o exportador espera.</summary>
        public static bool TipoAceito(string tipo) => TiposAceitos.Contains(tipo ?? "", StringComparer.Ordinal);

        /// <summary>Grafia certa de um tipo escrito com outras maiúsculas/minúsculas, ou de erros comuns ("Lenght"). null se não houver.</summary>
        public static string TipoCorreto(string tipo)
        {
            if (string.IsNullOrWhiteSpace(tipo)) return null;
            string t = tipo.Trim();
            string mesmo = TiposAceitos.FirstOrDefault(a => string.Equals(a, t, StringComparison.OrdinalIgnoreCase));
            if (mesmo != null) return mesmo == t ? null : mesmo;
            switch (t.ToLowerInvariant())
            {
                case "lenght": return "Length";
                case "number": return "Real";
                case "wattage": return "Power";
                case "temperature": return "ThermodynamicTemperature";
                case "ifcelectricalvoltage": return "ElectricVoltage";
                case "percent": case "percentage": return "Ratio";
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
