using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MetroSP.Ui
{
    /// <summary>
    /// Identidade visual Ação BIM: preto, laranja (#FF9900), cinza e branco.
    /// Aplica estilos padrão (botões, caixas de texto, tabelas) e monta o cabeçalho das janelas.
    /// </summary>
    public static class Theme
    {
        public static readonly Color Preto = Color.FromRgb(0x1A, 0x1A, 0x1A);
        public static readonly Color Laranja = Color.FromRgb(0xFF, 0x99, 0x00);
        public static readonly Color LaranjaClaro = Color.FromRgb(0xFF, 0xE6, 0xBF);
        public static readonly Color LaranjaSelecao = Color.FromRgb(0xFF, 0xF3, 0xE0);
        public static readonly Color Cinza = Color.FromRgb(0x8A, 0x8A, 0x8A);
        public static readonly Color CinzaClaro = Color.FromRgb(0xE6, 0xE6, 0xE6);
        public static readonly Color CinzaFundo = Color.FromRgb(0xF5, 0xF5, 0xF5);
        public static readonly Color Verde = Color.FromRgb(0x2F, 0x7A, 0x3D);
        public static readonly Color VerdeClaro = Color.FromRgb(0xE2, 0xF1, 0xE4);
        public static readonly Color LaranjaTexto = Color.FromRgb(0x8A, 0x45, 0x00);

        public static SolidColorBrush Solid(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        // Fundo de linha por gravidade: problema, aviso, normal.
        public static readonly Brush LinhaProblema = Solid(LaranjaClaro);
        public static readonly Brush LinhaAviso = Solid(CinzaClaro);
        public static readonly Brush LinhaNormal = Brushes.White;

        public static Style PrimaryButton { get; } = CreateButtonStyle(primary: true);

        /// <summary>
        /// Monta a janela no padrão: cabeçalho (logo, título, subtítulo), corpo e rodapé de botões,
        /// e registra os estilos padrão nos recursos da janela.
        /// </summary>
        /// <summary>Fonte e estilos MetroSP (botões, campos, tabelas, abas) numa janela montada à mão.</summary>
        public static void Estilos(Window w)
        {
            w.Background = Brushes.White;
            w.FontFamily = new FontFamily("Segoe UI");
            w.FontSize = 12;
            w.Resources[typeof(Button)] = CreateButtonStyle(primary: false);
            w.Resources[typeof(TextBox)] = CreateTextBoxStyle();
            w.Resources[typeof(DataGrid)] = CreateDataGridStyle();
            w.Resources[typeof(DataGridColumnHeader)] = CreateHeaderStyle();
            w.Resources[typeof(TabControl)] = CreateTabControlStyle();
            w.Resources[typeof(TabItem)] = CreateTabItemStyle();
        }

        /// <summary>Marca do cabeçalho preto: a Entrega passo a passo esconde o cabeçalho da ferramenta embutida.</summary>
        public const string TagCabecalho = "MetroSP.Cabecalho";

        public static void Apply(Window w, string titulo, string subtitulo, UIElement corpo, UIElement rodape)
        {
            Estilos(w);

            var raiz = new DockPanel();

            UIElement cabecalho = CreateHeader(titulo, subtitulo);
            if (cabecalho is FrameworkElement fe) fe.Tag = TagCabecalho;
            DockPanel.SetDock(cabecalho, Dock.Top);
            raiz.Children.Add(cabecalho);

            if (rodape != null)
            {
                var barra = new Border
                {
                    Background = Solid(CinzaFundo),
                    BorderBrush = Solid(CinzaClaro),
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Padding = new Thickness(14, 10, 14, 10),
                    Child = rodape,
                };
                DockPanel.SetDock(barra, Dock.Bottom);
                raiz.Children.Add(barra);
            }

            // Janela que cresce com o conteúdo: o corpo ganha rolagem, para nada sumir quando ela encolher.
            var ajustaAoConteudo = w.SizeToContent != SizeToContent.Manual;
            UIElement miolo = ajustaAoConteudo
                ? new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = corpo }
                : corpo;
            raiz.Children.Add(new Border { Padding = new Thickness(14, 12, 14, 12), Child = miolo });
            w.Content = raiz;
            Redimensionavel(w, ajustaAoConteudo);
        }

        /// <summary>
        /// Regra da identidade: toda janela pode ser redimensionada. A que abre no tamanho do conteúdo
        /// passa a tamanho livre depois de aberta, com um mínimo para não esconder os botões.
        /// </summary>
        public static void Redimensionavel(Window w, bool ajustaAoConteudo)
        {
            w.ResizeMode = ResizeMode.CanResizeWithGrip;
            if (!ajustaAoConteudo) return;
            void Aberta(object s, EventArgs e)
            {
                w.ContentRendered -= Aberta;
                w.MinWidth = Math.Max(w.MinWidth, Math.Min(w.ActualWidth, 380));
                w.MinHeight = Math.Max(w.MinHeight, Math.Min(w.ActualHeight, 240));
                w.SizeToContent = SizeToContent.Manual;
            }
            w.ContentRendered += Aberta;
        }

        /// <summary>Etiqueta arredondada para resumos (ex.: "3 fora do .txt").</summary>
        public static Border Chip(string texto, bool destaque = false, bool escuro = false)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(9, 2, 9, 2),
                Margin = new Thickness(0, 0, 8, 0),
                Background = Solid(escuro ? Preto : destaque ? LaranjaClaro : CinzaClaro),
                Child = new TextBlock
                {
                    Text = texto,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11,
                    Foreground = escuro ? Brushes.White : Solid(destaque ? Color.FromRgb(0x8A, 0x45, 0x00) : Preto),
                },
            };
        }

        /// <summary>Rótulo de campo, no mesmo estilo dos títulos de seção (maiúsculas, pequeno), acima da caixa.</summary>
        public static TextBlock Label(string texto) => new TextBlock
        {
            Text = texto.ToUpperInvariant(),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Solid(Color.FromRgb(0x55, 0x55, 0x55)),
            Margin = new Thickness(0, 10, 0, 4),
        };

        /// <summary>Painel de botões alinhado à direita, com espaçamento padrão.</summary>
        public static StackPanel Buttons(params Button[] botoes)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            foreach (Button b in botoes)
            {
                b.Margin = new Thickness(8, 0, 0, 0);
                p.Children.Add(b);
            }
            return p;
        }

        private static UIElement CreateHeader(string titulo, string subtitulo)
        {
            var grid = new Grid { Height = 58, ClipToBounds = true, Background = Brushes.White };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Linhas de malha à direita, como nas peças da Ação BIM.
            var malha = new Canvas { Width = 280, HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false };
            for (int i = 0; i < 20; i++)
            {
                malha.Children.Add(new Path
                {
                    Data = Geometry.Parse($"M{i * 14},58 Q{i * 14 + 40},20 {i * 14 + 120},0"),
                    Stroke = Solid(Laranja),
                    StrokeThickness = 0.6,
                    Opacity = 0.35,
                });
            }
            Grid.SetColumnSpan(malha, 3);
            grid.Children.Add(malha);

            // Logos Ação BIM e Metrô lado a lado.
            var logos = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            var logo = new Image { Source = Icones.Carregar("acaobim_claro.png"), Height = 24, VerticalAlignment = VerticalAlignment.Center };
            var metro = new Image { Source = Icones.Carregar("metro_claro.png"), Height = 22, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
            RenderOptions.SetBitmapScalingMode(metro, BitmapScalingMode.HighQuality);
            logos.Children.Add(logo);
            logos.Children.Add(metro);
            grid.Children.Add(logos);

            var separador = new Border { Width = 1, Height = 30, Background = Solid(Color.FromRgb(0xDD, 0xDD, 0xDD)), Margin = new Thickness(0, 0, 12, 0) };
            Grid.SetColumn(separador, 1);
            grid.Children.Add(separador);

            var textos = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textos.Children.Add(new TextBlock { Text = titulo, Foreground = Solid(Preto), FontSize = 15, FontWeight = FontWeights.SemiBold });
            if (!string.IsNullOrEmpty(subtitulo))
                textos.Children.Add(new TextBlock { Text = subtitulo, Foreground = Solid(Cinza), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(textos, 2);
            grid.Children.Add(textos);

            return new Border { BorderBrush = Solid(Laranja), BorderThickness = new Thickness(0, 0, 0, 3), Child = grid };
        }

        private static Style CreateButtonStyle(bool primary)
        {
            var s = new Style(typeof(Button));
            s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 5, 14, 5)));
            s.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 90.0));
            s.Setters.Add(new Setter(Control.BackgroundProperty, primary ? Solid(Laranja) : Brushes.White));
            s.Setters.Add(new Setter(Control.ForegroundProperty, primary ? Brushes.White : Solid(Preto)));
            s.Setters.Add(new Setter(Control.BorderBrushProperty, primary ? Solid(Laranja) : Solid(Cinza)));
            s.Setters.Add(new Setter(Control.FontWeightProperty, primary ? FontWeights.SemiBold : FontWeights.Normal));
            s.Setters.Add(new Setter(Control.TemplateProperty, ButtonTemplate()));

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BorderBrushProperty, Solid(primary ? Preto : Laranja)));
            s.Triggers.Add(hover);
            var off = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            off.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5));
            s.Triggers.Add(off);
            return s;
        }

        /// <summary>Template simples (borda reta) para não herdar o visual azul padrão do Windows.</summary>
        private static ControlTemplate ButtonTemplate()
        {
            var t = new ControlTemplate(typeof(Button));
            var borda = new FrameworkElementFactory(typeof(Border));
            borda.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            borda.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            borda.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            borda.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var conteudo = new FrameworkElementFactory(typeof(ContentPresenter));
            conteudo.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            conteudo.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            borda.AppendChild(conteudo);
            t.VisualTree = borda;
            return t;
        }

        private static Style CreateTextBoxStyle()
        {
            var s = new Style(typeof(TextBox));
            s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 3, 4, 3)));
            s.Setters.Add(new Setter(Control.BorderBrushProperty, Solid(Color.FromRgb(0xBB, 0xBB, 0xBB))));
            var foco = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            foco.Setters.Add(new Setter(Control.BorderBrushProperty, Solid(Laranja)));
            s.Triggers.Add(foco);
            return s;
        }

        private static Style CreateDataGridStyle()
        {
            var s = new Style(typeof(DataGrid));
            s.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White));
            s.Setters.Add(new Setter(Control.BorderBrushProperty, Solid(CinzaClaro)));
            s.Setters.Add(new Setter(DataGrid.GridLinesVisibilityProperty, DataGridGridLinesVisibility.Horizontal));
            s.Setters.Add(new Setter(DataGrid.HorizontalGridLinesBrushProperty, Solid(CinzaClaro)));
            s.Setters.Add(new Setter(DataGrid.MinRowHeightProperty, 26.0));
            return s;
        }

        private static Style CreateHeaderStyle()
        {
            var s = new Style(typeof(DataGridColumnHeader));
            s.Setters.Add(new Setter(Control.BackgroundProperty, Solid(CinzaFundo)));
            s.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 5, 6, 5)));
            s.Setters.Add(new Setter(Control.BorderBrushProperty, Solid(Preto)));
            s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 2)));
            return s;
        }

        private static Style CreateTabControlStyle()
        {
            var s = new Style(typeof(TabControl));
            s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 1, 0, 0)));
            s.Setters.Add(new Setter(Control.BorderBrushProperty, Solid(CinzaClaro)));
            s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0, 10, 0, 0)));
            s.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White));
            return s;
        }

        /// <summary>Abas planas: a selecionada fica em negrito com sublinhado laranja.</summary>
        private static Style CreateTabItemStyle()
        {
            var t = new ControlTemplate(typeof(TabItem));
            var borda = new FrameworkElementFactory(typeof(Border)) { Name = "bd" };
            borda.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            borda.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            borda.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 3));
            borda.SetValue(Border.PaddingProperty, new Thickness(14, 7, 14, 6));
            borda.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 4, 0));
            var conteudo = new FrameworkElementFactory(typeof(ContentPresenter));
            conteudo.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            borda.AppendChild(conteudo);
            t.VisualTree = borda;

            var sel = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
            sel.Setters.Add(new Setter(Border.BorderBrushProperty, Solid(Laranja), "bd"));
            sel.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            t.Triggers.Add(sel);
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, Solid(CinzaFundo), "bd"));
            t.Triggers.Add(hover);

            var s = new Style(typeof(TabItem));
            s.Setters.Add(new Setter(Control.TemplateProperty, t));
            s.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
            s.Setters.Add(new Setter(Control.ForegroundProperty, Solid(Preto)));
            return s;
        }

        // ---------- layout enxuto: seções, listas com selos e rodapé com resumo ----------

        /// <summary>Situação mostrada num selo: neutro (cinza), ok (verde) ou aviso (laranja).</summary>
        public enum Estado { Neutro, Ok, Aviso }

        /// <summary>Título de seção em maiúsculas, pequeno e cinza escuro (ex.: "ARQUIVOS DA EXPORTAÇÃO").</summary>
        public static TextBlock Secao(string texto) => new TextBlock
        {
            Text = texto.ToUpperInvariant(),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Solid(Color.FromRgb(0x55, 0x55, 0x55)),
            Margin = new Thickness(0, 12, 0, 5),
        };

        /// <summary>Rótulo na mesma linha de um campo (ex.: "ESCOPO" antes da lista), no estilo dos títulos de seção.</summary>
        public static TextBlock Rotulo(string texto)
        {
            TextBlock t = Secao(texto);
            t.Margin = new Thickness(0, 0, 10, 0);
            t.VerticalAlignment = VerticalAlignment.Center;
            return t;
        }

        /// <summary>Selo de situação; o texto e a cor mudam depois com <see cref="MudarSelo"/>.</summary>
        public static Border Selo(string texto = "", Estado estado = Estado.Neutro)
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(9, 1, 9, 2),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
            };
            MudarSelo(b, texto, estado);
            return b;
        }

        public static void MudarSelo(Border selo, string texto, Estado estado, string dica = null)
        {
            var t = (TextBlock)selo.Child;
            t.Text = texto ?? "";
            selo.Visibility = string.IsNullOrEmpty(texto) ? Visibility.Collapsed : Visibility.Visible;
            selo.Background = Solid(estado == Estado.Ok ? VerdeClaro : estado == Estado.Aviso ? LaranjaClaro : CinzaClaro);
            t.Foreground = Solid(estado == Estado.Ok ? Verde : estado == Estado.Aviso ? LaranjaTexto : Color.FromRgb(0x33, 0x33, 0x33));
            selo.ToolTip = string.IsNullOrEmpty(dica) ? null : dica;
        }

        /// <summary>Caixa com borda fina para uma lista de itens (<see cref="Item"/>), separados por linhas.</summary>
        public static Border Grupo(out StackPanel itens)
        {
            itens = new StackPanel();
            return new Border { BorderBrush = Solid(CinzaClaro), BorderThickness = new Thickness(1), Child = itens };
        }

        /// <summary>
        /// Linha de uma lista: nome à esquerda, conteúdo (ex.: nome do arquivo), selo e ações à direita;
        /// o detalhe opcional vai embaixo do conteúdo, em cinza.
        /// </summary>
        public static Grid Item(string nome, UIElement conteudo, UIElement selo, UIElement acoes, UIElement detalhe = null, bool primeiro = false)
        {
            var g = new Grid { Margin = new Thickness(0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var rotulo = new TextBlock { Text = nome, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 7, 6, 7) };
            g.Children.Add(rotulo);
            if (conteudo is FrameworkElement fc) { fc.VerticalAlignment = VerticalAlignment.Center; fc.Margin = new Thickness(0, 7, 8, 7); }
            if (conteudo != null) { Grid.SetColumn(conteudo, 1); g.Children.Add(conteudo); }
            if (selo is FrameworkElement fs) fs.Margin = new Thickness(0, 0, 8, 0);
            if (selo != null) { Grid.SetColumn(selo, 2); g.Children.Add(selo); }
            if (acoes is FrameworkElement fa) { fa.VerticalAlignment = VerticalAlignment.Center; fa.Margin = new Thickness(0, 4, 8, 4); }
            if (acoes != null) { Grid.SetColumn(acoes, 3); g.Children.Add(acoes); }
            if (detalhe is FrameworkElement fd) fd.Margin = new Thickness(0, -4, 8, 7);
            if (detalhe != null) { Grid.SetRow(detalhe, 1); Grid.SetColumn(detalhe, 1); Grid.SetColumnSpan(detalhe, 3); g.Children.Add(detalhe); }

            return primeiro ? g : (Grid)Separado(g);
        }

        private static UIElement Separado(Grid g)
        {
            // Linha fina acima de cada item a partir do segundo.
            var linha = new Border { BorderBrush = Solid(CinzaClaro), BorderThickness = new Thickness(0, 1, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumnSpan(linha, 4);
            g.Children.Insert(0, linha);
            return g;
        }

        /// <summary>Texto cinza pequeno, para detalhes embaixo de um item ou de um campo.</summary>
        public static TextBlock Detalhe(string texto = "") => new TextBlock
        {
            Text = texto,
            Foreground = Solid(Cinza),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        };

        /// <summary>Botão com cara de link (texto sublinhado), para ações secundárias.</summary>
        public static Button Link(string texto)
        {
            var t = new ControlTemplate(typeof(Button));
            var tb = new FrameworkElementFactory(typeof(TextBlock));
            tb.SetValue(TextBlock.TextProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            tb.SetValue(TextBlock.TextDecorationsProperty, TextDecorations.Underline);
            tb.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            t.VisualTree = tb;
            var b = new Button
            {
                Content = texto,
                Template = t,
                Cursor = System.Windows.Input.Cursors.Hand,
                Foreground = Solid(Color.FromRgb(0x55, 0x55, 0x55)),
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 0,
                Padding = new Thickness(0),
            };
            return b;
        }

        /// <summary>Rodapé com um resumo à esquerda (avisos ou "Pronto") e os botões à direita.</summary>
        public static DockPanel Rodape(TextBlock resumo, params Button[] botoes)
        {
            var p = new DockPanel { LastChildFill = true };
            StackPanel b = Buttons(botoes);
            DockPanel.SetDock(b, Dock.Right);
            p.Children.Add(b);
            if (resumo != null)
            {
                resumo.VerticalAlignment = VerticalAlignment.Center;
                resumo.TextTrimming = TextTrimming.CharacterEllipsis;
                resumo.Margin = new Thickness(0, 0, 12, 0);
                p.Children.Add(resumo);
            }
            return p;
        }

        /// <summary>Atualiza o resumo do rodapé: a lista de avisos, ou o texto de "tudo certo".</summary>
        public static void MudarResumo(TextBlock resumo, System.Collections.Generic.IList<string> avisos, string pronto)
        {
            if (avisos == null || avisos.Count == 0)
            {
                resumo.Text = pronto;
                resumo.Foreground = Solid(Verde);
                resumo.ToolTip = null;
                return;
            }
            resumo.Text = (avisos.Count == 1 ? "1 aviso: " : avisos.Count + " avisos: ") + string.Join(" · ", avisos);
            resumo.Foreground = Solid(LaranjaTexto);
            resumo.ToolTip = string.Join("\n", avisos);
        }

        /// <summary>Seleção das tabelas em laranja claro (em vez do azul do Windows).</summary>
        public static void StyleSelection(DataGrid grid)
        {
            grid.Resources[SystemColors.HighlightBrushKey] = Solid(Laranja);
            grid.Resources[SystemColors.HighlightTextBrushKey] = Solid(Preto);
            grid.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = Solid(LaranjaClaro);
            grid.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = Solid(Preto);
        }
    }
}
