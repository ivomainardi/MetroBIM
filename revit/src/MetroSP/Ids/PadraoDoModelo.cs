using System;
using System.Collections.Generic;
using System.Linq;
using DB = Autodesk.Revit.DB;

namespace MetroSP.Ids
{
    /// <summary>
    /// O padrão de exportação (setup, property sets e mapeamento) que vale para um modelo, o mesmo na Entrega
    /// (passo Padrões e conferência) e no Exportar IFC. Ordem: o perfil do arquivo de coordenação do modelo, o
    /// escolhido para este modelo, o indicado pelo nome do modelo (Nomenclatura.txt) e, por último, o último usado.
    /// </summary>
    public static class PadraoDoModelo
    {
        public class Escolha
        {
            public ClassesIfc.Classe Padrao { get; set; }
            /// <summary>De onde veio, para mostrar (ex.: "escolhido para este modelo").</summary>
            public string Motivo { get; set; }
            public bool DaCoordenacao { get; set; }
        }

        /// <summary>Padrão que vale para o modelo, ou null (sem padrão: valem os arquivos escolhidos um a um).</summary>
        public static Escolha De(DB.Document doc, IfcExportSettings s = null)
        {
            if (doc == null) return null;
            s = s ?? IfcExportSettings.Load();
            List<ClassesIfc.Classe> padroes;
            try { padroes = ClassesIfc.Listar(); }
            catch { return null; }
            ClassesIfc.Classe Por(string nome) => string.IsNullOrWhiteSpace(nome) ? null
                : padroes.FirstOrDefault(c => string.Equals(c.Nome, nome.Trim(), StringComparison.CurrentCultureIgnoreCase));

            ClassesIfc.Classe c0 = Por(Coordenacao.CoordenacaoRevit.ClasseAtiva(doc));
            if (c0 != null) return new Escolha { Padrao = c0, Motivo = "do arquivo de coordenação", DaCoordenacao = true };
            string guardado = s.PadraoDo(doc.Title);
            if (guardado == IfcExportSettings.SemPadrao) return null;
            ClassesIfc.Classe c1 = Por(guardado);
            if (c1 != null) return new Escolha { Padrao = c1, Motivo = "escolhido para este modelo" };
            (ClassesIfc.Classe c2, string motivo) = ClassesIfc.DoNomeDoModelo(doc.Title, padroes);
            if (c2 != null) return new Escolha { Padrao = c2, Motivo = motivo };
            ClassesIfc.Classe c3 = Por(s.Classe);
            if (c3 != null) return new Escolha { Padrao = c3, Motivo = "o último usado" };
            return null;
        }

        /// <summary>Guarda o padrão escolhido para o modelo (null = nenhum: valem os arquivos escolhidos um a um).</summary>
        public static void Guardar(DB.Document doc, string padrao)
        {
            if (doc == null) return;
            IfcExportSettings s = IfcExportSettings.Load();
            s.GuardarPadrao(doc.Title, padrao ?? IfcExportSettings.SemPadrao);
            if (padrao != null) s.Classe = padrao;
            s.Save();
        }
    }
}
