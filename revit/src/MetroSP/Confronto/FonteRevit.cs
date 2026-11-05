using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MetroSP.Dados;
using MetroSP.Parametros;

namespace MetroSP.Confronto
{
    /// <summary>Confrontar com planilha no Revit: elementos que vão para o IFC (FiltroIfc) e seus parâmetros de instância e de tipo.</summary>
    public class FonteRevit : IFonteConfronto
    {
        private const string Id = "@id", UniqueId = "@uid", Familia = "@familia", Tipo = "@tipo", FamiliaTipo = "@famtipo", Categoria = "@categoria";

        private readonly UIDocument _uidoc;
        private readonly Document _doc;
        private readonly EscopoDados _escopo;
        private readonly Dictionary<string, ParamRef> _refs = new Dictionary<string, ParamRef>();

        public FonteRevit(UIDocument uidoc)
        {
            _uidoc = uidoc;
            _doc = uidoc.Document;
            _escopo = new EscopoDados(uidoc);
            _escopo.Mudou += () => EscopoMudou?.Invoke();
        }

        public string Programa => "Revit";
        public string NomeDocumento => _doc.Title;
        public string Termo => "elemento";
        public FrameworkElement PainelEscopo => _escopo.Painel;
        public event Action EscopoMudou;

        public IList<ObjetoConfronto> Objetos() => _escopo.Elementos().Select(e => new ObjetoConfronto
        {
            Ref = e,
            Id = e.Id.Value.ToString(CultureInfo.InvariantCulture),
            Nome = Valores.Nome(_doc, e),
            Categoria = e.Category?.Name,
        }).ToList();

        public IList<CampoConfronto> Campos()
        {
            var r = new List<CampoConfronto>
            {
                new CampoConfronto { Chave = Id, Nome = "Id do elemento", Grupo = "Revit" },
                new CampoConfronto { Chave = UniqueId, Nome = "UniqueId (GUID)", Grupo = "Revit" },
                new CampoConfronto { Chave = Familia, Nome = "Família", Grupo = "Revit" },
                new CampoConfronto { Chave = Tipo, Nome = "Tipo", Grupo = "Revit" },
                new CampoConfronto { Chave = FamiliaTipo, Nome = "Família: Tipo", Grupo = "Revit" },
                new CampoConfronto { Chave = Categoria, Nome = "Categoria", Grupo = "Revit" },
            };
            _refs.Clear();
            foreach (ParamRef p in ValueCopier.Candidatos(_doc, _escopo.Elementos()).Where(p => p.Storage != StorageType.None))
            {
                _refs[p.Chave] = p;
                r.Add(new CampoConfronto
                {
                    Chave = p.Chave,
                    Nome = p.Nome,
                    Grupo = p.DoTipo ? "tipo" : "instância",
                    Detalhe = p.TipoLabel + " · " + p.Origem,
                    Editavel = p.Editavel && p.Storage != StorageType.ElementId,
                });
            }
            return r;
        }

        private static Element E(ObjetoConfronto o) => (Element)o.Ref;

        public ValorConfronto Ler(ObjetoConfronto o, CampoConfronto c)
        {
            Element e = E(o);
            switch (c.Chave)
            {
                case Id: return Fixo(o.Id);
                case UniqueId: return Fixo(e.UniqueId);
                case Familia: return Fixo(Valores.Familia(_doc, e));
                case Tipo: return Fixo(Valores.Tipo(_doc, e));
                case FamiliaTipo: return Fixo(Valores.Nome(_doc, e));
                case Categoria: return Fixo(e.Category?.Name ?? "");
            }
            if (!_refs.TryGetValue(c.Chave, out ParamRef r)) return new ValorConfronto();
            var (p, dono) = r.Resolver(_doc, e);
            if (p == null) return new ValorConfronto { Motivo = $"Acrescentar \"{r.Nome}\" a esta categoria ({(r.DoTipo ? "tipo" : "instância")})" };
            var v = new ValorConfronto { Existe = true, Texto = Valores.Texto(_doc, p), Editavel = Valores.Editavel(p) };
            if (!v.Editavel) v.Motivo = p.StorageType == StorageType.ElementId ? "parâmetro de referência (escolher no Revit)" : "somente leitura (fórmula ou parâmetro do Revit)";
            else if (_doc.IsWorkshared && WorksharingUtils.GetCheckoutStatus(_doc, dono.Id) == CheckoutStatus.OwnedByOtherUser)
            {
                v.Editavel = false;
                v.Motivo = "emprestado para outro usuário";
            }
            if (r.DoTipo && dono != null)
            {
                v.Dono = dono.Id.Value.ToString(CultureInfo.InvariantCulture);
                v.DonoRotulo = "Tipo " + Valores.Nome(_doc, e);
            }
            return v;
        }

        private static ValorConfronto Fixo(string texto) => new ValorConfronto { Existe = true, Texto = texto ?? "", Motivo = "dado do Revit, não é parâmetro" };

        private Parameter Param(ObjetoConfronto o, CampoConfronto c) =>
            _refs.TryGetValue(c.Chave, out ParamRef r) ? r.Resolver(_doc, E(o)).Param : null;

        public bool Iguais(ObjetoConfronto o, CampoConfronto c, string modelo, string planilha)
        {
            Parameter p = Param(o, c);
            if (p == null || !p.HasValue || p.StorageType == StorageType.String || p.StorageType == StorageType.ElementId) return false;
            if (!Valores.Converter(_doc, p, planilha, out object valor, out _)) return false;
            if (valor is int i) return p.StorageType == StorageType.Integer && p.AsInteger() == i;
            if (valor is double d && p.StorageType == StorageType.Double)
            {
                // Igual na precisão das unidades do projeto ("2,5" = "2,50 m").
                ForgeTypeId spec = Valores.Spec(p);
                if (spec != null && UnitUtils.IsMeasurableSpec(spec))
                {
                    try
                    {
                        Units u = _doc.GetUnits();
                        return UnitFormatUtils.Format(u, spec, d, false) == UnitFormatUtils.Format(u, spec, p.AsDouble(), false);
                    }
                    catch { /* sem formato */ }
                }
                return Math.Abs(p.AsDouble() - d) <= 1e-6 * Math.Max(1, Math.Abs(d));
            }
            return false;
        }

        public bool Preparar(ObjetoConfronto o, CampoConfronto c, string planilha, out object valor, out string motivo)
        {
            valor = null;
            Parameter p = Param(o, c);
            if (p == null) { motivo = "não é parâmetro"; return false; }
            return Valores.Converter(_doc, p, planilha, out valor, out motivo);
        }

        public (int Ok, int Falhas) Gravar(IList<Gravacao> gravacoes)
        {
            int ok = 0, falhas = 0;
            using (var t = new Transaction(_doc, "MetroSP: Confrontar com planilha"))
            {
                t.Start();
                foreach (Gravacao g in gravacoes)
                {
                    g.Ok = Valores.Gravar(Param(g.Objeto, g.Campo), g.Valor);
                    if (g.Ok) ok++; else falhas++;
                }
                t.Commit();
            }
            return (ok, falhas);
        }

        public void Selecionar(IEnumerable<ObjetoConfronto> objetos, bool mostrar)
        {
            var ids = objetos.Select(o => E(o).Id).Where(id => _doc.GetElement(id) != null).Distinct().ToList();
            _uidoc.Selection.SetElementIds(ids);
            if (mostrar && ids.Count > 0)
            {
                try { _uidoc.ShowElements(ids); } catch { /* sem vista que mostre */ }
            }
        }
    }
}
