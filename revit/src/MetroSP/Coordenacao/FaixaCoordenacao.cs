using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MetroSP.Ui;
using DB = Autodesk.Revit.DB;

namespace MetroSP.Coordenacao
{
    /// <summary>
    /// Faixa do arquivo de coordenação no Exportar IFC: com coordenação, a revisão, quem fez, o perfil (com troca) e os
    /// links para trocar ou deixar de usar; sem coordenação, o link para abrir um arquivo (e o aviso de um achado perto
    /// do modelo). Chama aoMudar depois de qualquer mudança, para a janela recarregar a classe e os arquivos.
    /// </summary>
    public class FaixaCoordenacao : Border
    {
        private readonly DB.Document _doc;
        private readonly Action _aoMudar;

        public FaixaCoordenacao(DB.Document doc, Action aoMudar)
        {
            _doc = doc;
            _aoMudar = aoMudar;
            CoordenacaoRevit.Ativar(doc);
            Mostrar();
        }

        private Window Dono => Window.GetWindow(this);

        private void Mostrar()
        {
            CoordenacaoRevit.Ativa c = CoordenacaoRevit.De(_doc);
            string achado = AbrirAlteracoesWindow.Proximos(_doc).FirstOrDefault();
            ArquivoCoordenacao novo = null;
            try { if (achado != null) novo = ArquivoCoordenacao.Ler(achado); } catch { achado = null; }

            if (c == null)
            {
                Margin = new Thickness(0, 0, 0, 6);
                BorderThickness = new Thickness(0);
                Background = Brushes.Transparent;
                Padding = new Thickness(0);
                var linha = new StackPanel { Orientation = Orientation.Horizontal };
                if (novo != null)
                {
                    linha.Children.Add(new TextBlock { Text = $"Há um arquivo de coordenação (rev. {novo.Revisao}) em {Path.GetFileName(achado)}. ",
                        Foreground = Theme.Solid(Theme.LaranjaTexto), VerticalAlignment = VerticalAlignment.Center });
                    linha.Children.Add(Link("usar...", "Abre o arquivo para escolher o perfil.", () => AbrirAlteracoesWindow.Abrir(Dono, _doc, achado)));
                }
                else linha.Children.Add(Link("Abrir arquivo de coordenação...",
                    "Usa as alterações que o coordenador BIM enviou (arquivo .coord-metrosp): setup, fase, categorias, classes por família e property sets.",
                    () => AbrirAlteracoesWindow.Abrir(Dono, _doc)));
                Child = linha;
                return;
            }

            Margin = new Thickness(0, 0, 0, 8);
            Background = Theme.Solid(Theme.LaranjaSelecao);
            BorderBrush = Theme.Solid(Theme.Laranja);
            BorderThickness = new Thickness(4, 1, 1, 1);
            Padding = new Thickness(10, 6, 10, 6);
            var grade = new DockPanel();
            var links = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (c.Arquivo.Perfis.Count > 1)
                links.Children.Add(Link("perfil ▾", "Trocar o perfil (fica gravado no modelo).", null, menuPerfis: true));
            links.Children.Add(Link("trocar...", "Abrir outro arquivo de coordenação ou outra revisão.", () => AbrirAlteracoesWindow.Abrir(Dono, _doc)));
            links.Children.Add(Link("não usar", "Tira o arquivo de coordenação deste modelo: volta a valer o setup e os .txt do seu computador.", NaoUsar));
            DockPanel.SetDock(links, Dock.Right);
            grade.Children.Add(links);

            var texto = new StackPanel();
            var titulo = new StackPanel { Orientation = Orientation.Horizontal };
            titulo.Children.Add(new TextBlock { Text = c.Titulo, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            Border chip = Theme.Chip(c.Perfil.Alteracoes + " ajustes", destaque: true);
            chip.ToolTip = string.Join("\n", AbrirAlteracoesWindow.Descrever(c.Perfil, _doc));
            titulo.Children.Add(chip);
            texto.Children.Add(titulo);
            texto.Children.Add(new TextBlock { Text = c.Resumo, FontSize = 11, Foreground = Theme.Solid(Color.FromRgb(0x55, 0x55, 0x55)), TextTrimming = TextTrimming.CharacterEllipsis });
            if (novo != null && novo.Revisao > c.Arquivo.Revisao)
            {
                var aviso = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
                aviso.Children.Add(new TextBlock { Text = $"Há uma revisão mais nova (rev. {novo.Revisao}) em {Path.GetFileName(achado)}. ", FontSize = 11, Foreground = Theme.Solid(Theme.LaranjaTexto) });
                aviso.Children.Add(Link("atualizar...", "Abre a revisão mais nova.", () => AbrirAlteracoesWindow.Abrir(Dono, _doc, achado)));
                texto.Children.Add(aviso);
            }
            grade.Children.Add(texto);
            Child = grade;
            ToolTip = "O que vem do arquivo de coordenação vale sobre o que está neste computador (setup, property sets, mapeamento e tabela de " +
                      "categorias da classe " + c.Perfil.Classe + "). Um arquivo que você salvar depois de receber a coordenação vale sobre o dela.";
        }

        private Button Link(string texto, string dica, Func<bool> acao, bool menuPerfis = false)
        {
            Button b = Theme.Link(texto);
            b.ToolTip = dica;
            b.Margin = new Thickness(10, 0, 0, 0);
            b.Click += (s, e) =>
            {
                if (menuPerfis) { MenuPerfis.Abrir(b, _doc, Mudou); return; }
                if (acao()) Mudou();
            };
            return b;
        }

        private Button Link(string texto, string dica, Action acao) => Link(texto, dica, () => { acao(); return false; });

        private void Mudou()
        {
            Mostrar();
            _aoMudar?.Invoke();
        }

        private void NaoUsar()
        {
            if (MessageBox.Show(Dono, "Tirar o arquivo de coordenação deste modelo?\n\nVolta a valer o setup e os .txt deste computador. " +
                    "Classes por família já gravadas no modelo continuam.", "MetroSP", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            if (!CoordenacaoRevit.Executar(_doc, "MetroSP: não usar coordenação", () => CoordenacaoRevit.Remover(_doc), out string erro))
            {
                MessageBox.Show(Dono, "Não foi possível: " + erro, "MetroSP");
                return;
            }
            CoordenacaoRevit.Ativar(_doc);
            Mudou();
        }
    }

    /// <summary>Menu com os perfis do arquivo de coordenação do modelo; escolher grava o perfil no modelo.</summary>
    public static class MenuPerfis
    {
        public static void Abrir(FrameworkElement alvo, DB.Document doc, Action aoMudar)
        {
            CoordenacaoRevit.Ativa c = CoordenacaoRevit.De(doc);
            if (c == null) return;
            var menu = new ContextMenu { PlacementTarget = alvo, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (PerfilCoordenacao p in c.Arquivo.Perfis)
            {
                var item = new MenuItem
                {
                    Header = p.Nome + (p.Esquema != null ? "  ·  " + p.Esquema : ""),
                    IsCheckable = true,
                    IsChecked = p.Nome == c.Perfil.Nome,
                    ToolTip = string.Join("\n", AbrirAlteracoesWindow.Descrever(p, doc)),
                };
                string nome = p.Nome;
                item.Click += (s, e) =>
                {
                    if (nome == c.Perfil.Nome) return;
                    if (!CoordenacaoRevit.TrocarPerfil(doc, nome, out string erro))
                        MessageBox.Show(Window.GetWindow(alvo), "Não foi possível trocar o perfil: " + erro, "MetroSP");
                    else aoMudar?.Invoke();
                };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }
    }
}
