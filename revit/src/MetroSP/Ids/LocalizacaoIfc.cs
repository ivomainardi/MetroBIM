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
        /// <summary>Endereço do projeto em Manage › Location (vai para o endereço do IfcSite/IfcBuilding).</summary>
        public string LocalAtual { get; private set; }
        /// <summary>Informações do projeto › Nome do edifício (IfcBuilding.Name).</summary>
        public string EdificioAtual { get; private set; }
        /// <summary>Informações do projeto › Endereço (IfcPostalAddress do IfcBuilding).</summary>
        public string EnderecoAtual { get; private set; }
        /// <summary>Informações do projeto › SiteName (o exportador usa como IfcSite.Name), ou vazio.</summary>
        public string SiteAtual { get; private set; }
        /// <summary>Sistema de coordenadas GIS do Location (ex.: SA-SIR-23S), ou null. Com ele o exportador grava EPSG.</summary>
        public string SistemaGis { get; private set; }
        /// <summary>Motivo que impede a correção (e a exportação), ou null.</summary>
        public string Erro { get; private set; }

        /// <summary>Parâmetro de Informações do projeto que o exportador IFC lê para o nome do IfcSite.</summary>
        public const string ParametroSite = "SiteName";
        private static readonly Guid GuidSiteName = new Guid("5b3c1f4e-8d2a-4c7e-9f61-0a2d6e4b7c19");

        public static string LinkMapa(double lat, double lon) =>
            "https://www.openstreetmap.org/?mlat=" + Num(lat, 8) + "&mlon=" + Num(lon, 8) + "#map=18/" + Num(lat, 8) + "/" + Num(lon, 8);

        /// <summary>Valores de localização confirmados na janela, que vão para o IFC e para os campos do Revit.</summary>
        public class Escolha
        {
            public double Latitude { get; set; }
            public double Longitude { get; set; }
            public string Local { get; set; }
            public string Site { get; set; }
            public string Edificio { get; set; }
            public string Endereco { get; set; }
        }

        /// <summary>Valores iniciais da janela: lat/long calculada pelo PBP e os textos atuais do Revit.</summary>
        public Escolha Proposta() => new Escolha
        {
            Latitude = Erro == null ? Latitude : LatitudeAtual,
            Longitude = Erro == null ? Longitude : LongitudeAtual,
            Local = LocalAtual,
            Site = SiteAtual,
            Edificio = EdificioAtual,
            Endereco = EnderecoAtual,
        };

        /// <summary>Valores que estão hoje nos campos do Revit (o que o exportador vai usar).</summary>
        public Escolha Atual() => new Escolha
        {
            Latitude = LatitudeAtual,
            Longitude = LongitudeAtual,
            Local = LocalAtual,
            Site = SiteAtual,
            Edificio = EdificioAtual,
            Endereco = EnderecoAtual,
        };

        /// <summary>O Location do Revit já está no ponto do Project Base Point.</summary>
        public bool LocationCorreto => Erro == null && Diferenca <= ToleranciaLocation;

        public static LocalizacaoIfc Calcular(Document doc)
        {
            var r = new LocalizacaoIfc();
            ProjectLocation ativa = doc.ActiveProjectLocation;
            SiteLocation site = ativa?.GetSiteLocation();
            r.NomeLocalizacao = ativa?.Name;
            ProjectInfo info = doc.ProjectInformation;
            try { r.EdificioAtual = info?.BuildingName ?? string.Empty; }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException) { r.EdificioAtual = string.Empty; }
            r.EnderecoAtual = info?.Address ?? string.Empty;
            r.SiteAtual = info?.LookupParameter(ParametroSite)?.AsString() ?? string.Empty;
            if (site == null) return r.Falha("o modelo não tem Location (Manage › Location).");
            r.LatitudeAtual = site.Latitude * 180 / Math.PI;
            r.LongitudeAtual = site.Longitude * 180 / Math.PI;
            r.LocalAtual = site.PlaceName ?? string.Empty;

            // Com um sistema GIS no Location, o exportador preenche sozinho o EPSG (IfcProjectedCRS/IfcMapConversion).
            // Não impede o cálculo: a janela pede para ignorar e a MetroSP tira essas entidades do IFC depois.
            r.SistemaGis = string.IsNullOrEmpty(site.GeoCoordinateSystemId) ? null : site.GeoCoordinateSystemId;

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

        /// <summary>
        /// Grava nos campos padrão do Revit o que difere da escolha: lat/long e endereço em Manage › Location,
        /// nome do edifício e endereço em Informações do projeto. Transação própria, que fica no modelo
        /// (pode ser desfeita com Ctrl+Z). Devolve o que mudou, vazio se nada mudou.
        /// </summary>
        public List<string> Aplicar(Document doc, Escolha e)
        {
            var mudou = new List<string>();
            bool latLong = PtlMetroSP.DistanciaMetros(LatitudeAtual, LongitudeAtual, e.Latitude, e.Longitude) > ToleranciaLocation;
            bool local = !MesmoTexto(LocalAtual, e.Local);
            bool siteNome = !MesmoTexto(SiteAtual, e.Site);
            bool edificio = !MesmoTexto(EdificioAtual, e.Edificio);
            bool endereco = !MesmoTexto(EnderecoAtual, e.Endereco);
            if (!latLong && !local && !siteNome && !edificio && !endereco) return mudou;

            using (var t = new Transaction(doc, "MetroSP: localização para o IFC"))
            {
                t.Start();
                SiteLocation site = doc.ActiveProjectLocation.GetSiteLocation();
                if (latLong)
                {
                    site.Latitude = e.Latitude * Math.PI / 180;
                    site.Longitude = e.Longitude * Math.PI / 180;
                    mudou.Add($"Location: latitude/longitude {Num(e.Latitude, 8)}, {Num(e.Longitude, 8)}");
                }
                if (local)
                {
                    site.PlaceName = e.Local ?? string.Empty;
                    mudou.Add("Location: endereço do projeto \"" + e.Local + "\"");
                }
                if (siteNome)
                {
                    Parameter par = ParametroSiteName(doc);
                    if (par == null || par.IsReadOnly || !par.Set(e.Site ?? string.Empty))
                        throw new InvalidOperationException($"não foi possível gravar o nome do IfcSite no parâmetro {ParametroSite} das Informações do projeto.");
                    mudou.Add($"Informações do projeto: {ParametroSite} (nome do IfcSite) \"" + e.Site + "\"");
                }
                if (edificio)
                {
                    doc.ProjectInformation.BuildingName = e.Edificio ?? string.Empty;
                    mudou.Add("Informações do projeto: nome do edifício \"" + e.Edificio + "\"");
                }
                if (endereco)
                {
                    doc.ProjectInformation.Address = e.Endereco ?? string.Empty;
                    mudou.Add("Informações do projeto: endereço \"" + e.Endereco + "\"");
                }
                if (t.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException("o Revit não aceitou a alteração.");
            }
            if (latLong)
            {
                // Com sistema GIS no Location, o Revit pode ignorar a lat/long sem dar erro: confere o que ficou.
                SiteLocation gravado = doc.ActiveProjectLocation.GetSiteLocation();
                double latG = gravado.Latitude * 180 / Math.PI, lonG = gravado.Longitude * 180 / Math.PI;
                double resto = PtlMetroSP.DistanciaMetros(latG, lonG, e.Latitude, e.Longitude);
                if (resto > ToleranciaLocation)
                {
                    mudou.RemoveAll(m => m.StartsWith("Location: latitude/longitude", StringComparison.Ordinal));
                    mudou.Add($"ATENÇÃO: o Revit não aceitou a latitude/longitude no Location (ficou {Num(latG, 8)}, {Num(lonG, 8)}" +
                        (string.IsNullOrEmpty(gravado.GeoCoordinateSystemId) ? "" : $"; o sistema GIS \"{gravado.GeoCoordinateSystemId}\" trava esses campos") +
                        "). Na exportação, a MetroSP grava no IfcSite a latitude/longitude do Project Base Point.");
                }
            }
            return mudou;
        }

        /// <summary>
        /// Parâmetro SiteName das Informações do projeto. Se o modelo não tem, cria como parâmetro compartilhado
        /// de texto (grupo IFC) a partir de um arquivo temporário. Precisa de transação aberta.
        /// </summary>
        private static Parameter ParametroSiteName(Document doc)
        {
            ProjectInfo info = doc.ProjectInformation;
            Parameter p = info.LookupParameter(ParametroSite);
            if (p != null) return p;

            Autodesk.Revit.ApplicationServices.Application app = doc.Application;
            string original = app.SharedParametersFilename;
            string temp = Path.Combine(Path.GetTempPath(), "MetroSP_SiteName.txt");
            try
            {
                File.WriteAllText(temp, string.Empty);
                app.SharedParametersFilename = temp;
                DefinitionGroup grupo = app.OpenSharedParameterFile().Groups.Create("MetroSP");
                var opcoes = new ExternalDefinitionCreationOptions(ParametroSite, SpecTypeId.String.Text)
                {
                    GUID = GuidSiteName,
                    Description = "Nome do IfcSite na exportação IFC (lido pelo exportador da Autodesk).",
                };
                Definition def = grupo.Definitions.Create(opcoes);
                var cats = app.Create.NewCategorySet();
                cats.Insert(Category.GetCategory(doc, BuiltInCategory.OST_ProjectInformation));
                doc.ParameterBindings.Insert(def, app.Create.NewInstanceBinding(cats), GroupTypeId.Ifc);
            }
            finally
            {
                try { app.SharedParametersFilename = original ?? ""; } catch { }
                try { File.Delete(temp); } catch { }
            }
            return info.LookupParameter(ParametroSite);
        }

        private static bool MesmoTexto(string a, string b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.Ordinal);

        /// <summary>Texto para a confirmação e para o log.</summary>
        public string Resumo(Escolha e)
        {
            double desvio = PtlMetroSP.DistanciaMetros(Latitude, Longitude, e.Latitude, e.Longitude);
            return $"Project Base Point (PTL, localização \"{NomeLocalizacao}\"): E {Num(Este, 3)} m, N {Num(Norte, 3)} m, elevação {Num(Elevacao, 3)} m\n" +
                $"Latitude/longitude calculadas (SIRGAS 2000): {Num(Latitude, 8)}, {Num(Longitude, 8)}  ({Gms(Latitude, "S", "N")}, {Gms(Longitude, "W", "E")})\n" +
                $"Distância à origem do PTL: {Num(RaioOrigem / 1000, 3)} km · fechamento da conversão: {Num(Fechamento * 1000, 4)} mm\n" +
                $"Motor: {PtlMetroSP.Identificacao}\n" +
                $"Location antes da exportação: {Num(LatitudeAtual, 8)}, {Num(LongitudeAtual, 8)}  (diferença de {Distancia(Diferenca)} do calculado)\n" +
                $"IfcSite: {Num(e.Latitude, 8)}, {Num(e.Longitude, 8)}" +
                (desvio > ToleranciaLocation ? $"  (a {Distancia(desvio)} do Project Base Point)" : "") +
                $" · nome \"{e.Site}\" · endereço do projeto \"{e.Local}\"\n" +
                $"IfcBuilding: nome \"{e.Edificio}\" · endereço \"{e.Endereco}\"";
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
        public Conferencia ConferirIfc(string arquivo, Escolha e)
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
                    double dif = PtlMetroSP.DistanciaMetros(lat, lon, e.Latitude, e.Longitude);
                    // O exportador trunca em milionésimos de segundo (~3 cm)
                    bool ok = dif <= 0.05 && lat < 0 && lon < 0;
                    c.Linhas.Add($"IfcSite RefLatitude/RefLongitude: {Num(lat, 8)}, {Num(lon, 8)} ({(ok ? "confere" : "NÃO confere")} com o Location, diferença de {Distancia(dif)})");
                    if (!ok) c.TemProblema = true;
                }
            }

            Match nomeSite = RxNomeSite.Match(texto);
            if (nomeSite.Success && !string.IsNullOrWhiteSpace(e.Site))
            {
                string nome = TextoIfc(nomeSite.Groups[1].Value);
                bool ok = MesmoTexto(nome, e.Site);
                c.Linhas.Add($"IfcSite Name: \"{nome}\"" + (ok ? "" : $" (esperado \"{e.Site}\")"));
                if (!ok) c.TemProblema = true;
            }

            Match edificio = RxEdificio.Match(texto);
            if (!edificio.Success) c.Linhas.Add("IfcBuilding não encontrado no arquivo.");
            else
            {
                string nome = TextoIfc(edificio.Groups[1].Value);
                bool ok = MesmoTexto(nome, e.Edificio) || (string.IsNullOrWhiteSpace(e.Edificio) && string.IsNullOrEmpty(nome));
                c.Linhas.Add($"IfcBuilding Name: \"{nome}\"" + (ok ? "" : $" (esperado \"{e.Edificio}\")"));
                if (!ok) c.TemProblema = true;
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

        /// <summary>Nome (3º atributo) do IfcBuilding: string IFC ou $.</summary>
        private static readonly Regex RxEdificio = new Regex(@"=\s*IFCBUILDING\s*\(\s*'[^']*'\s*,\s*[^,]*,\s*('(?:[^']|'')*'|\$)", RegexOptions.IgnoreCase);
        private static readonly Regex RxNomeSite = new Regex(@"=\s*IFCSITE\s*\(\s*'[^']*'\s*,\s*[^,]*,\s*('(?:[^']|'')*'|\$)", RegexOptions.IgnoreCase);
        private static readonly Regex RxX2 = new Regex(@"\\X2\\((?:[0-9A-Fa-f]{4})+)\\X0\\");
        private static readonly Regex RxX = new Regex(@"\\X\\([0-9A-Fa-f]{2})");

        /// <summary>Decodifica uma string STEP ('...' com '' e \X2\...\X0\ / \X\hh); $ vira vazio.</summary>
        private static string TextoIfc(string bruto)
        {
            if (bruto == "$" || bruto.Length < 2) return string.Empty;
            string t = bruto.Substring(1, bruto.Length - 2).Replace("''", "'");
            t = RxX2.Replace(t, m =>
            {
                string hex = m.Groups[1].Value;
                var sb = new StringBuilder();
                for (int i = 0; i < hex.Length; i += 4) sb.Append((char)Convert.ToInt32(hex.Substring(i, 4), 16));
                return sb.ToString();
            });
            t = RxX.Replace(t, m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
            return t.Replace("\\\\", "\\");
        }

        private static readonly Regex RxGeorref = new Regex(@"^#(\d+)\s*=\s*IFC(MAPCONVERSION|PROJECTEDCRS)\s*\(.*?\)\s*;[ \t]*\r?\n", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);

        /// <summary>
        /// Tira do IFC as entidades IfcMapConversion e IfcProjectedCRS (que trazem o EPSG), já que as coordenadas
        /// estão no PTL. Só remove se nenhuma outra entidade aponta para elas. Devolve o que fez, ou null se não havia nada.
        /// </summary>
        public static string RemoverGeorreferencia(string arquivo)
        {
            Encoding latin1 = Encoding.GetEncoding(28591); // lê e grava os bytes do STEP sem alterar nada
            string texto = File.ReadAllText(arquivo, latin1);
            MatchCollection achadas = RxGeorref.Matches(texto);
            if (achadas.Count == 0) return null;

            string sem = RxGeorref.Replace(texto, string.Empty);
            foreach (Match m in achadas)
            {
                if (Regex.IsMatch(sem, "#" + m.Groups[1].Value + @"(?!\d)"))
                    return "O IFC tem IfcMapConversion/IfcProjectedCRS, mas outras entidades apontam para eles; não foram removidos.";
            }
            File.WriteAllText(arquivo, sem, latin1);
            return $"Removidos do IFC: {string.Join(", ", achadas.Cast<Match>().Select(m => m.Groups[2].Value.Equals("MAPCONVERSION", StringComparison.OrdinalIgnoreCase) ? "IfcMapConversion" : "IfcProjectedCRS"))} " +
                "(o exportador os criou pelo sistema GIS do Location; as coordenadas estão no PTL).";
        }

        /// <summary>
        /// Grava no IfcSite do arquivo a RefLatitude/RefLongitude calculada pelo Project Base Point, se o exportador
        /// saiu com outra (Location do Revit errado ou travado pelo sistema GIS). Devolve o que fez, ou null se já conferia.
        /// </summary>
        public string CorrigirLatLongIfc(string arquivo)
        {
            if (Erro != null) return null;
            Encoding latin1 = Encoding.GetEncoding(28591);
            string texto = File.ReadAllText(arquivo, latin1);
            Match site = RxSite.Match(texto);
            if (!site.Success) return null;
            System.Text.RegularExpressions.Group args = site.Groups[1];
            MatchCollection tuplas = RxTupla.Matches(args.Value);
            if (tuplas.Count < 2) return null;
            double latIfc = DeTupla(tuplas[0]), lonIfc = DeTupla(tuplas[1]);
            double dif = PtlMetroSP.DistanciaMetros(latIfc, lonIfc, Latitude, Longitude);
            if (dif <= 0.05) return null;

            // Troca só as duas primeiras tuplas do IfcSite (RefLatitude e RefLongitude), de trás para frente.
            var sb = new StringBuilder(texto);
            sb.Remove(args.Index + tuplas[1].Index, tuplas[1].Length).Insert(args.Index + tuplas[1].Index, ParaTupla(Longitude));
            sb.Remove(args.Index + tuplas[0].Index, tuplas[0].Length).Insert(args.Index + tuplas[0].Index, ParaTupla(Latitude));
            File.WriteAllText(arquivo, sb.ToString(), latin1);
            return $"IfcSite RefLatitude/RefLongitude corrigida no IFC para {Num(Latitude, 8)}, {Num(Longitude, 8)} (Project Base Point); " +
                $"o exportador tinha gravado {Num(latIfc, 6)}, {Num(lonIfc, 6)}, que é o que está no Location do Revit.";
        }

        /// <summary>Graus decimais → (graus, minutos, segundos, milionésimos) com o mesmo sinal, como o exportador grava.</summary>
        private static string ParaTupla(double graus)
        {
            int sinal = graus < 0 ? -1 : 1;
            long micro = (long)Math.Round(Math.Abs(graus) * 3600e6);
            long g = micro / 3600000000L; micro -= g * 3600000000L;
            long m = micro / 60000000L; micro -= m * 60000000L;
            long sec = micro / 1000000L; micro -= sec * 1000000L;
            string C(long v) => (v == 0 ? 0 : sinal * v).ToString(CultureInfo.InvariantCulture);
            return $"({C(g)},{C(m)},{C(sec)},{C(micro)})";
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
        public string GravarLog(Document doc, string arquivoIfc, string setup, Escolha e, List<string> alteracoes, Conferencia conferencia, string revit, string plugin)
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
            sb.AppendLine(Resumo(e));
            sb.AppendLine();
            sb.AppendLine(alteracoes.Count == 0 ? "Nenhum campo do Revit foi alterado nesta exportação." : "Gravado no Revit nesta exportação:");
            foreach (string a in alteracoes) sb.AppendLine("- " + a);
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
