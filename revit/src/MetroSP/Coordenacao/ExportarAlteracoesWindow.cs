using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MetroSP.Ids;
using MetroSP.Ui;
using Microsoft.Win32;
using DB = Autodesk.Revit.DB;

namespace MetroSP.Coordenacao
{
    /// <summary>
    /// Exportar alterações: grava tudo como está no modelo agora (setup e fase, categorias IFC, classes por família,
    /// property sets, mapeamento e Projeto e Local) num arquivo .coord-metrosp, para outra pessoa abrir e usar.
    /// O arquivo pode ter vários perfis; os que já estão no modelo continuam e o escolhido é atualizado com o estado atual.
    /// Abre no contexto da API (grava no modelo).
    /// </summary>
    public class ExportarAlteracoesWindow : Window
    {
        private class Entrada
        {
            public PerfilCoordenacao Perfil;
            public bool Incluir = true;
            /// <summary>true = montado agora, com o que está no modelo.</summary>
            public bool Agora;
        }

        private readonly DB.Document _doc;
        private readonly ArquivoCoordenacao _anterior;
        private readonly List<Entrada> _entradas = new List<Entrada>();
        private readonly ComboBox _classe;
        private readonly TextBox _nome;
        private readonly StackPanel _lista;
        private readonly CheckBox _porElemento;
        private readonly CheckBox _dadosProjeto;
        private readonly CheckBox _usarNoModelo;
        private readonly TextBox _revisao;
        private readonly TextBox _nota;
        private readonly TextBlock _resumo;
        private string _padrao;
        private string _erroCaptura;
        private bool _montando;

        public static bool Abrir(Window dono, DB.Document doc)
        {
            var w = new ExportarAlteracoesWindow(doc) { Owner = dono };
            return w.ShowDialog() == true;
        }

        public static bool Abrir(IntPtr dono, DB.Document doc)
        {
            var w = new ExportarAlteracoesWindow(doc);
            new System.Windows.Interop.WindowInteropHelper(w).Owner = dono;
            return w.ShowDialog() == true;
        }

        public ExportarAlteracoesWindow(DB.Document doc)
        {
            _doc = doc;
            _anterior = CoordenacaoRevit.Ler(doc, out string perfilAtivo, out _);
            Title = "MetroSP - Exportar alterações";
            Width = 720;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;

            var corpo = new StackPanel();

            // ---------- o que está no modelo agora ----------
            corpo.Children.Add(Theme.Secao("Tudo como está agora"));
            var linha = new Grid();
            linha.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            linha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            linha.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            linha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var rotClasse = Theme.Rotulo("Classe");
            linha.Children.Add(rotClasse);
            _classe = new ComboBox { Padding = new Thickness(4, 3, 4, 3), ToolTip = "Classe MetroSP cujo setup e .txt entram no perfil (os mesmos que o Exportar IFC usa)." };
            Grid.SetColumn(_classe, 1);
            linha.Children.Add(_classe);
            var rotNome = Theme.Rotulo("Perfil");
            rotNome.Margin = new Thickness(16, 0, 10, 0);
            Grid.SetColumn(rotNome, 2);
            linha.Children.Add(rotNome);
            _nome = new TextBox { VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Nome do perfil (ex.: \"Entrega Metrô · IFC 4.3\"). Um perfil com o mesmo nome é substituído pelo estado atual." };
            Grid.SetColumn(_nome, 3);
            linha.Children.Add(_nome);
            corpo.Children.Add(linha);

            // ---------- perfis ----------
            corpo.Children.Add(Theme.Secao("Perfis que vão no arquivo"));
            corpo.Children.Add(Theme.Grupo(out _lista));
            var acoes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            var juntar = Theme.Link("Juntar perfis de outro arquivo...");
            juntar.ToolTip = "Traz os perfis de outro .coord-metrosp para este arquivo (perfis com o mesmo nome são substituídos).";
            juntar.Click += (s, e) => Juntar();
            acoes.Children.Add(juntar);
            corpo.Children.Add(acoes);

            // ---------- o que vai junto ----------
            corpo.Children.Add(Theme.Secao("O que vai junto"));
            corpo.Children.Add(Theme.Detalhe("Setup do exportador e fase (pelo nome), categorias IFC, classes IFC dos tipos de família, property sets e mapeamento."));
            _porElemento = new CheckBox
            {
                Content = "Classes IFC preenchidas em cada elemento (só valem neste modelo)",
                Margin = new Thickness(0, 6, 0, 0),
                ToolTip = "Leva também o \"Exportar para IFC como\", o tipo predefinido e o \"Exportar para IFC\" das instâncias, pelo UniqueId.\n" +
                          "Só servem para quem exporta este mesmo modelo.",
            };
            _porElemento.Checked += (s, e) => Capturar();
            _porElemento.Unchecked += (s, e) => Capturar();
            corpo.Children.Add(_porElemento);
            _dadosProjeto = new CheckBox
            {
                Content = "Projeto e Local (IfcProject, IfcSite, IfcBuilding e Pset MetroSP_Projeto)",
                IsChecked = true,
                Margin = new Thickness(0, 4, 0, 0),
                ToolTip = "Os dados do botão Projeto e Local deste modelo. Quem abre o arquivo pode atualizar o modelo dele com eles.",
            };
            _dadosProjeto.Checked += (s, e) => AtualizarResumo();
            _dadosProjeto.Unchecked += (s, e) => AtualizarResumo();
            corpo.Children.Add(_dadosProjeto);
            _usarNoModelo = new CheckBox
            {
                Content = "Usar também neste modelo",
                IsChecked = true,
                Margin = new Thickness(0, 4, 0, 0),
                ToolTip = "Guarda o arquivo neste modelo, como quem recebe faria. Assim o seu Exportar IFC usa o mesmo perfil.",
            };
            corpo.Children.Add(_usarNoModelo);

            // ---------- revisão ----------
            corpo.Children.Add(Theme.Secao("Revisão e nota"));
            var rev = new DockPanel();
            _revisao = new TextBox { Width = 50, Text = ((_anterior?.Revisao ?? 0) + 1).ToString(), VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Número da revisão. Quem recebe vê a revisão em uso no Exportar IFC." };
            var rotRev = new TextBlock { Text = "rev.", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            DockPanel.SetDock(rotRev, Dock.Left);
            DockPanel.SetDock(_revisao, Dock.Left);
            rev.Children.Add(rotRev);
            rev.Children.Add(_revisao);
            _nota = new TextBox { Margin = new Thickness(8, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "O que mudou nesta revisão (aparece para quem abre o arquivo)." };
            rev.Children.Add(_nota);
            corpo.Children.Add(rev);

            _resumo = new TextBlock();
            var salvar = new Button { Content = "Salvar arquivo...", IsDefault = true, Style = Theme.PrimaryButton };
            salvar.Click += (s, e) => Salvar();
            var cancelar = new Button { Content = "Cancelar", IsCancel = true };
            Theme.Apply(this, "Exportar alterações", "Tudo como está no modelo agora, para outra pessoa abrir e usar", corpo, Theme.Rodape(_resumo, cancelar, salvar));

            // Perfis que já estão no modelo; o ativo é o que recebe o estado atual.
            PerfilCoordenacao ativo = _anterior?.Perfil(perfilAtivo);
            if (_anterior != null)
                foreach (PerfilCoordenacao p in _anterior.Perfis) _entradas.Add(new Entrada { Perfil = p.Copia() });
            _padrao = _anterior?.PerfilPadrao;
            _porElemento.IsChecked = ativo?.Elementos.Count > 0;

            _montando = true;
            List<ClassesIfc.Classe> classes = ClassesIfc.Listar();
            foreach (ClassesIfc.Classe c in classes) _classe.Items.Add(c.Nome);
            string classe = ativo?.Classe ?? IfcExportSettings.Load().Classe ?? ClassesIfc.DoNomeDoModelo(doc.Title, classes).Classe?.Nome;
            _classe.SelectedItem = _classe.Items.OfType<string>().FirstOrDefault(n => string.Equals(n, classe, StringComparison.CurrentCultureIgnoreCase));
            _nome.Text = ativo?.Nome ?? "Entrega Metrô";
            _montando = false;
            _classe.SelectionChanged += (s, e) => Capturar();
            _nome.LostFocus += (s, e) => Capturar();
            Capturar();
        }

        /// <summary>Monta o perfil com o estado atual do modelo e põe na lista (no lugar do de mesmo nome).</summary>
        private void Capturar()
        {
            if (_montando) return;
            _erroCaptura = null;
            string nome = string.IsNullOrWhiteSpace(_nome.Text) ? "Entrega Metrô" : _nome.Text.Trim();
            _entradas.RemoveAll(e => e.Agora);
            if (_classe.SelectedItem is string classe)
            {
                try
                {
                    Cursor = System.Windows.Input.Cursors.Wait;
                    PerfilCoordenacao p = CoordenacaoRevit.Capturar(_doc, classe, nome, _porElemento.IsChecked == true);
                    int i = _entradas.FindIndex(e => string.Equals(e.Perfil.Nome, nome, StringComparison.CurrentCultureIgnoreCase));
                    var nova = new Entrada { Perfil = p, Agora = true };
                    if (i >= 0) _entradas[i] = nova; else _entradas.Insert(0, nova);
                }
                catch (Exception ex) { _erroCaptura = ex.Message; }
                finally { Cursor = null; }
            }
            else _erroCaptura = "escolha a classe";
            if (_padrao == null || !_entradas.Any(e => e.Perfil.Nome == _padrao)) _padrao = _entradas.FirstOrDefault()?.Perfil.Nome;
            MostrarLista();
        }

        private void MostrarLista()
        {
            _lista.Children.Clear();
            if (_entradas.Count == 0)
            {
                _lista.Children.Add(new TextBlock { Text = "Nenhum perfil.", Foreground = Theme.Solid(Theme.Cinza), Margin = new Thickness(10, 7, 10, 7) });
                AtualizarResumo();
                return;
            }
            bool primeiro = true;
            foreach (Entrada e in _entradas)
            {
                Entrada esta = e;
                var incluir = new CheckBox { Content = e.Perfil.Nome, IsChecked = e.Incluir, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = string.Join("\n", AbrirAlteracoesWindow.Descrever(e.Perfil, _doc)) };
                incluir.Checked += (s, a) => { esta.Incluir = true; AtualizarResumo(); };
                incluir.Unchecked += (s, a) => { esta.Incluir = false; AtualizarResumo(); };
                var chips = new StackPanel { Orientation = Orientation.Horizontal };
                if (e.Agora) chips.Children.Add(Theme.Chip("agora", escuro: true));
                if (e.Perfil.Esquema != null) chips.Children.Add(Theme.Chip(e.Perfil.Esquema));
                chips.Children.Add(Theme.Chip(e.Perfil.Alteracoes + " ajustes", destaque: true));
                var acoes = new StackPanel { Orientation = Orientation.Horizontal };
                if (_entradas.Count > 1)
                {
                    if (e.Perfil.Nome == _padrao) acoes.Children.Add(new TextBlock { Text = "padrão", Foreground = Theme.Solid(Theme.Verde), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
                        ToolTip = "Perfil que vale quando quem recebe não escolhe outro." });
                    else
                    {
                        var padrao = Theme.Link("tornar padrão");
                        padrao.Margin = new Thickness(0, 0, 10, 0);
                        padrao.ToolTip = "Perfil que vale quando quem recebe não escolhe outro.";
                        padrao.Click += (s, a) => { _padrao = esta.Perfil.Nome; MostrarLista(); };
                        acoes.Children.Add(padrao);
                    }
                }
                if (!e.Agora)
                {
                    var remover = Theme.Link("remover");
                    remover.Click += (s, a) => { _entradas.Remove(esta); if (_padrao == esta.Perfil.Nome) _padrao = null; Capturar(); };
                    acoes.Children.Add(remover);
                }
                _lista.Children.Add(Theme.Item("Classe " + (e.Perfil.Classe ?? "?"), incluir, chips, acoes, primeiro: primeiro));
                primeiro = false;
            }
            AtualizarResumo();
        }

        private List<PerfilCoordenacao> Incluidos() => _entradas.Where(e => e.Incluir).Select(e => e.Perfil).ToList();

        private void AtualizarResumo()
        {
            var avisos = new List<string>();
            if (_erroCaptura != null) avisos.Add(_erroCaptura);
            List<PerfilCoordenacao> perfis = Incluidos();
            if (perfis.Count == 0) avisos.Add("nenhum perfil marcado");
            int n = perfis.Sum(p => p.Alteracoes) + (_dadosProjeto.IsChecked == true ? 1 : 0);
            Theme.MudarResumo(_resumo, avisos, $"{perfis.Count} perfil(is) · {n} ajustes");
        }

        private void Juntar()
        {
            var d = new OpenFileDialog { Title = "Juntar perfis de outro arquivo de coordenação", Filter = ArquivoCoordenacao.Filtro };
            if (d.ShowDialog(this) != true) return;
            try
            {
                ArquivoCoordenacao outro = ArquivoCoordenacao.Ler(d.FileName);
                foreach (PerfilCoordenacao p in outro.Perfis)
                {
                    int i = _entradas.FindIndex(e => !e.Agora && string.Equals(e.Perfil.Nome, p.Nome, StringComparison.CurrentCultureIgnoreCase));
                    if (_entradas.Any(e => e.Agora && string.Equals(e.Perfil.Nome, p.Nome, StringComparison.CurrentCultureIgnoreCase))) continue;   // o de agora vale
                    if (i >= 0) _entradas[i] = new Entrada { Perfil = p }; else _entradas.Add(new Entrada { Perfil = p });
                }
                MostrarLista();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void Salvar()
        {
            List<PerfilCoordenacao> perfis = Incluidos();
            if (perfis.Count == 0) { MessageBox.Show(this, "Marque pelo menos um perfil.", Title); return; }
            if (!int.TryParse(_revisao.Text.Trim(), out int revisao) || revisao < 0)
            {
                MessageBox.Show(this, "A revisão deve ser um número.", Title);
                return;
            }
            var a = new ArquivoCoordenacao
            {
                Revisao = revisao,
                Autor = Environment.UserName,
                Data = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                Nota = string.IsNullOrWhiteSpace(_nota.Text) ? null : _nota.Text.Trim(),
                ModeloOrigem = _doc.Title,
                Perfis = perfis,
                PerfilPadrao = perfis.Any(p => p.Nome == _padrao) ? _padrao : perfis[0].Nome,
            };
            if (_dadosProjeto.IsChecked == true) a.Comum.DadosProjeto = CoordenacaoRevit.CapturarDadosProjeto(_doc);

            var d = new SaveFileDialog { Title = "Salvar arquivo de coordenação", Filter = ArquivoCoordenacao.Filtro, FileName = a.NomeSugerido(), DefaultExt = ArquivoCoordenacao.Extensao };
            try
            {
                string pasta = !string.IsNullOrEmpty(_doc.PathName) ? Path.GetDirectoryName(_doc.PathName) : null;
                if (pasta != null && Directory.Exists(pasta)) d.InitialDirectory = pasta;
            }
            catch { }
            if (d.ShowDialog(this) != true) return;
            try { a.Gravar(d.FileName); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível salvar o arquivo:\n" + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string noModelo = "";
            if (_usarNoModelo.IsChecked == true)
            {
                string perfil = perfis.Any(p => p.Nome == _nome.Text.Trim()) ? _nome.Text.Trim() : a.PerfilPadrao;
                if (CoordenacaoRevit.Executar(_doc, "MetroSP: exportar alterações", () => CoordenacaoRevit.Gravar(_doc, a, perfil, Path.GetFileName(d.FileName)), out string erro))
                {
                    CoordenacaoRevit.Ativar(_doc);
                    noModelo = $"\n\nEste modelo também passou a usar o perfil \"{a.Perfil(perfil).Nome}\".";
                }
                else noModelo = "\n\nNão foi possível guardar o arquivo neste modelo: " + erro;
            }
            MessageBox.Show(this, $"Arquivo salvo (rev. {a.Revisao}, {perfis.Count} perfil(is)):\n{d.FileName}\n\n" +
                "Envie para o projetista por e-mail, pela pasta do Forma ou pela rede. Ele abre em Entrega passo a passo › Abrir alterações " +
                "(ou no Exportar IFC), e o Exportar IFC passa a usar o perfil." + noModelo, Title, MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
    }
}
