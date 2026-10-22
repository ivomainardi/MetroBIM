using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace MetroSP.Ids
{
    /// <summary>
    /// Localiza no computador as pastas do Autodesk Forma (Autodesk Docs). O Revit não lê arquivos soltos
    /// da nuvem pela API; o Desktop Connector espelha os projetos em %USERPROFILE%\DC\ACCDocs\Hub\Projeto,
    /// e é por esse caminho que o .json e o .txt são lidos.
    /// </summary>
    public static class FormaPaths
    {
        /// <summary>
        /// Pasta padrão, dentro de cada projeto, com as configurações de exportação IFC. Fica relativa à pasta
        /// do projeto para valer em qualquer projeto e qualquer máquina.
        /// </summary>
        public const string PastaPadraoInicial = @"Project Files\BIM\Configuracoes IFC";
        public const string SubSetups = "Setups";
        public const string SubPropertySets = "PropertySets";
        public const string SubMapeamentos = "Mapeamentos";

        /// <summary>Um projeto do Forma sincronizado no computador.</summary>
        public class Projeto
        {
            public string Hub { get; set; }
            public string Nome { get; set; }
            public string Caminho { get; set; }
            public override string ToString() => $"{Nome}  ({Hub})";
        }

        /// <summary>Projetos sincronizados: Raiz\Hub\Projeto.</summary>
        public static List<Projeto> Projetos(string raiz)
        {
            var lista = new List<Projeto>();
            if (string.IsNullOrEmpty(raiz)) return lista;
            try
            {
                foreach (string hub in Directory.EnumerateDirectories(raiz))
                    foreach (string projeto in Directory.EnumerateDirectories(hub))
                        lista.Add(new Projeto { Hub = Path.GetFileName(hub), Nome = Path.GetFileName(projeto), Caminho = projeto });
            }
            catch { /* pasta inacessível */ }
            return lista.OrderBy(p => p.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>Cria a pasta padrão com as subpastas e um LEIA-ME. O Desktop Connector envia para o Forma.</summary>
        public static void CriarPastaPadrao(string pasta)
        {
            foreach (string sub in new[] { SubSetups, SubPropertySets, SubMapeamentos })
                Directory.CreateDirectory(Path.Combine(pasta, sub));
            string leiame = Path.Combine(pasta, "LEIA-ME.txt");
            if (!File.Exists(leiame))
                File.WriteAllText(leiame,
                    "Configurações de exportação IFC usadas pelo botão MetroSP > Exportar IFC.\r\n\r\n" +
                    SubSetups + "\\        setups .json salvos no Revit (Exportar > IFC > Modificar configuração > Salvar configuração)\r\n" +
                    SubPropertySets + "\\  .txt de property sets definidos pelo usuário\r\n" +
                    SubMapeamentos + "\\   .txt de mapeamento de parâmetros (opcional)\r\n\r\n" +
                    "Dê ao .txt o mesmo nome do setup (ex.: Civil.json, Civil.txt) para ele ser escolhido sozinho.\r\n",
                    System.Text.Encoding.UTF8);
        }

        /// <summary>Pasta raiz do Desktop Connector (ACCDocs), ou null se não estiver instalado.</summary>
        public static string Raiz()
        {
            string perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] candidatas =
            {
                Path.Combine(perfil, "DC", "ACCDocs"),
                Path.Combine(perfil, "ACCDocs"),
                Path.Combine(perfil, "BIM 360"),
            };
            return candidatas.FirstOrDefault(Directory.Exists);
        }

        /// <summary>Nome do projeto no Forma do modelo aberto, se ele estiver na nuvem.</summary>
        public static string ProjetoDoModelo(Document doc)
        {
            try
            {
                if (doc == null || !doc.IsModelInCloud) return null;
                string visivel = ModelPathUtils.ConvertModelPathToUserVisiblePath(doc.GetCloudModelPath());
                // "Autodesk Docs://Projeto/Pasta/Modelo.rvt" ou "BIM 360://Projeto/Modelo.rvt"
                int i = visivel.IndexOf("://", StringComparison.Ordinal);
                if (i < 0) return null;
                string resto = visivel.Substring(i + 3);
                int barra = resto.IndexOf('/');
                return barra > 0 ? resto.Substring(0, barra) : null;
            }
            catch { return null; }
        }

        /// <summary>Caminho curto para mostrar: "Forma › Hub › Projeto › Pasta".</summary>
        public static string Descrever(string raiz, string pasta)
        {
            if (string.IsNullOrEmpty(raiz) || string.IsNullOrEmpty(pasta)) return pasta;
            string r = raiz.TrimEnd('\\', '/');
            if (!pasta.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return pasta;
            IEnumerable<string> partes = pasta.Substring(r.Length).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" › ", new[] { "Forma" }.Concat(partes));
        }
    }
}
