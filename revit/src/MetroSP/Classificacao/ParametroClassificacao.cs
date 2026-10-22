using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MetroSP.Parametros;

namespace MetroSP.Classificacao
{
    /// <summary>
    /// Cria ou amplia o vínculo do parâmetro de classificação às categorias que ainda não o têm.
    /// Se o parâmetro já é de projeto, só acrescenta as categorias (mantém GUID, instância/tipo e valores).
    /// Se não, procura a definição (mesmo GUID do que já existe no arquivo, senão pelo nome) na base Metrô,
    /// nos .txt de referência e no arquivo configurado no Revit.
    /// </summary>
    public static class ParametroClassificacao
    {
        /// <summary>Categorias (de modelo, que aceitam parâmetros) pelos nomes.</summary>
        public static List<Category> Categorias(Document doc, IEnumerable<string> nomes)
        {
            var set = new HashSet<string>(nomes.Where(n => !string.IsNullOrEmpty(n)));
            return doc.Settings.Categories.Cast<Category>()
                .Where(c => set.Contains(c.Name) && c.AllowsBoundParameters && c.CategoryType == CategoryType.Model)
                .ToList();
        }

        /// <summary>Faz o vínculo. Retorna a mensagem para o usuário, ou null se o usuário cancelou.</summary>
        public static string Vincular(Document doc, string nome, List<Category> categorias)
        {
            if (categorias.Count == 0) return "Nenhuma das categorias aceita parâmetros de projeto.";
            Application app = doc.Application;
            string lista = string.Join(", ", categorias.Select(c => c.Name).OrderBy(n => n));

            // 1. Já é parâmetro de projeto: acrescenta as categorias.
            DefinitionBindingMapIterator it = doc.ParameterBindings.ForwardIterator();
            while (it.MoveNext())
            {
                if (it.Key?.Name != nome || !(it.Current is ElementBinding vinculo)) continue;
                string modo = vinculo is TypeBinding ? "tipo" : "instância";
                if (!Confirmar($"Acrescentar {nome} (parâmetro de {modo}, como já está no projeto) às categorias:\n{lista}?")) return null;
                foreach (Category c in categorias) vinculo.Categories.Insert(c);
                using (var t = new Transaction(doc, "MetroSP: vincular " + nome))
                {
                    t.Start();
                    bool ok = doc.ParameterBindings.ReInsert(it.Key, vinculo, it.Key.GetGroupTypeId());
                    t.Commit();
                    if (!ok) throw new InvalidOperationException("O Revit recusou ampliar o vínculo.");
                }
                return $"{nome} vinculado a: {lista}.";
            }

            // 2. Procura a definição nos .txt.
            Guid? guid = new FilteredElementCollector(doc).OfClass(typeof(SharedParameterElement)).Cast<SharedParameterElement>()
                .FirstOrDefault(p => p.Name == nome)?.GuidValue;
            var lib = ReferenceLibrary.Abrir();
            lib.Carregar(app.SharedParametersFilename);
            var arquivos = lib.Arquivos.ToList();
            string revit = app.SharedParametersFilename;
            if (!string.IsNullOrEmpty(revit) && File.Exists(revit) && !arquivos.Any(a => string.Equals(a.Caminho, revit, StringComparison.OrdinalIgnoreCase)))
            {
                SharedParamTxt t = SharedParamTxt.Ler(revit);
                t.EhRevitAtual = true;
                arquivos.Add(t);
            }
            SharedParamDef def = guid != null
                ? arquivos.Select(a => a.PorGuid.TryGetValue(guid.Value, out SharedParamDef d) ? d : null).FirstOrDefault(d => d != null)
                : arquivos.Select(a => a.PorNome.TryGetValue(nome, out SharedParamDef d) ? d : null).FirstOrDefault(d => d != null);
            if (def == null)
                return guid != null
                    ? $"O arquivo já tem {nome} (GUID {guid}), mas esse GUID não está em nenhum .txt de referência. " +
                      "Adicione o .txt que o contém em Listar Compartilhados → Comparar com .txt e tente de novo."
                    : $"{nome} não está em nenhum .txt de referência nem no arquivo configurado no Revit. " +
                      "Adicione o .txt da base Metrô (ou o IFC Shared Parameters) em Listar Compartilhados → Comparar com .txt.";
            if (def.TipoDeDado != null && !def.TipoDeDado.Equals("TEXT", StringComparison.OrdinalIgnoreCase))
                return $"{nome} em {Path.GetFileName(def.Arquivo.Caminho)} é do tipo {def.TipoDeDado}; a classificação precisa de texto.";

            bool? instancia = PerguntarModo(nome, lista, def);
            if (instancia == null) return null;

            string original = app.SharedParametersFilename;
            try
            {
                app.SharedParametersFilename = def.Arquivo.Caminho;
                DefinitionFile arquivo = app.OpenSharedParameterFile()
                    ?? throw new InvalidOperationException("O Revit não conseguiu abrir " + def.Arquivo.Caminho);
                ExternalDefinition externa = arquivo.Groups.SelectMany(g => g.Definitions.OfType<ExternalDefinition>())
                    .FirstOrDefault(d => d.GUID == def.Guid)
                    ?? throw new InvalidOperationException("Definição não encontrada pelo Revit em " + def.Arquivo.Caminho);
                CategorySet cats = app.Create.NewCategorySet();
                foreach (Category c in categorias) cats.Insert(c);
                ElementBinding novo = instancia == true ? (ElementBinding)app.Create.NewInstanceBinding(cats) : app.Create.NewTypeBinding(cats);
                using (var t = new Transaction(doc, "MetroSP: criar " + nome))
                {
                    t.Start();
                    bool ok = doc.ParameterBindings.Insert(externa, novo, GroupTypeId.Ifc);
                    t.Commit();
                    if (!ok) throw new InvalidOperationException("O Revit recusou criar o parâmetro.");
                }
            }
            finally
            {
                try { app.SharedParametersFilename = original ?? ""; } catch { }
            }
            return $"{nome} criado como parâmetro de {(instancia == true ? "instância" : "tipo")} (GUID {def.Guid}, de {Path.GetFileName(def.Arquivo.Caminho)}) em: {lista}.";
        }

        private static bool Confirmar(string texto)
        {
            var td = new TaskDialog("MetroSP")
            {
                MainInstruction = "Vincular parâmetro de classificação",
                MainContent = texto,
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.Yes,
            };
            return td.Show() == TaskDialogResult.Yes;
        }

        /// <summary>true = instância, false = tipo, null = cancelou.</summary>
        private static bool? PerguntarModo(string nome, string lista, SharedParamDef def)
        {
            var td = new TaskDialog("MetroSP")
            {
                MainInstruction = $"Criar {nome} como parâmetro de projeto",
                MainContent = $"Categorias: {lista}\n\nDefinição: {Path.GetFileName(def.Arquivo.Caminho)}, GUID {def.Guid}, grupo IFC.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Instância", "Cada elemento recebe sua própria classificação (recomendado para aplicar por seleção).");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Tipo", "A classificação vale para todas as instâncias do tipo.");
            TaskDialogResult r = td.Show();
            return r == TaskDialogResult.CommandLink1 ? true : r == TaskDialogResult.CommandLink2 ? false : (bool?)null;
        }
    }
}
