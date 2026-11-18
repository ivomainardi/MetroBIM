using System;
using System.Collections.Generic;

namespace MetroSP.Terreno
{
    /// <summary>
    /// Superfície TIN do Civil 3D (modelo neutro, sem dependência da API): pontos sem repetição e triângulos
    /// com os índices dos pontos (a partir de 0). Coordenadas do desenho, em metros.
    /// </summary>
    public class SurfaceModel
    {
        public string Name;
        public string Description;
        public string Handle;
        public string Layer;
        public List<double[]> Points = new List<double[]>();
        public List<int[]> Triangles = new List<int[]>();

        public double MinZ = double.PositiveInfinity, MaxZ = double.NegativeInfinity;
        public double Area2D, Area3D;

        /// <summary>Cota mínima e máxima e áreas em planta e 3D, a partir dos triângulos.</summary>
        public void Compute()
        {
            MinZ = double.PositiveInfinity; MaxZ = double.NegativeInfinity; Area2D = 0; Area3D = 0;
            foreach (var p in Points)
            {
                if (p[2] < MinZ) MinZ = p[2];
                if (p[2] > MaxZ) MaxZ = p[2];
            }
            foreach (var t in Triangles)
            {
                double[] a = Points[t[0]], b = Points[t[1]], c = Points[t[2]];
                double ux = b[0] - a[0], uy = b[1] - a[1], uz = b[2] - a[2];
                double vx = c[0] - a[0], vy = c[1] - a[1], vz = c[2] - a[2];
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                Area2D += Math.Abs(nz) / 2;
                Area3D += Math.Sqrt(nx * nx + ny * ny + nz * nz) / 2;
            }
        }
    }
}
