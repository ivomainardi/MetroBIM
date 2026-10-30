using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace MetroSP.Ids
{
    /// <summary>
    /// Configurações de IFC por classe que vêm com o instalador (pasta IFC ao lado da DLL), cada uma com o
    /// setup .json, o .txt de property sets e o .txt de mapeamento com o nome da classe. O usuário pode
    /// editar os property sets de uma classe pela janela: a cópia editada fica em %AppData%\MetroSP\IFC
    /// e vale no lugar da padrão até ele restaurar o padrão.
    /// </summary>
    public static class ClassesIfc
    {
        public class Classe
        {
            public string Nome { get; set; }
            public string Setup { get; set; }
            public string PropertySets { get; set; }
            public string Mapeamento { get; set; }
            /// <summary>true quando os property sets são a cópia editada pelo usuário.</summary>
            public bool Editada { get; set; }
            public override string ToString() => Editada ? Nome + "  (editada)" : Nome;
        }

        /// <summary>Pasta IFC instalada junto com o add-in.</summary>
        public static string PastaPadrao =>
            Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "", "IFC");

        /// <summary>Pasta das edições do usuário.</summary>
        public static string PastaUsuario =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MetroSP", "IFC");

        /// <summary>Classes instaladas e as criadas pelo usuário, pelo nome dos arquivos.</summary>
        public static List<Classe> Listar()
        {
            var nomes = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (string raiz in new[] { PastaPadrao, PastaUsuario })
            {
                Nomes(raiz, FormaPaths.SubSetups, "*.json", nomes);
                Nomes(raiz, FormaPaths.SubPropertySets, "*.txt", nomes);
            }
            return nomes.Select(Obter).ToList();
        }

        public static Classe Obter(string nome)
        {
            string psetsUsuario = CaminhoUsuario(nome);
            bool editada = File.Exists(psetsUsuario);
            return new Classe
            {
                Nome = nome,
                Setup = Achar(FormaPaths.SubSetups, nome + ".json"),
                PropertySets = editada ? psetsUsuario : Achar(FormaPaths.SubPropertySets, nome + ".txt"),
                Mapeamento = Achar(FormaPaths.SubMapeamentos, nome + ".txt"),
                Editada = editada,
            };
        }

        /// <summary>Onde fica a cópia editada dos property sets da classe.</summary>
        public static string CaminhoUsuario(string nome) =>
            Path.Combine(PastaUsuario, FormaPaths.SubPropertySets, nome + ".txt");

        /// <summary>Property sets da classe como vieram no instalador (null se a classe não tem .txt padrão).</summary>
        public static string CaminhoPadrao(string nome)
        {
            string f = Path.Combine(PastaPadrao, FormaPaths.SubPropertySets, nome + ".txt");
            return File.Exists(f) ? f : null;
        }

        /// <summary>Apaga a cópia editada; a classe volta a usar o .txt instalado.</summary>
        public static void RestaurarPadrao(string nome)
        {
            string f = CaminhoUsuario(nome);
            if (File.Exists(f)) File.Delete(f);
        }

        // Primeiro o do usuário, depois o instalado.
        private static string Achar(string sub, string arquivo) =>
            new[] { PastaUsuario, PastaPadrao }.Select(r => Path.Combine(r, sub, arquivo)).FirstOrDefault(File.Exists);

        private static void Nomes(string raiz, string sub, string padrao, SortedSet<string> nomes)
        {
            try
            {
                string pasta = Path.Combine(raiz, sub);
                if (!Directory.Exists(pasta)) return;
                foreach (string f in Directory.EnumerateFiles(pasta, padrao)) nomes.Add(Path.GetFileNameWithoutExtension(f));
            }
            catch { /* pasta inacessível */ }
        }
    }
}
