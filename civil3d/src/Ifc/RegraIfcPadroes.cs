using System;
using System.Collections.Generic;
using System.Linq;
using MetroSP.Classificacao;

namespace MetroSP.Ifc
{
    public partial class RegraIfc
    {
        /// <summary>As classificações de todos os padrões salvos neste computador (sem repetir property set e propriedade).</summary>
        public static List<RegraIfc> DosPadroes()
        {
            var r = new List<RegraIfc>();
            foreach (SistemaClassificacao s in PadraoClassificacao.Listar().SelectMany(p => p.Sistemas))
            {
                if (string.IsNullOrWhiteSpace(s.PropriedadeCodigo)) continue;
                if (r.Any(x => string.Equals(x.Pset, s.PropertySet, StringComparison.OrdinalIgnoreCase) &&
                               string.Equals(x.PropCodigo, s.PropriedadeCodigo, StringComparison.OrdinalIgnoreCase))) continue;
                r.Add(new RegraIfc
                {
                    Pset = string.IsNullOrWhiteSpace(s.PropertySet) ? null : s.PropertySet,
                    PropCodigo = s.PropriedadeCodigo,
                    PropDescricao = s.Descricao,
                    Formato = s.Formato,
                    Nome = s.Nome,
                    Edicao = s.Edicao,
                });
            }
            return r;
        }
    }
}
