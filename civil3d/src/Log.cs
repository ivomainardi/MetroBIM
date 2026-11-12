using System;
using System.IO;

namespace MetroSP
{
    /// <summary>Erros com o detalhe completo em %APPDATA%\MetroSP\erros.log, para mandar ao suporte.</summary>
    internal static class Log
    {
        public static string Path => System.IO.Path.Combine(Settings.UserDir, "erros.log");

        /// <summary>Grava o erro e devolve o caminho do arquivo (null se não deu para gravar).</summary>
        public static string Erro(string onde, Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Settings.UserDir);
                var versao = typeof(Log).Assembly.GetName().Version;
                File.AppendAllText(Path, $"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} · MetroSP {versao} · {onde}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
                return Path;
            }
            catch { return null; }
        }
    }
}
