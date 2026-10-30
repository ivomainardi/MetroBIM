using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace MetroSP.Ids
{
    /// <summary>
    /// Lat/long do Project Base Point para o IfcSite: lê o PBP em coordenadas compartilhadas (PTL do Metrô),
    /// converte com <see cref="PtlMetroSP"/> e grava em Manage › Location (SiteLocation da localização ativa).
    /// Não mexe no Survey Point, no Project Base Point, nas coordenadas compartilhadas nem no norte verdadeiro.
    /// </summary>
    public class LocalizacaoIfc
    {
        /// <summary>Abaixo disso (m) o Location já é considerado correto e não é regravado.</summary>
        public const double ToleranciaLocation = 0.01;

        public string NomeLocalizacao { get; private set; }
        /// <summary>E/W, N/S e elevação do Project Base Point, em metros, nas coordenadas compartilhadas.</summary>
        public double Este { get; private set; }
        public double Norte { get; private set; }
        public double Elevacao { get; private set; }
        public double Latitude { get; private set; }
        public double Longitude { get; private set; }
        /// <summary>Ida e volta PTL → geodésica → PTL (m).</summary>
        public double Fechamento { get; private set; }
        public double RaioOrigem { get; private set; }
        public double LatitudeAtual { get; private set; }
        public double LongitudeAtual { get; private set; }
        /// <summary>Distância entre o Location atual e o calculado (m).</summary>
        public double Diferenca { get; private set; }
        /// <summary>Motivo que impede a correção (e a exportação), ou null.</summary>
        public string Erro { get; private set; }

        public bool PrecisaAtualizar => Erro == null && Diferenca > ToleranciaLocation;
        public string LinkMapa => "https://www.openstreetmap.org/?mlat=" + Num(Latitude, 8) + "&mlon=" + Num(Longitude, 8) + "#map=18/" + Num(Latitude, 8) + "/" + Num(Longitude, 8);

        public static LocalizacaoIfc Calcular(Document doc)
        {
            var r = new LocalizacaoIfc();
            ProjectLocation ativa = doc.ActiveProjectLocation;
            SiteLocation site = ativa?.GetSiteLocation();
            r.NomeLocalizacao = ativa?.Name;
            if (site == null) return r.Falha("o modelo não tem Location (Manage › Location).");
            r.LatitudeAtual = site.Latitude * 180 / Math.PI;
            r.LongitudeAtual = site.Longitude * 180 / Math.PI;

            // Com um sistema GIS no Location, o exportador preenche sozinho o EPSG (IfcProjectedCRS/IfcMapConversion).
            if (!string.IsNullOrEmpty(site.GeoCoordinateSystemId))
                return r.Falha($"o Location do modelo tem o sistema de coordenadas GIS \"{site.GeoCoordinateSystemId}\". " +
                    "Com ele o exportador IFC preenche o EPSG automaticamente, e as coordenadas do modelo estão no PTL.");

            string falhaMotor = PtlMetroSP.Autoteste();
            if (falhaMotor != null) return r.Falha("o autoteste da conversão PTL reprovou (" + falhaMotor + ").");

            BasePoint pbp = BasePoint.GetProjectBasePoint(doc);
            if (pbp == null) return r.Falha("o modelo não tem Project Base Point.");
            XYZ p = pbp.SharedPosition;
            r.Este = UnitUtils.ConvertFromInternalUnits(p.X, UnitTypeId.Meters);
            r.Norte = UnitUtils.ConvertFromInternalUnits(p.Y, UnitTypeId.Meters);
            r.Elevacao = UnitUtils.ConvertFromInternalUnits(p.Z, UnitTypeId.Meters);

            if (!Finito(r.Este) || !Finito(r.Norte)) return r.Falha("as coordenadas do Project Base Point não são numéricas.");
            r.RaioOrigem = Math.Sqrt(Math.Pow(r.Este - PtlMetroSP.X0, 2) + Math.Pow(r.Norte - PtlMetroSP.Y0, 2));
            if (r.RaioOrigem > PtlMetroSP.RaioRecomendado)
                return r.Falha($"o Project Base Point (E {Num(r.Este, 3)}, N {Num(r.Norte, 3)}) está a {Num(r.RaioOrigem / 1000, 1)} km da origem do PTL, " +
                    "fora da área do PTL do Metrô (50 km). Confira se o modelo recebeu as coordenadas compartilhadas (Acquire Coordinates).");

            (double lat, double lon) = PtlMetroSP.ParaGeodesica(r.Este, r.Norte);
            r.Latitude = lat;
            r.Longitude = lon;
            if (!Finito(lat) || !Finito(lon) || Math.Abs(lat) > 90 || Math.Abs(lon) > 180)
                return r.Falha("a conversão não deu uma latitude/longitude válida.");
            if (lat >= 0 || lon >= 0)
                return r.Falha($"a conversão deu {Num(lat, 6)}, {Num(lon, 6)}, fora dos hemisférios sul e oeste.");

            (double x, double y) = PtlMetroSP.ParaPtl(lat, lon);
            r.Fechamento = Math.Sqrt(Math.Pow(x - r.Este, 2) + Math.Pow(y - r.Norte, 2));
            if (r.Fechamento > 0.0001)
                return r.Falha($"a volta da conversão não fechou ({Num(r.Fechamento, 6)} m).");

            r.Diferenca = PtlMetroSP.DistanciaMetros(r.LatitudeAtual, r.LongitudeAtual, lat, lon);
            return r;
        }

        /// <summary>Grava a lat/long no Location numa transação própria, que fica no modelo (e pode ser desfeita com Ctrl+Z).</summary>
        public void Aplicar(Document doc)
        {
            using (var t = new Transaction(doc, "MetroSP: Location pelo Project Base Point"))
            {
                t.Start();
                SiteLocation site = doc.ActiveProjectLocation.GetSiteLocation();
                site.Latitude = Latitude * Math.PI / 180;
                site.Longitude = Longitude * Math.PI / 180;
                if (t.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException("o Revit não aceitou a alteração do Location.");
            }
        }

        /// <summary>Texto para a confirmação e para o log.</summary>
        public string Resumo()
        {
            return $"Project Base Point (PTL, localização \"{NomeLocalizacao}\"): E {Num(Este, 3)} m, N {Num(Norte, 3)} m, elevação {Num(Elevacao, 3)} m\n" +
                $"Latitude/longitude calculadas (SIRGAS 2000): {Num(Latitude, 8)}, {Num(Longitude, 8)}  ({Gms(Latitude, "S", "N")}, {Gms(Longitude, "W", "E")})\n" +
                $"Location atual: {Num(LatitudeAtual, 8)}, {Num(LongitudeAtual, 8)}  (diferença de {Distancia(Diferenca)})\n" +
                $"Distância à origem do PTL: {Num(RaioOrigem / 1000, 3)} km · fechamento da conversão: {Num(Fechamento * 1000, 4)} mm\n" +
                $"Motor: {PtlMetroSP.Identificacao}";
        }

        // ---- Conferência do IFC exportado ----

        /// <summary>Resultado da leitura do IFC: linhas de conferência e se algo saiu errado.</summary>
        public class Conferencia
        {
            public List<string> Linhas { get; } = new List<string>();
            public bool TemProblema { get; set; }
            public override string ToString() => string.Join("\n", Linhas);
        }

        private static readonly Regex RxSchema = new Regex(@"FILE_SCHEMA\s*\(\s*\(\s*'([^']*)'", RegexOptions.IgnoreCase);
        private static readonly Regex RxSite = new Regex(@"=\s*IFCSITE\s*\((.*?)\)\s*;", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        private static readonly Regex RxTupla = new Regex(@"\(\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*(?:,\s*(-?\d+)\s*)?\)");

        /// <summary>Lê o .ifc como texto e confere esquema, RefLatitude/RefLongitude do IfcSite e a ausência de EPSG.</summary>
        public Conferencia ConferirIfc(string arquivo)
        {
            var c = new Conferencia();
            string texto;
            try { texto = File.ReadAllText(arquivo); }
            catch (Exception ex)
            {
                c.Linhas.Add("Não foi possível ler o IFC para conferir: " + ex.Message);
                c.TemProblema = true;
                return c;
            }

            Match schema = RxSchema.Match(texto);
            string esquema = schema.Success ? schema.Groups[1].Value : "?";
            bool ifc4 = esquema.Equals("IFC4", StringComparison.OrdinalIgnoreCase);
            c.Linhas.Add("Esquema: " + esquema + (ifc4 ? "" : " (esperado IFC4)"));
            if (!ifc4) c.TemProblema = true;

            Match site = RxSite.Match(texto);
            if (!site.Success)
            {
                c.Linhas.Add("IfcSite não encontrado no arquivo.");
                c.TemProblema = true;
            }
            else
            {
                MatchCollection tuplas = RxTupla.Matches(site.Groups[1].Value);
                if (tuplas.Count < 2)
                {
                    c.Linhas.Add("IfcSite sem RefLatitude/RefLongitude.");
                    c.TemProblema = true;
                }
                else
                {
                    double lat = DeTupla(tuplas[0]), lon = DeTupla(tuplas[1]);
                    double dif = PtlMetroSP.DistanciaMetros(lat, lon, Latitude, Longitude);
                    // O exportador trunca em milionésimos de segundo (~3 cm)
                    bool ok = dif <= 0.05 && lat < 0 && lon < 0;
                    c.Linhas.Add($"IfcSite RefLatitude/RefLongitude: {Num(lat, 8)}, {Num(lon, 8)} ({(ok ? "confere" : "NÃO confere")} com o PBP, diferença de {Distancia(dif)})");
                    if (!ok) c.TemProblema = true;
                }
            }

            bool crs = texto.IndexOf("IFCPROJECTEDCRS", StringComparison.OrdinalIgnoreCase) >= 0;
            bool mapa = texto.IndexOf("IFCMAPCONVERSION", StringComparison.OrdinalIgnoreCase) >= 0;
            bool epsg = texto.IndexOf("'EPSG:", StringComparison.OrdinalIgnoreCase) >= 0;
            if (crs || mapa || epsg)
            {
                c.Linhas.Add("Atenção: o IFC tem " + string.Join(", ", new[] { crs ? "IfcProjectedCRS" : null, mapa ? "IfcMapConversion" : null, epsg ? "código EPSG" : null }.Where(s => s != null)) +
                    ", mas as coordenadas estão no PTL.");
                c.TemProblema = true;
            }
            else c.Linhas.Add("Sem EPSG, IfcProjectedCRS ou IfcMapConversion (coordenadas no PTL).");
            return c;
        }

        /// <summary>(graus, minutos, segundos, milionésimos), todos com o mesmo sinal.</summary>
        private static double DeTupla(Match m)
        {
            double v = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)
                + double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / 60
                + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) / 3600;
            if (m.Groups[4].Success) v += double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) / 3600e6;
            return v;
        }

        // ---- Log ----

        /// <summary>Grava &lt;arquivo&gt;.ifc.log.txt ao lado do IFC. Devolve o caminho, ou null se não conseguiu.</summary>
        public string GravarLog(Document doc, string arquivoIfc, string setup, bool atualizou, Conferencia conferencia, string revit, string plugin)
        {
            var sb = new StringBuilder();
            sb.AppendLine("MetroSP · exportação IFC · registro do Location");
            sb.AppendLine("Data: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("Usuário: " + Environment.UserName);
            sb.AppendLine("Modelo: " + (string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName));
            sb.AppendLine("Revit: " + revit + " · MetroSP " + plugin);
            sb.AppendLine("Setup IFC: " + setup + " · base Shared Coordinates · EPSG em branco");
            sb.AppendLine("IFC: " + arquivoIfc);
            sb.AppendLine();
            sb.AppendLine(Resumo());
            sb.AppendLine(atualizou ? "Location atualizado antes da exportação." : "Location já estava correto; não foi alterado.");
            sb.AppendLine();
            sb.AppendLine("Conferência do IFC:");
            sb.AppendLine(conferencia?.ToString() ?? "não feita");
            string log = arquivoIfc + ".log.txt";
            try
            {
                File.WriteAllText(log, sb.ToString(), Encoding.UTF8);
                return log;
            }
            catch { return null; }
        }

        // ---- Apoio ----

        private LocalizacaoIfc Falha(string motivo)
        {
            Erro = motivo;
            return this;
        }

        private static bool Finito(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        private static string Num(double v, int casas) => v.ToString("F" + casas, CultureInfo.InvariantCulture);

        private static string Distancia(double m) => m < 1 ? Num(m * 100, 1) + " cm" : m < 1000 ? Num(m, 2) + " m" : Num(m / 1000, 2) + " km";

        private static string Gms(double graus, string negativo, string positivo)
        {
            double a = Math.Abs(graus);
            int g = (int)a;
            double mf = (a - g) * 60;
            int m = (int)mf;
            double s = (mf - m) * 60;
            return $"{g}°{m:00}'{s.ToString("00.0000", CultureInfo.InvariantCulture)}\"{(graus < 0 ? negativo : positivo)}";
        }
    }
}
