using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using MetroSP.Ui;

namespace MetroSP.Ids
{
    /// <summary>
    /// Bloco "Localização no IFC" da janela Exportar IFC: mostra o que vai para o IfcSite (latitude, longitude,
    /// endereço do projeto) e para o IfcBuilding (nome, endereço), já com a lat/long calculada pelo Project Base Point.
    /// O que o usuário deixar diferente do Revit é gravado nos campos padrão antes de exportar.
    /// </summary>
    public class LocalizacaoPainel : StackPanel
    {
        private readonly LocalizacaoIfc _local;
        private readonly TextBox _latitude;
        private readonly TextBox _longitude;
        private readonly TextBox _endereco;
        private readonly TextBox _edificio;
        private readonly TextBox _enderecoEdificio;
        private readonly TextBlock _situacao;

        public LocalizacaoPainel(LocalizacaoIfc local)
        {
            _local = local;
            LocalizacaoIfc.Escolha p = local.Proposta();

            Children.Add(Theme.Label("Localização no IFC (IfcSite e IfcBuilding)"));
            var pbp = Linha(this);
            if (local.Erro != null)
            {
                pbp.Text = "Não foi possível calcular a lat/long pelo Project Base Point: " + local.Erro;
                pbp.Foreground = Theme.Solid(Theme.Laranja);
            }
            else
                pbp.Text = $"Project Base Point (PTL): E {Num(local.Este, 3)} m · N {Num(local.Norte, 3)} m · elevação {Num(local.Elevacao, 3)} m. " +
                    "Latitude e longitude abaixo calculadas por ele (NBR 14166, SIRGAS 2000).";

            var grade = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Children.Add(grade);

            Titulo(grade, 0, "IfcSite");
            _latitude = Campo(grade, 1, 0, "Latitude", Num(p.Latitude, 8));
            _longitude = Campo(grade, 1, 2, "Longitude", Num(p.Longitude, 8));
            _endereco = Campo(grade, 2, 0, "Endereço do projeto", p.Local, largo: true);
            Titulo(grade, 3, "IfcBuilding");
            _edificio = Campo(grade, 4, 0, "Nome do edifício", p.Edificio, largo: true);
            _enderecoEdificio = Campo(grade, 5, 0, "Endereço", p.Endereco, largo: true);

            _situacao = Linha(this);
            _latitude.TextChanged += (s, e) => MostrarSituacao();
            _longitude.TextChanged += (s, e) => MostrarSituacao();
            MostrarSituacao();

            var nota = Linha(this);
            nota.Text = "Ao exportar, o que estiver diferente do Revit é gravado nos campos padrão: Manage › Location " +
                "(latitude, longitude, endereço do projeto) e Informações do projeto (nome do edifício, endereço). " +
                "A atualização do Location não altera o Survey Point, o Project Base Point nem as Shared Coordinates.";
        }

        /// <summary>Lê e valida os campos. Devolve null e a mensagem em erro se algo estiver errado.</summary>
        public LocalizacaoIfc.Escolha Ler(out string erro)
        {
            erro = null;
            if (_local.Erro != null)
            {
                erro = "A exportação precisa do Location corrigido pelo Project Base Point, e não foi possível calculá-lo: " + _local.Erro;
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

        private void MostrarSituacao()
        {
            _situacao.Inlines.Clear();
            _situacao.Foreground = Theme.Solid(Theme.Cinza);
            if (!Ler(_latitude.Text, out double lat) || !Ler(_longitude.Text, out double lon))
            {
                _situacao.Inlines.Add(new Run("Latitude/longitude inválidas."));
                _situacao.Foreground = Theme.Solid(Theme.Laranja);
                return;
            }
            double hoje = PtlMetroSP.DistanciaMetros(_local.LatitudeAtual, _local.LongitudeAtual, lat, lon);
            string texto = $"Location do Revit hoje: {Num(_local.LatitudeAtual, 8)}, {Num(_local.LongitudeAtual, 8)}" +
                (hoje <= LocalizacaoIfc.ToleranciaLocation ? " (igual)." : $" (será atualizado; diferença de {Distancia(hoje)}).");
            double? desvio = DesvioDoPbp();
            if (desvio > LocalizacaoIfc.ToleranciaLocation)
            {
                texto += $" Atenção: a lat/long digitada está a {Distancia(desvio.Value)} do Project Base Point.";
                _situacao.Foreground = Theme.Solid(Theme.Laranja);
            }
            _situacao.Inlines.Add(new Run(texto + " "));
            var mapa = new Hyperlink(new Run("Ver no mapa"));
            string url = LocalizacaoIfc.LinkMapa(lat, lon);
            mapa.Click += (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch { /* sem navegador: ignora */ }
            };
            _situacao.Inlines.Add(mapa);
            if (desvio > LocalizacaoIfc.ToleranciaLocation)
            {
                _situacao.Inlines.Add(new Run(" · "));
                var voltar = new Hyperlink(new Run("Usar o Project Base Point"));
                voltar.Click += (s, e) =>
                {
                    _latitude.Text = Num(_local.Latitude, 8);
                    _longitude.Text = Num(_local.Longitude, 8);
                };
                _situacao.Inlines.Add(voltar);
            }
        }

        private static void Titulo(Grid grade, int linha, string texto)
        {
            grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var t = new TextBlock { Text = texto, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) };
            Grid.SetRow(t, linha);
            Grid.SetColumnSpan(t, 4);
            grade.Children.Add(t);
        }

        private static TextBox Campo(Grid grade, int linha, int coluna, string rotulo, string valor, bool largo = false)
        {
            while (grade.RowDefinitions.Count <= linha) grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var r = new TextBlock { Text = rotulo, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(coluna == 0 ? 0 : 12, 2, 6, 2) };
            Grid.SetRow(r, linha);
            Grid.SetColumn(r, coluna);
            grade.Children.Add(r);
            var box = new TextBox { Text = valor ?? string.Empty, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
            Grid.SetRow(box, linha);
            Grid.SetColumn(box, coluna + 1);
            if (largo) Grid.SetColumnSpan(box, 3);
            grade.Children.Add(box);
            return box;
        }

        private static TextBlock Linha(Panel panel)
        {
            var t = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
            panel.Children.Add(t);
            return t;
        }

        /// <summary>Aceita ponto ou vírgula como separador decimal.</summary>
        private static bool Ler(string texto, out double valor) =>
            double.TryParse((texto ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out valor)
            && !double.IsNaN(valor) && !double.IsInfinity(valor);

        private static string Num(double v, int casas) => v.ToString("F" + casas, CultureInfo.InvariantCulture);

        private static string Distancia(double m) => m < 1 ? Num(m * 100, 1) + " cm" : m < 1000 ? Num(m, 2) + " m" : Num(m / 1000, 2) + " km";
    }
}
