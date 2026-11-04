using System;

namespace MetroSP.Ifc
{
    /// <summary>
    /// Conversão entre o Plano Topográfico Local do Metrô-SP (ABNT NBR 14166) e coordenadas geodésicas SIRGAS 2000.
    /// Porte direto do "Núcleo de transformação v6.5" do GeoDoc v1.0 (calcularPTL e ptlToLatLon),
    /// com os mesmos parâmetros e o mesmo autoteste de 26 pontos.
    /// X do PTL = Este (E/W no Revit), Y = Norte (N/S). Latitude e longitude em graus decimais, sul e oeste negativos.
    /// </summary>
    public static class PtlMetroSP
    {
        public const string Identificacao = "PTL Metrô-SP · ABNT NBR 14166 · SIRGAS 2000 · núcleo GeoDoc v6.5";

        // Elipsoide GRS80 (SIRGAS 2000)
        private const double A = 6378137.0;
        private const double FInv = 298.257222101;
        private const double F = 1 / FInv;
        private const double E2 = F * (2 - F);
        private const double Arc1s = Math.PI / (180 * 3600);
        private const double KAs = 3.9173e-12;

        // Origem do PTL
        public static readonly double Lat0 = -(23 + 33 / 60.0 + 3.0482 / 3600);
        public static readonly double Lon0 = -(46 + 43 / 60.0 + 53.6793 / 3600);
        private const double Ht = 760.0;
        public const double X0 = 150000.0;
        public const double Y0 = 250000.0;

        /// <summary>Raio a partir da origem em que o GeoDoc considera o PTL válido (m).</summary>
        public const double RaioRecomendado = 50000.0;

        private static double Rad(double graus) => graus * Math.PI / 180;
        private static double Sq(double v) => v * v;

        private struct Constantes
        {
            public double C, B, Cc, D, E;
        }

        private static Constantes Origem()
        {
            double w0r = Rad(Lat0);
            double n0 = A / Math.Sqrt(1 - E2 * Sq(Math.Sin(w0r)));
            double m0 = A * (1 - E2) / Math.Pow(1 - E2 * Sq(Math.Sin(w0r)), 1.5);
            double rm = Math.Sqrt(m0 * n0);
            return new Constantes
            {
                C = (rm + Ht) / rm,
                B = 1 / (m0 * Arc1s),
                Cc = Math.Tan(w0r) / (2 * m0 * n0 * Arc1s),
                D = 3 * E2 * Math.Sin(w0r) * Math.Cos(w0r) * Arc1s / (2 * (1 - E2 * Sq(Math.Sin(w0r)))),
                E = (1 + 3 * Sq(Math.Tan(w0r))) / (6 * Sq(n0)),
            };
        }

        /// <summary>Geodésica → PTL (fórmula direta da NBR 14166). Devolve (X, Y).</summary>
        public static (double X, double Y) ParaPtl(double lat, double lon)
        {
            Constantes k = Origem();
            double dls = (-lon - -Lon0) * 3600.0, dws = (lat - Lat0) * 3600.0;
            double dl1 = dls * (1 - KAs * dls * dls), dw1 = dws * (1 - KAs * dws * dws);
            double wr = Rad(lat);
            double np = A / Math.Sqrt(1 - E2 * Sq(Math.Sin(wr)));
            double x = -dl1 * Math.Cos(wr) * np * Arc1s * k.C;
            double y = ((dw1 + k.Cc * x * x + k.D * dw1 * dw1 + k.E * dw1 * x * x + k.E * k.Cc * Math.Pow(x, 4)) / k.B) * k.C;
            return (X0 + x, Y0 + y);
        }

        /// <summary>PTL → geodésica (aproximação inversa da NBR 14166 + 3 refinamentos, como no GeoDoc).</summary>
        public static (double Lat, double Lon) ParaGeodesica(double X, double Y)
        {
            Constantes k = Origem();
            double xt = X - X0, yt = Y - Y0;
            double kAs2 = Arc1s * Arc1s / 6;
            double x = xt / k.C, y = yt / k.C;
            double s = Math.Sqrt(x * x + y * y);
            double az = s == 0 ? 0 : ((Math.Atan2(x, y) + Math.PI) % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI);
            double delta = k.B * s * Math.Cos(az) + k.Cc * s * s * Sq(Math.Sin(az)) - k.B * k.E * Math.Pow(s, 3) * Sq(Math.Sin(az)) * Math.Cos(az);
            double f1 = -delta - k.D * delta * delta;
            double fs = f1 * (1 + kAs2 * f1 * f1);
            double lat = Lat0 + fs / 3600;
            double np0 = A / Math.Sqrt(1 - E2 * Sq(Math.Sin(Rad(lat))));
            double l1 = s * Math.Sin(az) / (Math.Cos(Rad(lat)) * np0 * Arc1s);
            double ls = l1 * (1 + kAs2 * l1 * l1);
            double lonW = -Lon0 + ls / 3600;

            for (int i = 0; i < 3; i++)
            {
                double p0 = lat, l0 = lonW;
                (double xx, double yy) = Direta(k, p0, l0);
                lat = p0 + (yt - yy) * k.B / k.C / 3600;
                double dlf = Math.Cos(Rad(lat)) * A / Math.Sqrt(1 - E2 * Sq(Math.Sin(Rad(lat)))) * Arc1s * k.C * 3600;
                lonW = l0 - (xt - xx) / dlf;
            }
            return (lat, -lonW);
        }

        /// <summary>Parte da fórmula direta usada nos refinamentos (lonW positiva para oeste).</summary>
        private static (double x, double y) Direta(Constantes k, double lat, double lonW)
        {
            double dp = (lat - Lat0) * 3600, dl = (lonW - -Lon0) * 3600;
            double dp1 = dp * (1 - KAs * dp * dp), dl1 = dl * (1 - KAs * dl * dl);
            double nn = A / Math.Sqrt(1 - E2 * Sq(Math.Sin(Rad(lat))));
            double xx = -dl1 * Math.Cos(Rad(lat)) * nn * Arc1s * k.C;
            double yy = (dp1 + k.Cc * xx * xx + k.D * dp1 * dp1 + k.E * dp1 * xx * xx + k.E * k.Cc * Math.Pow(xx, 4)) / k.B * k.C;
            return (xx, yy);
        }

        /// <summary>Distância em metros entre duas posições geodésicas próximas (aproximação local, suficiente para conferência).</summary>
        public static double DistanciaMetros(double lat1, double lon1, double lat2, double lon2)
        {
            double latMedia = Rad((lat1 + lat2) / 2);
            double m = A * (1 - E2) / Math.Pow(1 - E2 * Sq(Math.Sin(latMedia)), 1.5);
            double n = A / Math.Sqrt(1 - E2 * Sq(Math.Sin(latMedia)));
            double dN = Rad(lat2 - lat1) * m, dE = Rad(lon2 - lon1) * n * Math.Cos(latMedia);
            return Math.Sqrt(dN * dN + dE * dE);
        }

        // ID, X (PTL), Y (PTL), latitude, longitude: pontos de controle do autoteste do GeoDoc.
        private static readonly (string Id, double X, double Y, double Lat, double Lon)[] Controle =
        {
            ("AF-1", 167470.0125, 238469.1036, -23.654854110918, -46.5603589137578),
            ("AF-2", 167633.5255, 238451.8621, -23.6550079934194, -46.558756161912),
            ("AM-1", 160612.4177, 242268.4821, -23.6206127360268, -46.6275954142835),
            ("AM-2", 160617.3772, 242431.506, -23.6191409168946, -46.6275479823008),
            ("AV-1", 163011.1496, 239980.2118, -23.641253806306, -46.6040722363652),
            ("AV-2", 162976.7794, 240093.6977, -23.6402295293694, -46.6044100440739),
            ("CS-1", 161763.0841, 240650.4769, -23.6352121895543, -46.6163082098414),
            ("CS-2", 161803.381, 240558.4642, -23.6360425892177, -46.6159126005094),
            ("EACH", 173690.5932, 257656.3908, -23.4815507536476, -46.4996969031605),
            ("FC-1", 155221.9537, 248365.7701, -23.5655923244886, -46.6804333992393),
            ("FC-2", 155311.779, 248402.6312, -23.5652592467986, -46.6795537748232),
            ("GI-1", 154281.8179, 250063.7288, -23.5502657175468, -46.689646021009),
            ("GI-2", 154372.2211, 250117.1582, -23.54978310917, -46.6887608645324),
            ("HP-1", 155973.1983, 245124.3072, -23.5948539401738, -46.673062685105),
            ("HP-2", 155983.7363, 245221.6634, -23.593974962494, -46.6729598428296),
            ("IN-1", 158655.5333, 242794.1854, -23.6158783058732, -46.6467723437817),
            ("IN-2", 158763.8721, 242834.0337, -23.6155179707935, -46.6457110951801),
            ("JC-1", 155295.1707, 246116.7207, -23.5858967129604, -46.6797083309858),
            ("JC-2", 155272.6002, 246196.1338, -23.5851798385922, -46.6799297026569),
            ("LA-1", 152562.4783, 253467.4785, -23.5195398149809, -46.7064892299883),
            ("LA-2", 152469.2889, 253510.3358, -23.5191530369291, -46.7074016863853),
            ("LI-1", 164009.6959, 239472.0292, -23.6458333509877, -46.5942819881688),
            ("LI-2", 164004.2548, 239355.1302, -23.646888763708, -46.5943342105728),
            ("MO-1", 157127.7207, 244184.341, -23.6033353492381, -46.6617482084552),
            ("MO-2", 157050.3429, 244095.7123, -23.6041358332617, -46.6625058500062),
            ("Origem", 150000, 250000, -23.5508467222222, -46.7315775833333),
        };

        private static string _falhaAutoteste;
        private static bool _autotesteRodado;

        /// <summary>
        /// Confere as fórmulas com os 26 pontos do GeoDoc, nos dois sentidos, com tolerância de 0,1 mm.
        /// Devolve null se aprovado, ou a descrição da falha. Roda uma vez por sessão do Revit.
        /// </summary>
        public static string Autoteste()
        {
            if (_autotesteRodado) return _falhaAutoteste;
            const double tol = 0.0001;
            foreach (var p in Controle)
            {
                (double x, double y) = ParaPtl(p.Lat, p.Lon);
                double ida = Math.Sqrt(Sq(x - p.X) + Sq(y - p.Y));
                (double lat, double lon) = ParaGeodesica(p.X, p.Y);
                double volta = DistanciaMetros(lat, lon, p.Lat, p.Lon);
                if (!(ida <= tol && volta <= tol))
                {
                    _falhaAutoteste = $"ponto {p.Id}: diferença {Math.Max(ida, volta):0.000000} m (tolerância 0,0001 m)";
                    break;
                }
            }
            _autotesteRodado = true;
            return _falhaAutoteste;
        }
    }
}
