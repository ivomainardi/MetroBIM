using System;
using System.IO;

namespace MetroSP.Ids
{
    /// <summary>
    /// Marca em disco a etapa em andamento da exportação IFC. Se o Revit fechar no meio, a próxima exportação lê a
    /// marca e põe no registro onde parou (o journal do Revit não mostra o que o add-in fazia).
    /// </summary>
    internal static class RastroExportacao
    {
        private static string Arquivo =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MetroSP", "exportar-ifc-em-andamento.txt");

        public static void Passo(string passo)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Arquivo));
                File.WriteAllText(Arquivo, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " · " + passo);
            }
            catch { /* sem disco: segue sem a marca */ }
        }

        /// <summary>A etapa em que a exportação anterior parou (o Revit fechou), ou null; apaga a marca.</summary>
        public static string Interrompida()
        {
            try
            {
                if (!File.Exists(Arquivo)) return null;
                string s = File.ReadAllText(Arquivo).Trim();
                File.Delete(Arquivo);
                return s.Length == 0 ? null : s;
            }
            catch { return null; }
        }

        public static void Fim()
        {
            try { if (File.Exists(Arquivo)) File.Delete(Arquivo); } catch { }
        }
    }
}
