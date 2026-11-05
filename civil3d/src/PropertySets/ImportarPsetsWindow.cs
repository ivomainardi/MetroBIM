using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MetroSP.Planilha;
using MetroSP.Ui;

namespace MetroSP.PropertySets
{
    /// <summary>
    /// Importa a estrutura dos property sets de uma planilha: o usuário indica a aba, a linha dos títulos e qual coluna
    /// traz cada informação (property set, propriedade, tipo...). Só a estrutura; nenhum valor é gravado nos objetos.
    /// As colunas indicadas ficam lembradas para a próxima planilha do mesmo padrão.
    /// </summary>
    public class ImportarPsetsWindow : Window
    {
        private readonly MapaColunasPainel _painel;
        private readonly TextBlock _resumo;
        private readonly Button _importar;

        public List<PsetMontagem> Resultado { get; private set; }

        public ImportarPsetsWindow(string arquivo)
        {
            Title = "MetroSP - Importar property sets";
            Width = 820;
            Height = 600;
            MinWidth = 620;
            MinHeight = 440;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            MapaColunas mapa = PsetsCore.ColunasLembradas();
            if (!string.Equals(mapa.Arquivo, arquivo, System.StringComparison.OrdinalIgnoreCase))
            {
                // Outra planilha: reaproveita as colunas do padrão, mas acha de novo a aba e a linha dos títulos.
                mapa.Arquivo = arquivo;
                mapa.Aba = null;
                mapa.LinhaCabecalho = 0;
            }

            var corpo = new StackPanel();
            var dica = new TextBlock
            {
                Text = "Uma linha por propriedade. Indique a coluna de cada informação.",
                Foreground = Theme.Solid(Theme.Cinza),
                Margin = new Thickness(0, 0, 0, 8),
                ToolTip = "Cria só a estrutura dos property sets (nomes, tipos e a quais objetos se aplicam). Os valores dos objetos não são preenchidos.\n" +
                          "As colunas indicadas ficam lembradas para a próxima importação.",
            };
            corpo.Children.Add(dica);
            _painel = new MapaColunasPainel(mapa, PsetsCore.Campos);
            corpo.Children.Add(_painel);

            _resumo = new TextBlock();
            _importar = new Button { Content = "Importar", Style = Theme.PrimaryButton, ToolTip = "Põe os property sets na lista. Depois, \"Criar no desenho\"." };
            _importar.Click += (s, e) => Importar();
            var cancelar = new Button { Content = "Cancelar", IsCancel = true };
            cancelar.Click += (s, e) => Close();
            _painel.Mudou += Atualizar;

            Theme.Apply(this, "Importar property sets", "planilha com a estrutura do padrão", new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = corpo,
            }, Theme.Rodape(_resumo, _importar, cancelar));
            Atualizar();
        }

        private void Atualizar()
        {
            if (_importar == null) return;
            string pend = _painel.Pendencia();
            if (pend != null)
            {
                _importar.IsEnabled = false;
                Theme.MudarResumo(_resumo, new[] { pend }, "");
                return;
            }
            List<PsetMontagem> lista = PsetsCore.DeLinhas(_painel.Linhas(), out int ignoradas);
            _importar.IsEnabled = lista.Count > 0;
            string texto = $"{lista.Count} property set(s), {lista.Sum(p => p.Propriedades.Count)} propriedade(s)";
            if (ignoradas > 0) texto += $" · {ignoradas} linha(s) sem property set ou propriedade";
            Theme.MudarResumo(_resumo, lista.Count == 0 ? new[] { "nenhuma propriedade nas colunas indicadas" } : null, texto);
        }

        private void Importar()
        {
            Resultado = PsetsCore.DeLinhas(_painel.Linhas(), out _);
            PsetsCore.LembrarColunas(_painel.Mapa);
            DialogResult = true;
        }
    }
}
