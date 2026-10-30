using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MetroSP.Ui;

namespace MetroSP.Ids
{
    /// <summary>
    /// Conteúdo da janela Localização IFC: o Project Base Point e o Location com selos, e os campos do IfcSite
    /// (nome, latitude, longitude, endereço) e do IfcBuilding (nome, endereço), já com a lat/long calculada pelo PBP.
    /// O que o usuário deixar diferente do Revit é gravado nos campos padrão.
    /// </summary>
    public class LocalizacaoPainel : StackPanel
    {
        private readonly LocalizacaoIfc _local;
        private readonly TextBox _latitude;
        private readonly TextBox _longitude;
        private readonly TextBox _endereco;
        private readonly TextBox _site;
        private readonly TextBox _edificio;
        private readonly TextBox _enderecoEdificio;
        private readonly Border _seloPbp;
        private readonly Border _seloLocation;
        private readonly TextBlock _location;
        private readonly Button _usarPbp;

        /// <summary>Algum campo mudou: a janela atualiza o resumo do rodapé.</summary>
        public event Action Mudou;

        public LocalizacaoPainel(LocalizacaoIfc local)
        {
            _local = local;
            LocalizacaoIfc.Escolha p = local.Proposta();

            // ---------- Referência: Project Base Point, Location atual e sistema GIS ----------
            Children.Add(Theme.Secao("Referência"));
            Border grupo = Theme.Grupo(out StackPanel itens);
            Children.Add(grupo);

            var pbp = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
            _seloPbp = Theme.Selo();
            if (local.Erro != null)
            {
                pbp.Text = "—";
                Theme.MudarSelo(_seloPbp, "Não calculado", Theme.Estado.Aviso, local.Erro);
            }
            else
            {
                pbp.Text = $"E {Num(local.Este, 3)} · N {Num(local.Norte, 3)} · elev. {Num(local.Elevacao, 3)} m";
                Theme.MudarSelo(_seloPbp, "PTL Metrô", Theme.Estado.Ok,
                    $"Latitude/longitude calculadas: {Num(local.Latitude, 8)}, {Num(local.Longitude, 8)}\n" +
                    $"Distância à origem do PTL: {Num(local.RaioOrigem / 1000, 3)} km · fechamento: {Num(local.Fechamento * 1000, 4)} mm\n" +
                    PtlMetroSP.Identificacao);
            }
            itens.Children.Add(Theme.Item("Base Point", pbp, _seloPbp, null, primeiro: true));

            _location = new TextBlock { Text = $"{Num(local.LatitudeAtual, 8)}, {Num(local.LongitudeAtual, 8)}", TextTrimming = TextTrimming.CharacterEllipsis };
            _seloLocation = Theme.Selo();
            var mapa = Theme.Link("Ver no mapa");
            mapa.Click += (s, e) => AbrirMapa();
            itens.Children.Add(Theme.Item("Location", _location, _seloLocation, mapa));

            if (local.SistemaGis != null)
            {
                var gis = new TextBlock { Text = local.SistemaGis, TextTrimming = TextTrimming.CharacterEllipsis };
                Border seloGis = Theme.Selo();
                Theme.MudarSelo(seloGis, "Ignorar na exportação", Theme.Estado.Neutro,
                    "O Location tem um sistema de coordenadas GIS. Ele não muda o cálculo pelo Project Base Point. " +
                    "Com ele o exportador IFC grava um EPSG; no Exportar IFC há a opção de ignorá-lo, para o IFC sair sem EPSG.");
                itens.Children.Add(Theme.Item("Sistema GIS", gis, seloGis, null));
            }

            // ---------- IfcSite à esquerda, IfcBuilding à direita ----------
            var colunas = new Grid();
            colunas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            colunas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            colunas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Children.Add(colunas);

            StackPanel site = Coluna(colunas, 0, "IfcSite",
                "Nome: parâmetro SiteName das Informações do projeto (criado se o modelo não tiver). " +
                "Latitude, longitude e endereço: Manage › Location.");
            _site = Campo(site, "Nome", p.Site);
            var latLong = new Grid();
            latLong.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            latLong.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            latLong.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lat = new StackPanel();
            var lon = new StackPanel();
            Grid.SetColumn(lon, 2);
            latLong.Children.Add(lat);
            latLong.Children.Add(lon);
            site.Children.Add(latLong);
            _latitude = Campo(lat, "Latitude", Num(p.Latitude, 8));
            _longitude = Campo(lon, "Longitude", Num(p.Longitude, 8));
            _usarPbp = Theme.Link("Usar o Project Base Point");
            _usarPbp.HorizontalAlignment = HorizontalAlignment.Left;
            _usarPbp.Margin = new Thickness(0, 4, 0, 0);
            _usarPbp.Click += (s, e) =>
            {
                _latitude.Text = Num(_local.Latitude, 8);
                _longitude.Text = Num(_local.Longitude, 8);
            };
            site.Children.Add(_usarPbp);
            _endereco = Campo(site, "Endereço", p.Local);

            StackPanel edificio = Coluna(colunas, 2, "IfcBuilding", "Nome do edifício e endereço: Informações do projeto.");
            _edificio = Campo(edificio, "Nome", p.Edificio);
            _enderecoEdificio = Campo(edificio, "Endereço", p.Endereco);

            foreach (TextBox t in new[] { _latitude, _longitude, _endereco, _site, _edificio, _enderecoEdificio })
                t.TextChanged += (s, e) => Atualizar();
            Atualizar();
        }

        /// <summary>Avisos para o resumo do rodapé (vazio se está tudo certo).</summary>
        public List<string> Avisos()
        {
            var a = new List<string>();
            if (_local.Erro != null) { a.Add("lat/long não calculada pelo Project Base Point"); return a; }
            if (!Ler(_latitude.Text, out double lat) || !Ler(_longitude.Text, out double lon)) { a.Add("latitude/longitude inválidas"); return a; }
            double? desvio = DesvioDoPbp();
            if (desvio > LocalizacaoIfc.ToleranciaLocation) a.Add($"lat/long a {Distancia(desvio.Value)} do Project Base Point");
            return a;
        }

        /// <summary>Quantos campos do Revit serão alterados ao gravar.</summary>
        public int CamposAlterados()
        {
            int n = 0;
            if (Ler(_latitude.Text, out double lat) && Ler(_longitude.Text, out double lon) &&
                PtlMetroSP.DistanciaMetros(_local.LatitudeAtual, _local.LongitudeAtual, lat, lon) > LocalizacaoIfc.ToleranciaLocation) n++;
            if (Diferente(_local.SiteAtual, _site.Text)) n++;
            if (Diferente(_local.LocalAtual, _endereco.Text)) n++;
            if (Diferente(_local.EdificioAtual, _edificio.Text)) n++;
            if (Diferente(_local.EnderecoAtual, _enderecoEdificio.Text)) n++;
            return n;
        }

        /// <summary>Lê e valida os campos. Devolve null e a mensagem em erro se algo estiver errado.</summary>
        public LocalizacaoIfc.Escolha Ler(out string erro)
        {
            erro = null;
            if (_local.Erro != null)
            {
                erro = "Não foi possível calcular a lat/long pelo Project Base Point: " + _local.Erro;
                return null;
            }
            if (!Ler(_latitude.Text, out double lat) || !Ler(_longitude.Text, out double lon) || Math.Abs(lat) > 90 || Math.Abs(lon) > 180)
            {
                erro = "Latitude e longitude precisam ser números em graus decimais (por exemplo -23.5508467 e -46.7315776).";
                return null;
            }
            if (lat >= 0 || lon >= 0)
            {
                erro = "A latitude e a longitude têm que ser negativas (hemisférios sul e oeste).";
                return null;
            }
            return new LocalizacaoIfc.Escolha
            {
                Latitude = lat,
                Longitude = lon,
                Local = _endereco.Text.Trim(),
                Site = _site.Text.Trim(),
                Edificio = _edificio.Text.Trim(),
                Endereco = _enderecoEdificio.Text.Trim(),
            };
        }

        /// <summary>Distância (m) entre a lat/long digitada e a calculada pelo PBP, ou null se não dá para ler.</summary>
        public double? DesvioDoPbp()
        {
            if (_local.Erro != null || !Ler(_latitude.Text, out double lat) || !Ler(_longitude.Text, out double lon)) return null;
            return PtlMetroSP.DistanciaMetros(lat, lon, _local.Latitude, _local.Longitude);
        }

        private void Atualizar()
        {
            if (Ler(_latitude.Text, out double lat) && Ler(_longitude.Text, out double lon))
            {
                double hoje = PtlMetroSP.DistanciaMetros(_local.LatitudeAtual, _local.LongitudeAtual, lat, lon);
                if (hoje <= LocalizacaoIfc.ToleranciaLocation)
                    Theme.MudarSelo(_seloLocation, "Confere", Theme.Estado.Ok, "O Location do Revit já tem esta latitude/longitude.");
                else
                    Theme.MudarSelo(_seloLocation, $"Será atualizado ({Distancia(hoje)})", Theme.Estado.Aviso,
                        $"Ao gravar, o Location passa para {Num(lat, 8)}, {Num(lon, 8)}.\n" +
                        "A atualização do Location não altera o Survey Point, o Project Base Point nem as Shared Coordinates.");
            }
            else Theme.MudarSelo(_seloLocation, "Lat/long inválidas", Theme.Estado.Aviso);

            double? desvio = DesvioDoPbp();
            _usarPbp.Visibility = desvio > LocalizacaoIfc.ToleranciaLocation ? Visibility.Visible : Visibility.Collapsed;
            Mudou?.Invoke();
        }

        private void AbrirMapa()
        {
            double lat = _local.LatitudeAtual, lon = _local.LongitudeAtual;
            if (Ler(_latitude.Text, out double la) && Ler(_longitude.Text, out double lo)) { lat = la; lon = lo; }
            try { Process.Start(new ProcessStartInfo(LocalizacaoIfc.LinkMapa(lat, lon)) { UseShellExecute = true }); }
            catch { /* sem navegador: ignora */ }
        }

        private static StackPanel Coluna(Grid colunas, int coluna, string titulo, string dica)
        {
            var p = new StackPanel();
            TextBlock t = Theme.Secao(titulo);
            t.ToolTip = dica;
            p.Children.Add(t);
            Grid.SetColumn(p, coluna);
            colunas.Children.Add(p);
            return p;
        }

        private static TextBox Campo(Panel painel, string rotulo, string valor)
        {
            TextBlock r = Theme.Label(rotulo);
            r.Margin = new Thickness(0, 4, 0, 3);
            painel.Children.Add(r);
            var box = new TextBox { Text = valor ?? string.Empty, VerticalContentAlignment = VerticalAlignment.Center };
            painel.Children.Add(box);
            return box;
        }

        private static bool Diferente(string a, string b) => !string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.Ordinal);

        /// <summary>Aceita ponto ou vírgula como separador decimal.</summary>
        private static bool Ler(string texto, out double valor) =>
            double.TryParse((texto ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out valor)
            && !double.IsNaN(valor) && !double.IsInfinity(valor);

        private static string Num(double v, int casas) => v.ToString("F" + casas, CultureInfo.InvariantCulture);

        private static string Distancia(double m) => m < 1 ? Num(m * 100, 1) + " cm" : m < 1000 ? Num(m, 2) + " m" : Num(m / 1000, 2) + " km";
    }
}
