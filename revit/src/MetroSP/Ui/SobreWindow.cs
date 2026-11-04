using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace MetroSP.Ui
{
    /// <summary>Janela Sobre: versão, data da compilação, Revit em uso e as ferramentas da aba MetroSP.</summary>
    public class SobreWindow : Window
    {
        private static readonly Assembly Esta = Assembly.GetExecutingAssembly();

        /// <summary>Versão do add-in, sem o hash que o SDK acrescenta depois do "+".</summary>
        public static string Versao
        {
            get
            {
                string v = Esta.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (string.IsNullOrEmpty(v)) v = Esta.GetName().Version?.ToString(3);
                int mais = v?.IndexOf('+') ?? -1;
                return mais > 0 ? v.Substring(0, mais) : v;
            }
        }

        /// <summary>Data e hora em que a DLL foi compilada (gravada pelo MetroSP.csproj).</summary>
        public static string DataCompilacao
        {
            get
            {
                string d = Esta.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "DataCompilacao")?.Value;
                if (DateTime.TryParseExact(d, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
                    return dt.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR"));
                try { return File.GetLastWriteTime(Esta.Location).ToString("dd/MM/yyyy"); } catch { return "—"; }
            }
        }

        private static readonly (string Painel, string Ferramentas)[] Ferramentas =
        {
            ("Entrega Metrô", "Conferir Entrega (semáforo do que o Metrô pede, pacote .metrosp) e os 4 passos da entrega IFC: 1 Localização, 2 Configurar (Arquivos IFC e Categorias IFC), 3 Verificar Padrões (IDS), 4 Exportar IFC"),
            ("Classificação", "Aplicar Classificação, Verificar Classificação, Consistência Código×Descrição"),
            ("Dados do modelo", "Planilha de Dados (Excel ida e volta), Editar em Massa, Filtrar e Colorir, Copiar Valores, Contar Elementos"),
            ("Parâmetros e famílias", "Menu Parâmetros (Gerenciar, Listar Compartilhados com troca instância ↔ tipo, Renomear Compartilhado) e Renomear Famílias e tipos"),
        };

        /// <summary>Abre a janela sobre o Revit (pela logo da Ribbon ou pelo botão Sobre).</summary>
        public static void Mostrar()
        {
            var w = new SobreWindow();
            try { new WindowInteropHelper(w).Owner = Process.GetCurrentProcess().MainWindowHandle; } catch { }
            w.ShowDialog();
        }

        public SobreWindow()
        {
            Title = "MetroSP - Sobre";
            Width = 520;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var corpo = new StackPanel();
            corpo.Children.Add(new TextBlock
            {
                Text = "Ferramentas para Revit que apoiam a modelagem e a entrega BIM no padrão do Metrô de São Paulo: " +
                       "parâmetros compartilhados, verificação IDS, exportação IFC por classe e classificação.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
            });

            var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            chips.Children.Add(Theme.Chip("Versão " + Versao, escuro: true));
            chips.Children.Add(Theme.Chip("Compilado em " + DataCompilacao, destaque: true));
            if (!string.IsNullOrEmpty(App.VersaoRevit)) chips.Children.Add(Theme.Chip("Revit " + App.VersaoRevit));
            corpo.Children.Add(chips);

            var dados = new Grid();
            dados.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dados.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            void Linha(string rotulo, string valor)
            {
                int r = dados.RowDefinitions.Count;
                dados.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var a = new TextBlock { Text = rotulo, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 12, 3), VerticalAlignment = VerticalAlignment.Top };
                var b = new TextBlock { Text = valor, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3) };
                Grid.SetRow(a, r);
                Grid.SetRow(b, r);
                Grid.SetColumn(b, 1);
                dados.Children.Add(a);
                dados.Children.Add(b);
            }
            Linha("Add-in", "MetroSP para Revit");
            Linha("Desenvolvimento", "Ação BIM para o Metrô de São Paulo");
            Linha("Revit suportado", "2025, 2026 e 2027");
            foreach (var (painel, itens) in Ferramentas) Linha(painel, itens);
            Linha("Instalado em", Path.GetDirectoryName(Esta.Location));
            corpo.Children.Add(dados);

            var pasta = new Button { Content = "Abrir pasta do add-in" };
            pasta.Click += (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Path.GetDirectoryName(Esta.Location) + "\"") { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Title); }
            };
            var copiar = new Button { Content = "Copiar informações" };
            copiar.Click += (s, e) =>
            {
                try { Clipboard.SetText(Resumo()); copiar.Content = "Copiado"; }
                catch { /* área de transferência ocupada */ }
            };
            var fechar = new Button { Content = "Fechar", IsDefault = true, IsCancel = true, Style = Theme.PrimaryButton };
            fechar.Click += (s, e) => Close();

            Theme.Apply(this, "MetroSP para Revit", $"Versão {Versao} · {DataCompilacao}", corpo, Theme.Buttons(pasta, copiar, fechar));
        }

        /// <summary>Texto para colar num e-mail ou chamado de suporte.</summary>
        private static string Resumo()
        {
            var sb = new StringBuilder();
            sb.AppendLine("MetroSP para Revit");
            sb.AppendLine("Versão: " + Versao);
            sb.AppendLine("Compilado em: " + DataCompilacao);
            sb.AppendLine("Revit: " + (App.VersaoRevit ?? "—"));
            sb.AppendLine("Pasta: " + Path.GetDirectoryName(Esta.Location));
            sb.AppendLine("Windows: " + Environment.OSVersion.VersionString);
            return sb.ToString();
        }
    }
}
