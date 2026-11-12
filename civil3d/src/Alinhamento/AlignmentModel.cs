using System.Collections.Generic;

namespace MetroSP.Alinhamento
{
    // Modelo neutro (sem dependência do Civil 3D) com o que vai para o IFC.
    // Convenções iguais às do IFC 4.3:
    //  - ângulos em radianos, medidos a partir do eixo X no sentido anti-horário;
    //  - raio positivo = curva para a esquerda (anti-horária), 0 = reta/raio infinito;
    //  - rampas como razão (0.02 = 2%); raio vertical positivo = côncavo (rampa crescente).

    public class HorizontalSegment
    {
        public string Type;            // LINE, CIRCULARARC, CLOTHOID, CUBIC, BLOSSCURVE, ...
        public double StartX, StartY;
        public double StartDirection;
        public double StartRadius;
        public double EndRadius;
        public double Length;
        public double StartStation;    // só para nome/descrição
    }

    public class VerticalSegment
    {
        public string Type;            // CONSTANTGRADIENT, PARABOLICARC, CIRCULARARC
        public double StartDistAlong;  // distância ao longo do eixo a partir do início do alinhamento
        public double HorizontalLength;
        public double StartHeight;
        public double StartGradient;
        public double EndGradient;
        public double Radius;          // 0 para rampa constante
    }

    public class AlignmentModel
    {
        /// <summary>Estacas do eixo 3D em que o perfil não tem cota (perfil com trecho faltando).</summary>
        public int Lacunas;

        public string Name;
        public string Description;
        public string ProfileName;
        public string Handle;          // usado para GUIDs estáveis entre exportações
        public double StartStation;
        public double EndStation;
        public List<HorizontalSegment> Horizontal = new List<HorizontalSegment>();
        public List<VerticalSegment> Vertical = new List<VerticalSegment>();
        public List<double[]> Axis3D = new List<double[]>();   // {x, y, z} amostrados no Civil 3D
    }

    public class IfcExportInfo
    {
        public string ProjectName;
        public string SourceFile;
        public string Author;
        public string ProjectedCrs;    // ex.: EPSG:31983 (SIRGAS 2000 / UTM 23S); vazio = sem IfcMapConversion
        public string Seed;            // identifica o desenho (fingerprint) para GUIDs estáveis
    }
}
