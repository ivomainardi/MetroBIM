using System.Windows;
using System.Windows.Controls;
using MetroSP.Ui;

namespace MetroSP.Ids
{
    /// <summary>Pede o Client ID do app do MetroSP na Autodesk, usado para entrar no Forma online.</summary>
    public class FormaClientIdWindow : Window
    {
        private readonly TextBox _id;
        private readonly TextBlock _aviso;

        public string ClientId => _id.Text.Trim();

        public FormaClientIdWindow()
        {
            Title = "MetroSP - Client ID do Forma online";
            Width = 560;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var corpo = new StackPanel();
            corpo.Children.Add(new TextBlock
            {
                Text = "O Client ID identifica o app do MetroSP na Autodesk (aps.autodesk.com › My Apps). " +
                       "Ele fica salvo só para você neste computador.",
                TextWrapping = TextWrapping.Wrap,
            });
            corpo.Children.Add(Theme.Label("Client ID"));
            _id = new TextBox { Text = FormaOnline.ClientIdDoUsuario ?? FormaOnline.ClientId ?? "", FontFamily = new System.Windows.Media.FontFamily("Consolas") };
            _id.TextChanged += (s, e) => Conferir();
            corpo.Children.Add(_id);
            _aviso = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            corpo.Children.Add(_aviso);
            corpo.Children.Add(new TextBlock
            {
                Text = "Para criar o app: tipo \"Desktop, Mobile, Single-Page App\", callback " + FormaOnline.Callback +
                       " e API Data Management. O administrador da conta ACC precisa liberar o Client ID em " +
                       "Account Admin › Custom Integrations. Deixe em branco para voltar ao Client ID instalado.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Theme.Solid(Theme.Cinza),
                Margin = new Thickness(0, 10, 0, 0),
            });

            var ok = new Button { Content = "Salvar", IsDefault = true, Style = Theme.PrimaryButton };
            ok.Click += (s, e) =>
            {
                if (ClientId.Length > 0 && !FormaOnline.ClientIdValido(ClientId))
                {
                    MessageBox.Show(this, "O Client ID tem só letras e números (normalmente 32). Confira o valor copiado.", Title);
                    return;
                }
                DialogResult = true;
            };
            var cancelar = new Button { Content = "Cancelar", IsCancel = true };
            Theme.Apply(this, "Client ID do Forma online", "App do MetroSP na Autodesk Platform Services", corpo, Theme.Buttons(cancelar, ok));
            Loaded += (s, e) => { _id.Focus(); _id.SelectAll(); Conferir(); };
        }

        private void Conferir()
        {
            string id = ClientId;
            bool vazio = id.Length == 0, valido = FormaOnline.ClientIdValido(id);
            _aviso.Text = vazio ? "Sem Client ID o Forma online fica desligado." : valido ? $"{id.Length} caracteres." : "Formato inesperado: use só letras e números.";
            _aviso.Foreground = Theme.Solid(vazio || valido ? Theme.Cinza : Theme.Laranja);
        }
    }
}
