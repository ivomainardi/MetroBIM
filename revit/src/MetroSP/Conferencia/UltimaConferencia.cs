using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace MetroSP.Conferencia
{
    /// <summary>
    /// Guarda a última conferência de cada sessão (do painel ou da Conferência completa), para outras janelas mostrarem
    /// os mesmos problemas sem conferir de novo. Ex.: Parâmetros › Parâmetros de projeto marca os parâmetros com problema.
    /// </summary>
    public static class UltimaConferencia
    {
        private static string _chave;
        private static ResultadoConferencia _resultado;

        public static void Guardar(Document doc, ResultadoConferencia r)
        {
            _chave = Chave(doc);
            _resultado = r;
        }

        /// <summary>A última conferência deste modelo, ou null.</summary>
        public static ResultadoConferencia De(Document doc) =>
            doc != null && _resultado != null && _chave == Chave(doc) ? _resultado : null;

        /// <summary>Problemas do passo "Parâmetros e mapeamento" por nome de parâmetro (texto pronto para uma coluna).</summary>
        public static Dictionary<string, string> ProblemasDeParametro(Document doc)
        {
            var r = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            EtapaConferencia etapa = De(doc)?.Etapas.FirstOrDefault(e => e.Numero == 2);
            if (etapa == null) return r;
            foreach (var g in etapa.Pendencias.Where(p => (p.Sinal == Sinal.Vermelho || p.Sinal == Sinal.Amarelo) && !string.IsNullOrEmpty(p.Assunto))
                                              .GroupBy(p => p.Assunto, StringComparer.OrdinalIgnoreCase))
            {
                var partes = new List<string>();
                List<Pendencia> faltas = g.Where(p => p.Correcao == Correcao.ParametrosDeProjeto && !string.IsNullOrEmpty(p.Categoria)).ToList();
                if (faltas.Count > 0)
                    partes.Add("Falta vincular a " + string.Join(", ", faltas.Select(p => $"{p.Categoria} ({p.QtdElementos})")));
                partes.AddRange(g.Except(faltas).Select(p => p.Problema).Where(t => !string.IsNullOrEmpty(t)));
                r[g.Key] = string.Join(" · ", partes.Distinct());
            }
            return r;
        }

        /// <summary>
        /// Pendências do passo 2 que vêm do .txt de property sets (parâmetro que os elementos não têm, ou tipo diferente):
        /// o caminho é ajustar o .txt, não os parâmetros de projeto.
        /// </summary>
        public static List<Pendencia> PendenciasDoMapeamento(Document doc) =>
            De(doc)?.Etapas.FirstOrDefault(e => e.Numero == 2)?.Pendencias
                .Where(p => p.Correcao == Correcao.EditarPropertySets && (p.Sinal == Sinal.Vermelho || p.Sinal == Sinal.Amarelo))
                .OrderByDescending(p => p.QtdElementos).ToList() ?? new List<Pendencia>();

        private static string Chave(Document d) => (d.PathName ?? "") + "|" + d.Title;
    }
}
