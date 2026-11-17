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
    /// Exportar alterações: salva tudo como está no modelo agora num arquivo .coord-metrosp para enviar ao projetista.
    /// Um perfil escolhido de cada vez: ao salvar, ele fica igual ao modelo agora e os outros perfis do arquivo ficam
    /// como estão. A lista mostra exatamente o que vai. Abre no contexto da API (o arquivo fica guardado no modelo).
    /// </summary>
    public class ExportarAlteracoesWindow : Window
    {
        private readonly DB.Document _doc;
        private readonly ArquivoCoordenacao _anterior;
        /// <summary>Perfis do arquivo, na ordem (o primeiro é o padrão).</summary>
        private readonly List<PerfilCoordenacao> _perfis = new List<PerfilCoordenacao>();
        private readonly ComboBox _perfil;
        private readonly ComboBox _classe;
        private readonly Button _excluir;
        private readonly TextBlock _frase;
        private readonly StackPanel _lista;
        private readonly CheckBox _porElemento = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBox _revisao;
        private readonly TextBox _nota;
        private readonly TextBlock _resumo;
        private readonly Conferencia.PacoteMetro _pacote = Conferencia.PacoteMetro.Abrir();
        private PerfilCoordenacao _capturado;
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
            Width = 700;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;

            var corpo = new StackPanel();

            // ---------- perfil ----------
            corpo.Children.Add(Theme.Secao("Perfil"));
            var linha = new DockPanel();
            var novo = new Button { Content = "+ Novo perfil", Margin = new Thickness(8, 0, 0, 0),
                ToolTip = "Cria outro perfil no mesmo arquivo (ex.: \"Compatibilização · IFC 4\"), com o nome e a base que você escolher." };
            novo.Click += (s, e) => NovoPerfil();
            DockPanel.SetDock(novo, Dock.Right);
            linha.Children.Add(novo);
            _classe = new ComboBox { Width = 110, Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(8, 0, 0, 0),
                ToolTip = "Base: o perfil deste computador cujo setup e .txt entram no arquivo (os mesmos que o Exportar IFC usa)." };
            DockPanel.SetDock(_classe, Dock.Right);
            linha.Children.Add(_classe);
            _perfil = new ComboBox { Padding = new Thickness(4, 3, 4, 3),
                ToolTip = "Um arquivo pode ter vários perfis (jeitos de exportar). O escolhido aqui é o que recebe o estado atual do modelo." };
            linha.Children.Add(_perfil);
            corpo.Children.Add(linha);
            var linhaFrase = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            _excluir = Theme.Link("Excluir perfil");
            _excluir.ToolTip = "Tira este perfil do arquivo.";
            _excluir.Click += (s, e) => ExcluirPerfil();
            DockPanel.SetDock(_excluir, Dock.Right);
            linhaFrase.Children.Add(_excluir);
            _frase = Theme.Detalhe();
            _frase.ToolTip = "O primeiro perfil é o que vale quando quem recebe não escolhe outro. " +
                             "No Exportar IFC do projetista, a escolha de perfil só aparece quando o arquivo tem mais de um.";
            linhaFrase.Children.Add(_frase);
            corpo.Children.Add(linhaFrase);

            // ---------- o que vai ----------
            corpo.Children.Add(Theme.Secao("O que vai no arquivo (lido do modelo agora)"));
            corpo.Children.Add(Theme.Grupo(out _lista));
            _porElemento.Checked += (s, e) => AtualizarResumo();
            _porElemento.Unchecked += (s, e) => AtualizarResumo();

            // ---------- revisão ----------
            corpo.Children.Add(Theme.Secao("Revisão"));
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
            Theme.Apply(this, "Exportar alterações", "Salva tudo como está agora num arquivo para enviar ao projetista", corpo, Theme.Rodape(_resumo, cancelar, salvar));

            // Perfis que já estão no modelo (o ativo vem escolhido); sem nenhum, um perfil "Entrega Metrô" com a classe do modelo.
            List<ClassesIfc.Classe> classes = ClassesIfc.Listar();
            foreach (ClassesIfc.Classe c in classes) _classe.Items.Add(c.Nome);
            if (_anterior != null) _perfis.AddRange(_anterior.Perfil(null) is PerfilCoordenacao padrao
                ? new[] { padrao }.Concat(_anterior.Perfis.Where(p => p != padrao)).Select(p => p.Copia()) : _anterior.Perfis.Select(p => p.Copia()));
            if (_perfis.Count == 0)
                _perfis.Add(new PerfilCoordenacao
                {
                    Nome = "Entrega Metrô",
                    Classe = IfcExportSettings.Load().Classe ?? ClassesIfc.DoNomeDoModelo(doc.Title, classes).Classe?.Nome ?? classes.FirstOrDefault()?.Nome,
                });
            PerfilCoordenacao ativo = _perfis.FirstOrDefault(p => p.Nome == _anterior?.Perfil(perfilAtivo)?.Nome) ?? _perfis[0];
            _porElemento.IsChecked = ativo.Elementos.Count > 0;
            PreencherPerfis(ativo);
            _perfil.SelectionChanged += (s, e) => { if (!_montando) Capturar(); };
            _classe.SelectionChanged += (s, e) =>
            {
                if (_montando || !(_perfil.SelectedItem is PerfilCoordenacao p) || !(_classe.SelectedItem is string c)) return;
                p.Classe = c;
                Capturar();
            };
            Capturar();
        }

        private PerfilCoordenacao Escolhido => _perfil.SelectedItem as PerfilCoordenacao;

        private void PreencherPerfis(PerfilCoordenacao escolher)
        {
            _montando = true;
            _perfil.Items.Clear();
            foreach (PerfilCoordenacao p in _perfis) _perfil.Items.Add(p);
            _perfil.SelectedItem = escolher;
            _montando = false;
        }

        /// <summary>Lê o modelo agora para o perfil escolhido (classe dele) e mostra o que vai.</summary>
        private void Capturar()
        {
            _erroCaptura = null;
            _capturado = null;
            PerfilCoordenacao p = Escolhido;
            if (p == null) return;
            _montando = true;
            _classe.SelectedItem = _classe.Items.OfType<string>().FirstOrDefault(n => string.Equals(n, p.Classe, StringComparison.CurrentCultureIgnoreCase));
            _montando = false;
            if (p.Classe == null || !(_classe.SelectedItem is string))
                _erroCaptura = "escolha a base do perfil";
            else
            {
                try
                {
                    Cursor = System.Windows.Input.Cursors.Wait;
                    _capturado = CoordenacaoRevit.Capturar(_doc, p.Classe, p.Nome, porElemento: true);
                }
                catch (Exception ex) { _erroCaptura = ex.Message; }
                finally { Cursor = null; }
            }
            int outros = _perfis.Count - 1;
            _frase.Text = "Ao salvar, este perfil fica igual ao modelo agora." +
                          (outros == 1 ? " O outro perfil do arquivo fica como está." : outros > 1 ? $" Os outros {outros} perfis do arquivo ficam como estão." : "") +
                          (_perfis.IndexOf(p) == 0 && outros > 0 ? " É o perfil principal (o primeiro)." : "");
            _excluir.Visibility = outros > 0 ? Visibility.Visible : Visibility.Collapsed;
            MostrarLista();
        }

        private void MostrarLista()
        {
            _lista.Children.Clear();
            PerfilCoordenacao c = _capturado;
            bool primeiro = true;
            void Linha(string nome, string texto, UIElement extra = null, bool ok = true)
            {
                var marca = new TextBlock { Text = ok ? "✓" : "–", Width = 16, FontWeight = FontWeights.Bold,
                    Foreground = Theme.Solid(ok ? Theme.Verde : Theme.Cinza), VerticalAlignment = VerticalAlignment.Center };
                var conteudo = new DockPanel();
                if (extra != null) { DockPanel.SetDock(extra, Dock.Right); conteudo.Children.Add(extra); }
                conteudo.Children.Add(new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = ok ? Theme.Solid(Theme.Preto) : Theme.Solid(Theme.Cinza) });
                var linha = new DockPanel();
                DockPanel.SetDock(marca, Dock.Left);
                linha.Children.Add(marca);
                linha.Children.Add(conteudo);
                _lista.Children.Add(Theme.Item(nome, linha, null, null, primeiro: primeiro));
                primeiro = false;
            }

            if (c == null)
            {
                Linha("Perfil", _erroCaptura ?? "", ok: false);
                AtualizarResumo();
                return;
            }
            string setup = c.Exportador?["Name"]?.ToString();
            Linha("Exportador", $"Setup {setup ?? c.Classe}" + (c.Esquema != null ? $" ({c.Esquema})" : "") +
                                (c.Fase != null ? $", fase \"{c.Fase}\"" : ", fase padrão do exportador"));
            OpcoesExportacao o = c.Exportacao ?? new OpcoesExportacao();
            Linha("Elementos", (o.SoVista ? $"só o visível na vista 3D \"{o.Elementos}\"" : "modelo inteiro") + " · " +
                               (o.NomeArquivo == null || o.NomeArquivo == OpcoesExportacao.NomeDoModelo ? "arquivo com o nome do modelo" : $"arquivo \"{o.NomeArquivo}.ifc\""));
            Linha("Coordenadas", (o.EpsgEmBranco != false ? "EPSG em branco" : "EPSG do setup") + " · " +
                                 (o.SempreEixos != false ? "sempre exportar os eixos" : "eixos pela vista e pela tabela"));
            string cat = string.Join(" · ", c.Categorias.Select(t => $"tabela {t.Key} ({t.Value.Count} categorias, {t.Value.Count(l => !l.Exportar)} sem exportar)"));
            Linha("Categorias IFC", cat.Length > 0 ? cat : "sem tabela salva: vale o padrão MetroSP de quem exporta", ok: cat.Length > 0);
            Linha("Classes por família", c.Familias.Count > 0 ? $"{c.Familias.Count} tipo(s) com classe IFC preenchida" : "nenhum tipo com classe IFC preenchida", ok: c.Familias.Count > 0);
            if (c.Elementos.Count > 0)
            {
                _porElemento.ToolTip = "Leva também o \"Exportar para IFC como\", o tipo predefinido e o \"Exportar para IFC\" preenchidos nas instâncias, " +
                                       "pelo UniqueId. Só valem para quem exportar este mesmo modelo.";
                Linha("Por elemento", $"{c.Elementos.Count} elemento(s) com classe IFC na instância (só valem neste modelo)", _porElemento, ok: _porElemento.IsChecked == true);
            }
            var txt = new List<string>();
            if (c.PropertySets != null) txt.Add($"{c.Classe}.txt");
            if (c.Mapeamento != null) txt.Add("mapeamento");
            Linha("Property sets", txt.Count > 0 ? string.Join(" e ", txt) : "nenhum", ok: txt.Count > 0);
            Linha("Projeto e Local", "IfcProject, IfcSite, IfcBuilding e Pset MetroSP_Projeto");
            var entrega = new[] { _pacote.Ids, _pacote.Planilha, _pacote.MetroShared }.Where(f => !string.IsNullOrWhiteSpace(f) && File.Exists(f)).Select(Path.GetFileName).ToList();
            Linha("Entrega", entrega.Count > 0 ? string.Join(", ", entrega) : "nenhum arquivo da entrega (.ids, planilha, MetroShared) em uso", ok: entrega.Count > 0);
            bool regras = ClassesIfc.ArquivoDeRegras != null;
            Linha("Nomenclatura", regras ? "regras de classe pelo nome do modelo" : "sem regras", ok: regras);
            AtualizarResumo();
        }

        private void AtualizarResumo()
        {
            if (_capturado == null) Theme.MudarResumo(_resumo, new[] { _erroCaptura ?? "nada para salvar" }, "");
            else Theme.MudarResumo(_resumo, null, $"Perfil \"{_capturado.Nome}\" pronto · rev. {_revisao?.Text}");
        }

        private void NovoPerfil()
        {
            var nome = new TextBox { VerticalContentAlignment = VerticalAlignment.Center, Text = "Compatibilização" };
            var classe = new ComboBox { Padding = new Thickness(4, 3, 4, 3) };
            foreach (object c in _classe.Items) classe.Items.Add(c);
            classe.SelectedItem = Escolhido?.Classe != null ? _classe.Items.OfType<string>().FirstOrDefault(n => n == Escolhido.Classe) : null;
            var corpo = new StackPanel();
            corpo.Children.Add(Theme.Label("Nome do perfil"));
            corpo.Children.Add(nome);
            corpo.Children.Add(Theme.Label("Base"));
            corpo.Children.Add(classe);
            var w = new Window { Title = "MetroSP - Novo perfil", Width = 380, SizeToContent = SizeToContent.Height, Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.CanResizeWithGrip };
            var ok = new Button { Content = "Criar", IsDefault = true, Style = Theme.PrimaryButton };
            ok.Click += (s, e) =>
            {
                string n = nome.Text.Trim();
                if (n.Length == 0 || !(classe.SelectedItem is string)) { MessageBox.Show(w, "Indique o nome e a classe.", w.Title); return; }
                if (_perfis.Any(p => string.Equals(p.Nome, n, StringComparison.CurrentCultureIgnoreCase))) { MessageBox.Show(w, "Já existe um perfil com esse nome.", w.Title); return; }
                w.DialogResult = true;
            };
            Theme.Apply(w, "Novo perfil", "Outro jeito de exportar, no mesmo arquivo", corpo, Theme.Rodape(null, new Button { Content = "Cancelar", IsCancel = true }, ok));
            nome.Loaded += (s, e) => { nome.Focus(); nome.SelectAll(); };
            if (w.ShowDialog() != true) return;
            var novo = new PerfilCoordenacao { Nome = nome.Text.Trim(), Classe = (string)classe.SelectedItem };
            _perfis.Add(novo);
            PreencherPerfis(novo);
            Capturar();
        }

        private void ExcluirPerfil()
        {
            PerfilCoordenacao p = Escolhido;
            if (p == null || _perfis.Count < 2) return;
            if (MessageBox.Show(this, $"Tirar o perfil \"{p.Nome}\" do arquivo?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _perfis.Remove(p);
            PreencherPerfis(_perfis[0]);
            Capturar();
        }

        private void Salvar()
        {
            PerfilCoordenacao p = _capturado;
            if (p == null) { MessageBox.Show(this, "Não foi possível ler o perfil: " + _erroCaptura, Title); return; }
            if (!int.TryParse(_revisao.Text.Trim(), out int revisao) || revisao < 0)
            {
                MessageBox.Show(this, "A revisão deve ser um número.", Title);
                return;
            }
            if (_porElemento.IsChecked != true) p.Elementos.Clear();
            // O escolhido fica igual ao modelo agora; os outros ficam como estão; o primeiro é o padrão.
            List<PerfilCoordenacao> perfis = _perfis.Select(x => x == Escolhido ? p : x).ToList();
            var a = new ArquivoCoordenacao
            {
                Revisao = revisao,
                Autor = Environment.UserName,
                Data = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                Nota = string.IsNullOrWhiteSpace(_nota.Text) ? null : _nota.Text.Trim(),
                ModeloOrigem = _doc.Title,
                Perfis = perfis,
                PerfilPadrao = perfis[0].Nome,
            };
            a.Comum.DadosProjeto = CoordenacaoRevit.CapturarDadosProjeto(_doc);
            a.Comum.Nomenclatura = CoordenacaoRevit.CapturarNomenclatura();
            a.Comum.Ids = ArquivoEmbutido.De(_pacote.Ids);
            a.Comum.PlanilhaClassificacao = ArquivoEmbutido.De(_pacote.Planilha);
            a.Comum.MetroShared = ArquivoEmbutido.De(_pacote.MetroShared);
            if (a.Comum.PlanilhaClassificacao != null) a.Comum.ParametroClassificacao = _pacote.ParametroClassificacao;

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

            // O modelo do coordenador passa a usar o mesmo arquivo, no perfil que acabou de salvar.
            string noModelo;
            if (CoordenacaoRevit.Executar(_doc, "MetroSP: exportar alterações", () => CoordenacaoRevit.Gravar(_doc, a, p.Nome, Path.GetFileName(d.FileName)), out string erro))
            {
                CoordenacaoRevit.Ativar(_doc);
                noModelo = $"\n\nEste modelo também usa o perfil \"{p.Nome}\".";
            }
            else noModelo = "\n\nNão foi possível guardar o arquivo neste modelo: " + erro;
            MessageBox.Show(this, $"Arquivo salvo (rev. {a.Revisao}" + (perfis.Count > 1 ? $", {perfis.Count} perfis" : "") + $"):\n{d.FileName}\n\n" +
                "Envie para o projetista por e-mail, pela pasta do Forma ou pela rede. Ele abre em Entrega passo a passo › Abrir alterações " +
                "(ou no Exportar IFC), e o Exportar IFC passa a usar tudo o que veio nele." + noModelo, Title, MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
    }
}
