using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace MetroSP.Ui
{
    /// <summary>Carrega um PNG embutido em Resources/ (mesmo esquema do MetroSP). Retorna null se não existir.</summary>
    internal static class Icones
    {
        // Prefixo = RootNamespace + pasta Resources.
        private const string Prefixo = "MetroSP.Resources.";

        public static BitmapImage Carregar(string arquivo)
        {
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Prefixo + arquivo);
            if (stream == null) return null;

            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = stream;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
    }
}
