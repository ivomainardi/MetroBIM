using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace MetroSP.Ids
{
    /// <summary>
    /// A partir do Revit 2027 o exportador IFC lê os property sets definidos pelo usuário do próprio modelo
    /// (elementos IFCUserDefinedPropertySet, os mesmos que a janela nativa cria ao importar o .txt) e ignora
    /// o .txt passado nas opções. Por isso o .txt escolhido é carregado no modelo antes de exportar, dentro da
    /// transação da exportação: sem "gravar GUID" ela é desfeita no fim e o modelo fica como estava.
    /// </summary>
    public static class PsetsNoModelo
    {
        /// <summary>
        /// Troca os property sets do usuário guardados no modelo pelos do .txt. Precisa de transação aberta.
        /// Retorna um texto para o relatório, ou null quando a versão do Revit ainda usa o .txt direto.
        /// </summary>
        public static string Carregar(Document doc, string txt)
        {
#if REVIT2027_OR_GREATER
            // Os que já estão no modelo sairiam junto no IFC: o .txt escolhido é a referência.
            var antigos = new List<ElementId>();
            foreach (string nome in IFCUserDefinedPropertySet.ListPropertySetNames(doc))
            {
                IFCUserDefinedPropertySet pset = IFCUserDefinedPropertySet.FindPropertySetByName(doc, nome);
                if (pset != null) antigos.Add(pset.Id);
            }
            if (antigos.Count > 0) doc.Delete(antigos);

            IList<IFCUserDefinedPropertySet> importados = IFCUserDefinedPropertySet.ImportFromFile(doc, txt, out bool valido);
            if (!valido)
                throw new InvalidOperationException("O Revit considerou o .txt de property sets inválido: " + txt);
            int n = importados?.Count ?? 0;
            return $"{n} property sets do .txt carregados no modelo para a exportação" +
                   (antigos.Count > 0 ? $" (substituindo {antigos.Count} que já estavam no modelo)." : ".");
#else
            return null;
#endif
        }
    }
}
