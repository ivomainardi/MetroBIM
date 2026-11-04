using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MetroSP.Ids;
using MetroSP.Ui;

namespace MetroSP.Conferencia
{
    /// <summary>
    /// Escolhe os arquivos do pacote do Metrô. O projetista normalmente só abre um pacote (.metrosp) preparado pelo
    /// coordenador BIM; quem prepara escolhe os arquivos e clica em "Salvar pacote como...".
    /// </summary>
    public class PacoteWindow : Window
    {
        private readonly TextBox _nome, _ids, _setup, _psets, _classes, _planilha, _metroShared, _parametro;
        private readonly TextBlock _origem;
        private string _arquivo;
        private string _assinaturaArquivo;

        /// <summary>O pacote escolhido (válido depois de DialogResult = true).</summary>
        public PacoteMetro Pacote { get; private set; }

        public PacoteWindow(PacoteMetro atual, string pastaInicial)
        {
            Title = "MetroSP - Pacote do Metrô";
            Width = 720;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var painel = new StackPanel();

            var topo = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var abrir = new Button { Content = "Abrir pacote...", ToolTip = "Abre um pacote .metrosp preparado pelo coordenador BIM (computador, rede ou Forma)." };
            DockPanel.SetDock(abrir, Dock.Right);
            topo.Children.Add(abrir);
            _origem = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 10, 0) };
            topo.Children.Add(_origem);
            painel.Children.Add(topo);

            _nome = Campo(painel, "Nome do pacote (ex.: L20 · Classe B3 Arquitetura)");
            _psets = Arquivo(painel, "Property sets do Metrô (.txt) — obrigatório", "Texto (*.txt)|*.txt");
            _setup = Arquivo(painel, "Setup de exportação IFC (.json) — obrigatório", "Setup IFC (*.json)|*.json");
            _ids = Arquivo(painel, "IDS (.ids) — opcional; com ele, só os campos obrigatórios do IDS são exigidos", "IDS (*.ids;*.xml)|*.ids;*.xml");
            _classes = Arquivo(painel, "Tabela de classes IFC (.txt) — opcional", "Texto (*.txt)|*.txt");
            _planilha = Arquivo(painel, "Planilha de classificação (.xlsx ou .csv)", "Planilha (*.xlsx;*.csv)|*.xlsx;*.csv");
            _metroShared = Arquivo(painel, "MetroShared — parâmetros compartilhados (.txt)", "Texto (*.txt)|*.txt");
            _parametro = Campo(painel, "Parâmetro da classificação");

            Preencher(atual);
            _arquivo = atual.Arquivo;
            _assinaturaArquivo = atual.Arquivo != null ? atual.Assinatura() : null;
            AtualizarOrigem();

            abrir.Click += (s, e) =>
            {
                var d = new Microsoft.Win32.OpenFileDialog { Filter = $"Pacote MetroSP (*{PacoteMetro.Extensao})|*{PacoteMetro.Extensao}|Todos os arquivos (*.*)|*.*" };
                string inicio = _arquivo != null ? Path.GetDirectoryName(_arquivo) : pastaInicial;
                if (!string.IsNullOrEmpty(inicio) && Directory.Exists(inicio)) d.InitialDirectory = inicio;
                if (d.ShowDialog(this) != true) return;
                try
                {
                    PacoteMetro p = PacoteMetro.LerArquivo(d.FileName);
                    Preencher(p);
                    _arquivo = p.Arquivo;
                    _assinaturaArquivo = p.Assinatura();
                    AtualizarOrigem();
                }
                catch (Exception ex) { MessageBox.Show(this, "Não deu para ler o pacote:\n" + ex.Message, "MetroSP"); }
            };

            var salvarComo = new Button { Content = "Salvar pacote como...", ToolTip = "Grava um .metrosp com estes arquivos (caminhos relativos à pasta dele) para a equipe usar." };
            salvarComo.Click += (s, e) =>
            {
                var d = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = $"Pacote MetroSP (*{PacoteMetro.Extensao})|*{PacoteMetro.Extensao}",
                    FileName = (string.IsNullOrWhiteSpace(_nome.Text) ? "pacote" : Limpar(_nome.Text)) + PacoteMetro.Extensao,
                };
                string inicio = _arquivo != null ? Path.GetDirectoryName(_arquivo) : Path.GetDirectoryName(_psets.Text.Trim());
                if (!string.IsNullOrEmpty(inicio) && Directory.Exists(inicio)) d.InitialDirectory = inicio;
                if (d.ShowDialog(this) != true) return;
                try
                {
                    PacoteMetro p = Ler();
                    p.SalvarArquivo(d.FileName);
                    _arquivo = p.Arquivo;
                    _assinaturaArquivo = p.Assinatura();
                    AtualizarOrigem();
                }
                catch (Exception ex) { MessageBox.Show(this, "Não deu para salvar o pacote:\n" + ex.Message, "MetroSP"); }
            };

            var usar = new Button { Content = "Usar e conferir", IsDefault = true, Style = Theme.PrimaryButton };
            usar.Click += (s, e) =>
            {
                PacoteMetro p = Ler();
                // Pacote de arquivo editado aqui e não salvo: passa a ser só deste usuário.
                p.Arquivo = _arquivo != null && p.Assinatura() == _assinaturaArquivo ? _arquivo : null;
                p.SoVistaAtiva = atual.SoVistaAtiva;
                Pacote = p;
                DialogResult = true;
            };
            var cancelar = new Button { Content = "Cancelar", IsCancel = true };

            var rodape = new DockPanel();
            StackPanel direita = Theme.Buttons(cancelar, usar);
            DockPanel.SetDock(direita, Dock.Right);
            rodape.Children.Add(direita);
            rodape.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { salvarComo } });

            Theme.Apply(this, "Pacote do Metrô", "Os arquivos que o Metrô pede para a entrega", painel, rodape);
        }

        private void AtualizarOrigem()
        {
            _origem.Text = _arquivo != null
                ? "Pacote: " + _arquivo
                : "Arquivos escolhidos neste computador. Para a equipe usar os mesmos, salve um pacote numa pasta compartilhada.";
        }

        private void Preencher(PacoteMetro p)
        {
            _nome.Text = p.Nome ?? "";
            _ids.Text = p.Ids ?? "";
            _setup.Text = p.Setup ?? "";
            _psets.Text = p.PropertySets ?? "";
            _classes.Text = p.ClassesIfc ?? "";
            _planilha.Text = p.Planilha ?? "";
            _metroShared.Text = p.MetroShared ?? "";
            _parametro.Text = p.ParametroClassificacao ?? "ClassificationCode";
        }

        private PacoteMetro Ler()
        {
            string V(TextBox t) => string.IsNullOrWhiteSpace(t.Text) ? null : t.Text.Trim().Trim('"');
            return new PacoteMetro
            {
                Nome = V(_nome),
                Ids = V(_ids),
                Setup = V(_setup),
                PropertySets = V(_psets),
                ClassesIfc = V(_classes),
                Planilha = V(_planilha),
                MetroShared = V(_metroShared),
                ParametroClassificacao = V(_parametro) ?? "ClassificationCode",
            };
        }

        private static string Limpar(string nome)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) nome = nome.Replace(c, '-');
            return nome.Trim();
        }

        private static TextBox Campo(Panel painel, string rotulo)
        {
            painel.Children.Add(Theme.Label(rotulo));
            var box = new TextBox();
            painel.Children.Add(box);
            return box;
        }

        private TextBox Arquivo(Panel painel, string rotulo, string filtro)
        {
            painel.Children.Add(Theme.Label(rotulo));
            var linha = new DockPanel();
            var procurar = new Button { Content = "Procurar...", Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(procurar, Dock.Right);
            linha.Children.Add(procurar);
            var box = new TextBox { VerticalContentAlignment = VerticalAlignment.Center };
            linha.Children.Add(box);
            painel.Children.Add(linha);

            procurar.Click += (s, e) =>
            {
                var d = new Microsoft.Win32.OpenFileDialog { Filter = filtro + "|Todos os arquivos (*.*)|*.*" };
                string atual = box.Text.Trim().Trim('"');
                if (File.Exists(atual))
                {
                    d.InitialDirectory = Path.GetDirectoryName(atual);
                    d.FileName = Path.GetFileName(atual);
                }
                else if (_arquivo != null) d.InitialDirectory = Path.GetDirectoryName(_arquivo);
                if (d.ShowDialog(this) == true) box.Text = d.FileName;
            };
            return box;
        }
    }
}
