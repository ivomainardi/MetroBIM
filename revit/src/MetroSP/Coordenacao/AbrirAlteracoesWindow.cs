using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MetroSP.Ids;
using MetroSP.Ifc;
using MetroSP.Ui;
using Microsoft.Win32;
using DB = Autodesk.Revit.DB;

namespace MetroSP.Coordenacao
{
    /// <summary>
    /// Abrir alterações: o projetista escolhe o arquivo de coordenação e o perfil. O arquivo fica guardado no modelo e
    /// o Exportar IFC passa a usar o perfil. Abre no contexto da API (grava no modelo).
    /// </summary>
    public class AbrirAlteracoesWindow : Window
    {
        private readonly DB.Document _doc;
        private readonly TextBox _arquivo;
        private readonly TextBlock _info;
        private readonly StackPanel _perfis;
        private readonly TextBlock _conteudo;
        private readonly CheckBox _gravarFamilias;
        private readonly CheckBox _dadosProjeto;
        private readonly TextBlock _resumo;
        private readonly Button _usar;
        private ArquivoCoordenacao _lido;
        private string _perfilEscolhido;

        /// <summary>O que foi feito, para quem abriu a janela mostrar (null = nada).</summary>
        public string Resultado { get; private set; }

        /// <summary>Abre a janela; true se a coordenação do modelo mudou.</summary>
        public static bool Abrir(Window dono, DB.Document doc, string arquivo = null)
        {
            var w = new AbrirAlteracoesWindow(doc, arquivo) { Owner = dono };
            bool ok = w.ShowDialog() == true;
            if (ok && w.Resultado != null) MessageBox.Show(dono, w.Resultado, "MetroSP", MessageBoxButton.OK, MessageBoxImage.Information);
            return ok;
        }

        public AbrirAlteracoesWindow(DB.Document doc, string arquivo = null)
        {
            _doc = doc;
            Title = "MetroSP - Abrir alterações";
            Width = 620;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;

            var corpo = new StackPanel();
            corpo.Children.Add(Theme.Secao("Arquivo de coordenação"));
            var linha = new DockPanel();
            var procurar = new Button { Content = "Procurar...", Margin = new Thickness(6, 0, 0, 0) };
            procurar.Click += (s, e) => Procurar();
            DockPanel.SetDock(procurar, Dock.Right);
            linha.Children.Add(procurar);
            _arquivo = new TextBox { IsReadOnly = true, VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Arquivo .coord-metrosp recebido do coordenador BIM (e-mail, Forma ou rede)." };
            linha.Children.Add(_arquivo);
            corpo.Children.Add(linha);
            _info = Theme.Detalhe();
            _info.Margin = new Thickness(0, 4, 0, 0);
            corpo.Children.Add(_info);

            corpo.Children.Add(Theme.Secao("Perfil"));
            corpo.Children.Add(Theme.Grupo(out _perfis));

            corpo.Children.Add(Theme.Secao("O que entra"));
            _conteudo = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 19 };
            corpo.Children.Add(_conteudo);

            _gravarFamilias = new CheckBox
            {
                Content = "Gravar já no modelo as classes por família",
                Margin = new Thickness(0, 10, 0, 0),
                ToolTip = "Desmarcado: as classes por família do arquivo entram só no IFC, na hora de exportar, e o modelo não muda.\n" +
                          "Marcado: o \"Exportar para IFC como\", o tipo predefinido e o \"Exportar para IFC\" dos tipos ficam gravados no modelo agora.",
            };
            corpo.Children.Add(_gravarFamilias);
            _dadosProjeto = new CheckBox
            {
                Content = "Atualizar Projeto e Local com os dados do arquivo",
                IsChecked = true,
                Margin = new Thickness(0, 6, 0, 0),
                ToolTip = "Grava no modelo os dados do IfcProject, IfcSite, IfcBuilding e do Pset MetroSP_Projeto que vieram no arquivo " +
                          "(só os campos preenchidos no arquivo). Os mesmos campos do botão Projeto e Local.",
            };
            corpo.Children.Add(_dadosProjeto);

            _resumo = new TextBlock();
            _usar = new Button { Content = "Usar este perfil", IsDefault = true, Style = Theme.PrimaryButton, IsEnabled = false };
            _usar.Click += (s, e) => Usar();
            var cancelar = new Button { Content = "Cancelar", IsCancel = true };
            Theme.Apply(this, "Abrir alterações", "O Exportar IFC passa a usar o que o coordenador BIM ajustou", corpo, Theme.Rodape(_resumo, cancelar, _usar));

            // Sem arquivo indicado: o da coordenação atual (para trocar o perfil) ou o mais novo achado perto do modelo.
            ArquivoCoordenacao atual = CoordenacaoRevit.Ler(doc, out string perfilAtual, out string origemAtual);
            if (arquivo != null) Carregar(arquivo);
            else if (atual != null) { _perfilEscolhido = perfilAtual; Mostrar(atual, "(guardado no modelo) " + origemAtual); }
            else
            {
                string achado = Proximos(doc).FirstOrDefault();
                if (achado != null) Carregar(achado);
                else Mostrar(null, null);
            }
        }

        /// <summary>Arquivos de coordenação na pasta do modelo e na pasta do Forma online, do mais novo para o mais antigo.</summary>
        public static List<string> Proximos(DB.Document doc)
        {
            var r = new List<string>();
            try { if (!string.IsNullOrEmpty(doc.PathName)) r.AddRange(ArquivoCoordenacao.Procurar(Path.GetDirectoryName(doc.PathName))); } catch { }
            try
            {
                r.AddRange(ArquivoCoordenacao.Procurar(FormaOnline.PastaCache));
                r.AddRange(ArquivoCoordenacao.Procurar(Path.Combine(FormaOnline.PastaCache, "Coordenacao")));
            }
            catch { }
            return r.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(File.GetLastWriteTimeUtc).ToList();
        }

        private void Procurar()
        {
            var d = new OpenFileDialog { Title = "Abrir arquivo de coordenação", Filter = ArquivoCoordenacao.Filtro };
            try
            {
                string pasta = File.Exists(_arquivo.Text) ? Path.GetDirectoryName(_arquivo.Text) : Path.GetDirectoryName(_doc.PathName);
                if (!string.IsNullOrEmpty(pasta) && Directory.Exists(pasta)) d.InitialDirectory = pasta;
            }
            catch { }
            if (d.ShowDialog(this) == true) Carregar(d.FileName);
        }

        private void Carregar(string arquivo)
        {
            try { Mostrar(ArquivoCoordenacao.Ler(arquivo), arquivo); }
            catch (Exception ex)
            {
                Mostrar(null, arquivo);
                Theme.MudarResumo(_resumo, new[] { ex.Message }, "");
            }
        }

        private void Mostrar(ArquivoCoordenacao a, string origem)
        {
            _lido = a;
            _arquivo.Text = origem ?? "";
            _perfis.Children.Clear();
            _usar.IsEnabled = a != null;
            if (a == null)
            {
                _info.Text = "Escolha o arquivo .coord-metrosp que o coordenador BIM enviou.";
                _conteudo.Text = "";
                _dadosProjeto.Visibility = Visibility.Collapsed;
                Theme.MudarResumo(_resumo, new[] { "nenhum arquivo escolhido" }, "");
                return;
            }
            _info.Text = $"rev. {a.Revisao} · {a.Autor ?? "?"} · {CoordenacaoRevit.DataCurta(a.Data)}" +
                         (string.IsNullOrWhiteSpace(a.Nota) ? "" : " · " + a.Nota) +
                         (string.IsNullOrWhiteSpace(a.ModeloOrigem) ? "" : "\nSaiu do modelo " + a.ModeloOrigem);
            string escolhido = a.Perfil(_perfilEscolhido)?.Nome;
            bool primeiro = true;
            foreach (PerfilCoordenacao p in a.Perfis)
            {
                var radio = new RadioButton { Content = p.Nome, GroupName = "perfil", IsChecked = p.Nome == escolhido, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                PerfilCoordenacao este = p;
                radio.Checked += (s, e) => { _perfilEscolhido = este.Nome; MostrarPerfil(); };
                var chips = new StackPanel { Orientation = Orientation.Horizontal };
                if (p.Esquema != null) chips.Children.Add(Theme.Chip(p.Esquema));
                if (p.Nome == a.PerfilPadrao && a.Perfis.Count > 1) chips.Children.Add(Theme.Chip("padrão", escuro: true));
                chips.Children.Add(Theme.Chip(p.Alteracoes + " ajustes", destaque: true));
                _perfis.Children.Add(Theme.Item("Classe " + (p.Classe ?? "?"), radio, chips, null, primeiro: primeiro));
                primeiro = false;
            }
            _perfilEscolhido = escolhido;
            _dadosProjeto.Visibility = a.Comum.DadosProjeto != null ? Visibility.Visible : Visibility.Collapsed;
            MostrarPerfil();
        }

        private void MostrarPerfil()
        {
            PerfilCoordenacao p = _lido?.Perfil(_perfilEscolhido);
            if (p == null) return;
            _conteudo.Text = string.Join("\n", Descrever(p, _doc));
            _gravarFamilias.IsEnabled = p.Familias.Count + p.Elementos.Count > 0;
            var avisos = new List<string>();
            if (p.Fase != null && CoordenacaoRevit.FasePeloNome(_doc, p.Fase) < 0) avisos.Add($"a fase \"{p.Fase}\" não existe neste modelo");
            if (ClassesIfc.Listar().All(c => !string.Equals(c.Nome, p.Classe, StringComparison.CurrentCultureIgnoreCase)) && p.Exportador == null)
                avisos.Add($"classe {p.Classe} sem setup");
            Theme.MudarResumo(_resumo, avisos, $"Perfil \"{p.Nome}\": {p.Alteracoes} ajustes");
        }

        /// <summary>Linhas que descrevem o perfil (para a janela e as dicas).</summary>
        public static List<string> Descrever(PerfilCoordenacao p, DB.Document doc)
        {
            var l = new List<string>();
            if (p.Exportador != null)
            {
                string nome = p.Exportador["Name"]?.ToString();
                l.Add($"• Setup do exportador{(string.IsNullOrEmpty(nome) ? "" : " \"" + nome + "\"")}" + (p.Esquema != null ? $" ({p.Esquema})" : ""));
            }
            if (p.Fase != null)
                l.Add($"• Fase: {p.Fase}" + (doc != null && CoordenacaoRevit.FasePeloNome(doc, p.Fase) < 0 ? " (não existe neste modelo: vale o padrão do exportador)" : ""));
            foreach (KeyValuePair<string, List<LinhaCategoria>> t in p.Categorias.Where(k => k.Value != null && k.Value.Count > 0))
                l.Add($"• Categorias IFC {t.Key}: {t.Value.Count} categorias, {t.Value.Count(x => !x.Exportar)} sem exportar");
            if (p.Familias.Count > 0) l.Add($"• Classe IFC em {p.Familias.Count} tipo(s) de família");
            if (p.Elementos.Count > 0) l.Add($"• Classe IFC em {p.Elementos.Count} elemento(s) (só valem no modelo de origem)");
            if (p.PropertySets != null) l.Add("• Property sets (.txt)");
            if (p.Mapeamento != null) l.Add("• Mapeamento de parâmetros (.txt)");
            if (l.Count == 0) l.Add("O perfil não traz ajustes.");
            return l;
        }

        private void Usar()
        {
            PerfilCoordenacao p = _lido?.Perfil(_perfilEscolhido);
            if (p == null) return;
            ArquivoCoordenacao a = _lido;
            bool familias = _gravarFamilias.IsEnabled && _gravarFamilias.IsChecked == true;
            bool dados = a.Comum.DadosProjeto != null && _dadosProjeto.IsChecked == true;
            string origem = File.Exists(_arquivo.Text) ? Path.GetFileName(_arquivo.Text)
                : CoordenacaoRevit.Ler(_doc, out _, out string o) != null ? o : "";
            var feito = new List<string> { $"{p.Nome} (rev. {a.Revisao}) em uso neste modelo. O Exportar IFC passa a usar este perfil." };
            bool ok = CoordenacaoRevit.Executar(_doc, "MetroSP: abrir alterações", () =>
            {
                CoordenacaoRevit.Gravar(_doc, a, p.Nome, origem);
                if (familias)
                {
                    var (tipos, elementos, nao) = CoordenacaoRevit.AplicarAjustes(_doc, p);
                    feito.Add($"Classe IFC gravada em {tipos} tipo(s)" + (elementos > 0 ? $" e {elementos} elemento(s)" : "") +
                              (nao.Count > 0 ? $"; {nao.Count} não encontrado(s) neste modelo." : "."));
                }
                if (dados)
                {
                    DadosProjetoIfc novo = DadosProjetoIfc.DeJson(a.Comum.DadosProjeto.ToJsonString());
                    if (novo != null)
                    {
                        DadosProjetoIfc antes = DadosProjetoRevit.Ler(_doc);
                        DadosProjetoIfc mesclado = antes.Copia();
                        mesclado.Completar(novo);   // campos preenchidos no arquivo valem; os vazios não apagam nada
                        var mudou = new List<string>();
                        DadosProjetoRevit.Gravar(_doc, antes, mesclado, mudou);
                        if (mudou.Count > 0) feito.Add("Projeto e Local: " + string.Join(", ", mudou) + ".");
                    }
                }
            }, out string erro);
            if (!ok)
            {
                MessageBox.Show(this, "Não foi possível guardar a coordenação no modelo:\n" + erro, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            CoordenacaoRevit.Ativar(_doc);
            if (!familias && p.Familias.Count + p.Elementos.Count > 0)
                feito.Add("As classes por família entram só no IFC, na hora de exportar (o modelo não mudou).");
            Resultado = string.Join("\n", feito);
            DialogResult = true;
        }
    }
}
