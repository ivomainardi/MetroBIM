using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MetroSP.Ids;
using MetroSP.Ui;

namespace MetroSP.Conferencia
{
    /// <summary>
    /// Escolhe os arquivos do pacote do Metrô. O projetista normalmente só abre um pacote (.metrosp) preparado pelo
    /// coordenador BIM; quem prepara escolhe os arquivos e clica em "Salvar padrões como...".
    /// </summary>
    public class PacoteWindow : Window
    {
        private readonly TextBox _nome, _ids, _planilha, _metroShared, _parametro;
        /// <summary>Perfil de exportação IFC: traz o setup (.json), os property sets e o mapeamento (.txt).</summary>
        private readonly ComboBox _perfil;
        private readonly WrapPanel _arquivosPerfil;
        /// <summary>Padrões antigos (sem perfil): os três arquivos escolhidos um a um, mantidos enquanto não se escolhe um perfil.</summary>
        private string _setupSolto, _psetsSolto, _classesSolto;
        private const string SemPerfil = "(arquivos soltos dos padrões antigos)";
        private readonly TextBlock _origem;
        private readonly Button _excluir;
        private string _arquivo;
        private string _assinaturaArquivo;

        /// <summary>O pacote escolhido (válido depois de DialogResult = true).</summary>
        public PacoteMetro Pacote { get; private set; }

        private readonly TextBox _metroData;
        /// <summary>De onde vêm a disciplina, a etapa, a classe e os objetos do MetroDATA (escolhidos no passo MetroDATA).</summary>
        private PacoteMetro _escolhas;

        public PacoteWindow(PacoteMetro atual, string pastaInicial)
        {
            Title = "MetroSP - Arquivos do Metrô";
            Width = 720;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var painel = new StackPanel();

            var topo = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var abrir = new Button { Content = "Abrir padrões...", ToolTip = "Abre um arquivo de padrões .metrosp preparado pelo coordenador BIM (computador, rede ou Forma)." };
            DockPanel.SetDock(abrir, Dock.Right);
            topo.Children.Add(abrir);
            _origem = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 10, 0) };
            topo.Children.Add(_origem);
            painel.Children.Add(topo);
            // Para levar os arquivos à equipe agora é o arquivo de coordenação (.coord-metrosp), que leva tudo dentro dele.
            var aviso = Theme.Detalhe(atual.Coordenacao != null
                ? $"Este modelo segue a {atual.Coordenacao}: os arquivos que vieram nela valem sobre os daqui."
                : "Para mandar estes arquivos à equipe, use Coordenação › Padrões ▾ › Arquivo de coordenação (vão dentro dele).");
            aviso.Margin = new Thickness(0, 0, 0, 6);
            aviso.TextWrapping = TextWrapping.Wrap;
            painel.Children.Add(aviso);

            _nome = Campo(painel, "Nome dos padrões (ex.: L20 · Classe B3 Arquitetura)");
            // O setup, os property sets e a tabela de classes vêm do perfil de exportação (um de cada).
            painel.Children.Add(Theme.Label("Perfil de exportação IFC — obrigatório"));
            var linhaPerfil = new DockPanel();
            var perfis = new Button { Content = "Perfis...", Margin = new Thickness(6, 0, 0, 0),
                ToolTip = "Abre os perfis de exportação IFC: criar, trocar os arquivos, importar ou exportar um perfil." };
            DockPanel.SetDock(perfis, Dock.Right);
            linhaPerfil.Children.Add(perfis);
            _perfil = new ComboBox { ToolTip = "O perfil traz o setup de exportação (.json), os property sets (.txt) e a tabela de classes IFC (.txt, opcional)." };
            linhaPerfil.Children.Add(_perfil);
            painel.Children.Add(linhaPerfil);
            _arquivosPerfil = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            painel.Children.Add(_arquivosPerfil);
            _perfil.SelectionChanged += (s, e) => MostrarArquivosPerfil();
            perfis.Click += (s, e) =>
            {
                var w = new ArquivosIfcWindow(null, PerfilEscolhido) { Owner = this };
                w.ShowDialog();
                if (w.Mudou) CarregarPerfis(PerfilEscolhido);
            };
            _ids = Arquivo(painel, "Padrões Metrô (.ids) — opcional; com ele, só os campos obrigatórios dos padrões são exigidos", "Padrões Metrô (*.ids;*.xml)|*.ids;*.xml");
            _planilha = Arquivo(painel, "Planilha de classificação (.xlsx ou .csv)", "Planilha (*.xlsx;*.csv)|*.xlsx;*.csv");
            _metroShared = Arquivo(painel, "MetroShared — parâmetros compartilhados (.txt)", "Texto (*.txt)|*.txt");
            _metroData = Arquivo(painel, "MetroDATA — planilha exportada do Airtable (.xlsx)", "Planilha do Excel (*.xlsx)|*.xlsx");
            _metroData.ToolTip = "Planilha do MetroDATA exportada do Airtable. Quando ela for atualizada, é só escolher o arquivo novo (ou salvar por cima).";
            _parametro = Campo(painel, "Parâmetro da classificação");

            Preencher(atual);
            _escolhas = atual;
            _arquivo = atual.Arquivo;
            _assinaturaArquivo = atual.Arquivo != null ? atual.Assinatura() : null;
            AtualizarOrigem();

            abrir.Click += (s, e) =>
            {
                var d = new Microsoft.Win32.OpenFileDialog { Filter = $"Padrões MetroSP (*{PacoteMetro.Extensao})|*{PacoteMetro.Extensao}|Todos os arquivos (*.*)|*.*" };
                string inicio = _arquivo != null ? Path.GetDirectoryName(_arquivo) : pastaInicial;
                if (!string.IsNullOrEmpty(inicio) && Directory.Exists(inicio)) d.InitialDirectory = inicio;
                if (d.ShowDialog(this) != true) return;
                try
                {
                    PacoteMetro p = PacoteMetro.LerArquivo(d.FileName);
                    Preencher(p);
                    _escolhas = p;
                    _arquivo = p.Arquivo;
                    _assinaturaArquivo = p.Assinatura();
                    AtualizarOrigem();
                }
                catch (Exception ex) { MessageBox.Show(this, "Não deu para ler os padrões:\n" + ex.Message, "MetroSP"); }
            };

            _excluir = new Button
            {
                Content = "Excluir padrões...",
                ToolTip = "Apaga o arquivo .metrosp aberto. Os arquivos que ele indica (IDS, setup, psets, planilha...) não são apagados.",
            };
            _excluir.IsEnabled = _arquivo != null;
            _excluir.Click += (s, e) =>
            {
                if (_arquivo == null) return;
                if (MessageBox.Show(this, $"Excluir os padrões {Path.GetFileName(_arquivo)}?\n\n{_arquivo}\n\n" +
                        "Quem usa este arquivo deixa de achá-lo. Os arquivos que ele indica continuam onde estão, " +
                        "e os campos desta janela ficam como estão (só neste computador).", "MetroSP",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                try
                {
                    if (File.Exists(_arquivo)) File.Delete(_arquivo);
                    _arquivo = null;
                    _assinaturaArquivo = null;
                    AtualizarOrigem();
                }
                catch (Exception ex) { MessageBox.Show(this, "Não deu para excluir os padrões:\n" + ex.Message, "MetroSP"); }
            };

            var usar = new Button { Content = "Usar e conferir", IsDefault = true, Style = Theme.PrimaryButton };
            usar.Click += (s, e) =>
            {
                if (PerfilEscolhido == null && _setupSolto == null && _psetsSolto == null &&
                    MessageBox.Show(this, "Nenhum perfil de exportação escolhido: a conferência fica sem setup e sem property sets.\n\nContinuar assim mesmo?", "MetroSP",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
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
            _excluir.Margin = new Thickness(8, 0, 0, 0);
            rodape.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { _excluir } });

            Theme.Apply(this, "Arquivos do Metrô", "Os arquivos que o Metrô pede para a entrega", painel, rodape);
        }

        private void AtualizarOrigem()
        {
            if (_excluir != null) _excluir.IsEnabled = _arquivo != null;
            _origem.Text = _arquivo != null
                ? "Padrões: " + _arquivo
                : "Arquivos escolhidos neste computador. Para a equipe usar os mesmos, salve os padrões numa pasta compartilhada.";
        }

        private void Preencher(PacoteMetro p)
        {
            _nome.Text = p.Nome ?? "";
            _ids.Text = p.Ids ?? "";
            bool comPerfil = !string.IsNullOrWhiteSpace(p.Perfil);
            _setupSolto = comPerfil ? null : p.Setup;
            _psetsSolto = comPerfil ? null : p.PropertySets;
            _classesSolto = comPerfil ? null : p.ClassesIfc;
            CarregarPerfis(comPerfil ? p.Perfil.Trim() : null);
            _planilha.Text = p.Planilha ?? "";
            _metroShared.Text = p.MetroShared ?? "";
            _metroData.Text = p.MetroData ?? "";
            _parametro.Text = p.ParametroClassificacao ?? "ClassificationCode";
        }

        private PacoteMetro Ler()
        {
            string V(TextBox t) => string.IsNullOrWhiteSpace(t.Text) ? null : t.Text.Trim().Trim('"');
            var p = new PacoteMetro
            {
                Nome = V(_nome),
                Ids = V(_ids),
                Perfil = PerfilEscolhido,
                Setup = _setupSolto,
                PropertySets = _psetsSolto,
                ClassesIfc = _classesSolto,
                Planilha = V(_planilha),
                MetroShared = V(_metroShared),
                ParametroClassificacao = V(_parametro) ?? "ClassificationCode",
                MetroData = V(_metroData),
            };
            p.AplicarPerfil();
            p.CopiarEscolhasMetroData(_escolhas);
            return p;
        }

        private string PerfilEscolhido => (_perfil.SelectedItem as ComboBoxItem)?.Tag as string;

        /// <summary>Lista os perfis deste computador e escolhe o indicado.</summary>
        private void CarregarPerfis(string escolher)
        {
            _perfil.Items.Clear();
            List<ClassesIfc.Classe> perfis;
            try { perfis = ClassesIfc.Listar(); }
            catch { perfis = new List<ClassesIfc.Classe>(); }
            foreach (ClassesIfc.Classe c in perfis)
                _perfil.Items.Add(new ComboBoxItem { Content = c.Nome, Tag = c.Nome });
            bool soltos = _setupSolto != null || _psetsSolto != null || _classesSolto != null;
            if (soltos)
                _perfil.Items.Add(new ComboBoxItem
                {
                    Content = SemPerfil, Tag = null,
                    ToolTip = "Estes padrões foram feitos antes dos perfis, com os arquivos escolhidos um a um:\n" +
                              string.Join("\n", new[] { _setupSolto, _psetsSolto, _classesSolto }.Where(x => x != null)) +
                              "\n\nEscolha um perfil para usar os arquivos dele.",
                });
            ComboBoxItem item = _perfil.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Tag is string n && string.Equals(n, escolher, StringComparison.CurrentCultureIgnoreCase));
            if (item == null && escolher != null)
            {
                // Perfil dos padrões que não existe neste computador: fica na lista, avisado.
                item = new ComboBoxItem { Content = escolher + "  (não encontrado neste computador)", Tag = escolher, Foreground = Theme.Solid(Theme.LaranjaTexto),
                    ToolTip = "Importe este perfil em Perfis... › Importar (peça o .zip a quem preparou os padrões)." };
                _perfil.Items.Insert(0, item);
            }
            _perfil.SelectedItem = item ?? (soltos ? _perfil.Items.Cast<ComboBoxItem>().Last() : null);
            MostrarArquivosPerfil();
        }

        /// <summary>Embaixo do perfil: os três arquivos dele (● tem, ○ falta).</summary>
        private void MostrarArquivosPerfil()
        {
            if (_arquivosPerfil == null) return;
            _arquivosPerfil.Children.Clear();
            string nome = PerfilEscolhido;
            string setup, psets, classes;
            if (nome != null)
            {
                ClassesIfc.Classe c = null;
                try { c = ClassesIfc.Obter(nome); } catch { /* sem perfil */ }
                setup = c?.Setup; psets = c?.PropertySets; classes = c?.Mapeamento;
            }
            else if (_perfil.SelectedItem != null) { setup = _setupSolto; psets = _psetsSolto; classes = _classesSolto; }
            else
            {
                _arquivosPerfil.Children.Add(Theme.Detalhe(_perfil.Items.Count == 0 ? "Nenhum perfil neste computador: crie ou importe um em Perfis..." : "Escolha o perfil da entrega."));
                return;
            }
            void Marca(string caminho, string rotulo, string falta, bool obrigatorio)
            {
                bool tem = caminho != null && File.Exists(caminho);
                _arquivosPerfil.Children.Add(new TextBlock
                {
                    Text = (tem ? "● " : "○ ") + (tem ? Path.GetFileName(caminho) + " " + rotulo : falta),
                    FontSize = 11, Margin = new Thickness(0, 0, 14, 0), ToolTip = caminho,
                    Foreground = Theme.Solid(tem ? Theme.Verde : obrigatorio ? Theme.LaranjaTexto : Theme.Cinza),
                });
            }
            Marca(setup, "(setup)", "sem setup (.json)", true);
            Marca(psets, "(property sets)", "sem property sets (.txt)", true);
            Marca(classes, "(classes IFC)", "sem tabela de classes (opcional)", false);
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
            // ✕ tira o arquivo dos padrões (o arquivo em si não é apagado).
            var tirar = new Button
            {
                Content = "✕", MinWidth = 0, Width = 28, Padding = new Thickness(0, 3, 0, 3), Margin = new Thickness(6, 0, 0, 0),
                ToolTip = "Tirar este arquivo dos padrões (o arquivo não é apagado).", IsEnabled = false,
            };
            DockPanel.SetDock(tirar, Dock.Right);
            linha.Children.Add(tirar);
            DockPanel.SetDock(procurar, Dock.Right);
            linha.Children.Add(procurar);
            var box = new TextBox { VerticalContentAlignment = VerticalAlignment.Center };
            linha.Children.Add(box);
            painel.Children.Add(linha);
            box.TextChanged += (s, e) => tirar.IsEnabled = box.Text.Trim().Length > 0;
            tirar.Click += (s, e) => box.Text = string.Empty;

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
