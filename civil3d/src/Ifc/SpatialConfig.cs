using System.Collections.Generic;

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
        public List<BuildingInfo> Edificacoes { get; set; } = new List<BuildingInfo>();

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

    public class BuildingInfo
    {
        public string Nome { get; set; }
        public string NomeLongo { get; set; }
        public string Descricao { get; set; }
        public double? ElevacaoReferencia { get; set; }
    }
}
