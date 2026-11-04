using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace MetroSP.Ifc
{
    /// <summary>
    /// Estrutura espacial e dados de projeto aplicados ao IFC exportado (parte da configuração MetroSP,
    /// arquivo MetroSP.ifc.json). Campos vazios não alteram o que a Autodesk gravou.
    /// </summary>
    public class SpatialConfig
    {
        public ProjectInfo Projeto { get; set; } = new ProjectInfo();
        public SiteInfo Terreno { get; set; } = new SiteInfo();

        /// <summary>
        /// Instalações dentro do IfcSite (IFC 4.3): a linha como IfcRailway, estações como IfcBuilding,
        /// e também IfcBridge e IfcRoad.
        /// </summary>
        public List<FacilityInfo> Instalacoes { get; set; } = new List<FacilityInfo>();

        /// <summary>Formato antigo (v0.4 a v0.7): só IfcBuilding. Lido e convertido para Instalacoes.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<FacilityInfo> Edificacoes { get; set; }

        /// <summary>Instalações, incluindo as do formato antigo como IfcBuilding.</summary>
        public List<FacilityInfo> AllFacilities()
        {
            var all = new List<FacilityInfo>(Instalacoes ?? new List<FacilityInfo>());
            foreach (var b in Edificacoes ?? new List<FacilityInfo>())
            {
                if (string.IsNullOrWhiteSpace(b.Tipo) || b.Tipo == FacilityInfo.DefaultType) b.Tipo = "IfcBuilding";
                all.Add(b);
            }
            return all;
        }

        /// <summary>Sistema de coordenadas para o IfcMapConversion, quando a Autodesk não gravar um.</summary>
        public string SistemaCoordenadas { get; set; } = "EPSG:31983";
    }

    public class ProjectInfo
    {
        public string Nome { get; set; }
        public string NomeLongo { get; set; }
        public string Descricao { get; set; }
        public string Fase { get; set; }
    }

    public class SiteInfo
    {
        public string Nome { get; set; }
        public string NomeLongo { get; set; }
        public string Descricao { get; set; }
        public double? Latitude { get; set; }      // graus decimais (negativo = sul)
        public double? Longitude { get; set; }     // graus decimais (negativo = oeste)
        public double? Elevacao { get; set; }      // m
        public string Matricula { get; set; }      // LandTitleNumber
    }

    public class FacilityInfo
    {
        public const string DefaultType = "IfcRailway";
        public static readonly string[] Types = { "IfcRailway", "IfcBuilding", "IfcBridge", "IfcRoad" };

        public string Tipo { get; set; } = DefaultType;
        public string Nome { get; set; }
        public string NomeLongo { get; set; }
        public string Descricao { get; set; }
        public double? ElevacaoReferencia { get; set; }   // só IfcBuilding
    }
}
