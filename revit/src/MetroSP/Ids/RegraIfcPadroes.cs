using System;
using System.Collections.Generic;
using System.Linq;
using MetroSP.Classificacao;

namespace MetroSP.Ifc
{
    public partial class RegraIfc
    {
        /// <summary>
        /// As classificações de todos os padrões salvos neste computador (sem repetir parâmetro).
        /// O exportador leva cada parâmetro como propriedade com o mesmo nome, em qualquer property set.
        /// </summary>
        public static List<RegraIfc> DosPadroes()
        {
            var r = new List<RegraIfc>();
            foreach (SistemaClassificacao s in PadraoClassificacao.Listar().SelectMany(p => p.Sistemas))
            {
                if (string.IsNullOrWhiteSpace(s.ParametroCodigo)) continue;
                if (r.Any(x => string.Equals(x.PropCodigo, s.ParametroCodigo, StringComparison.OrdinalIgnoreCase))) continue;
                r.Add(new RegraIfc
                {
                    Pset = null,
                    PropCodigo = s.ParametroCodigo,
                    PropDescricao = s.Formato == FormatoClassificacao.Separados && !string.IsNullOrWhiteSpace(s.ParametroDescricao) ? s.ParametroDescricao : null,
                    Formato = s.Formato,
                    Nome = s.Nome,
                    Edicao = s.Edicao,
                });
            }
            return r;
        }
    }
}
