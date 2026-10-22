using System;
using System.IO;
using System.Text;

namespace MetroSP.Parametros
{
    /// <summary>
    /// Edição direta do arquivo .txt de parâmetros compartilhados.
    /// Formato da linha: PARAM \t GUID \t NAME \t DATATYPE \t DATACATEGORY \t GROUP \t VISIBLE ...
    /// </summary>
    public static class SharedParameterFile
    {
        /// <summary>
        /// Troca o nome do parâmetro com o GUID informado, mantendo o restante da linha.
        /// Preserva a codificação original (o Revit costuma gravar em UTF-16).
        /// Retorna false se o GUID não estiver no arquivo.
        /// </summary>
        public static bool Rename(string path, Guid guid, string newName)
        {
            Encoding encoding;
            string[] lines;
            using (var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                string text = reader.ReadToEnd();
                encoding = reader.CurrentEncoding;
                lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            }

            bool found = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string[] cols = lines[i].Split('\t');
                if (cols.Length < 3 || cols[0] != "PARAM") continue;
                if (!Guid.TryParse(cols[1], out Guid g) || g != guid) continue;

                cols[2] = newName;
                lines[i] = string.Join("\t", cols);
                found = true;
            }

            if (!found) return false;

            File.Copy(path, path + ".bak", overwrite: true);
            File.WriteAllText(path, string.Join("\r\n", lines), encoding);
            return true;
        }

        /// <summary>Verifica se já existe outro parâmetro (GUID diferente) com esse nome no arquivo.</summary>
        public static bool NameTakenByOtherGuid(string path, Guid guid, string name)
        {
            foreach (string line in File.ReadLines(path))
            {
                string[] cols = line.Split('\t');
                if (cols.Length < 3 || cols[0] != "PARAM") continue;
                if (cols[2] == name && Guid.TryParse(cols[1], out Guid g) && g != guid) return true;
            }
            return false;
        }
    }
}
