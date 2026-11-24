using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using MetroSP.Ifc;

namespace MetroSP.Ids
{
    /// <summary>
    /// Barras de cada armadura do Revit (Rebar, Rebar de Area/Path Reinforcement, Rebar Container), na mesma ordem em que
    /// o exportador IFC da Autodesk cria os IfcReinforcingBar: posição a posição, pulando as que não existem (primeira
    /// ou última barra desligada). Comprimento de cada barra: o "Bar Length" da posição nos conjuntos variáveis, ou o
    /// comprimento total dividido pelo número de barras nos uniformes. Peso: área do diâmetro nominal × comprimento ×
    /// densidade do material (aço, 7850 kg/m³, quando o material não tem densidade).
    /// </summary>
    internal static class ArmaduraRevit
    {
        private const double Pe = 0.3048;            // pé → metro
        private const double DensidadeAco = 7850;    // kg/m³

        public static IReadOnlyList<BarraRevit> Barras(Document doc, long id)
        {
            try
            {
                Element e = doc.GetElement(Compat.NovoId(id));
                switch (e)
                {
                    case Rebar rebar: return DoRebar(doc, rebar);
                    case RebarInSystem ris:
                        return Uniformes(ris.NumberOfBarPositions, ris.DoesBarExistAtPosition, ris.TotalLength, ris.Quantity,
                            KgPorMetro(doc, ris, ris.GetTypeId()));
                    case RebarContainer rc:
                        var lista = new List<BarraRevit>();
                        foreach (RebarContainerItem item in rc)
                            lista.AddRange(Uniformes(item.NumberOfBarPositions, item.DoesBarExistAtPosition, item.TotalLength,
                                item.Quantity, KgPorMetro(doc, rc, item.BarTypeId)));
                        return lista;
                    // Armadura de área ou de caminho: o exportador pode marcar as barras com o Id do conjunto.
                    case AreaReinforcement ar: return DoSistema(doc, ar.GetRebarInSystemIds());
                    case PathReinforcement pr: return DoSistema(doc, pr.GetRebarInSystemIds());
                    default: return null;
                }
            }
            catch { return null; }   // elemento que não é armadura, ou API que falhou: fica sem as quantidades
        }

        /// <summary>Diâmetro, superfície, estilo, forma, material e plano do anel de uma armadura (null = não é armadura).</summary>
        public static ArmaduraDadosRevit Dados(Document doc, long id)
        {
            try
            {
                Element e = doc.GetElement(Compat.NovoId(id));
                ElementId tipoId;
                switch (e)
                {
                    case Rebar rebar: tipoId = rebar.GetTypeId(); break;
                    case RebarInSystem ris: tipoId = ris.GetTypeId(); break;
                    case RebarContainer rc:
                        tipoId = ElementId.InvalidElementId;
                        foreach (RebarContainerItem item in rc) { tipoId = item.BarTypeId; break; }
                        break;
                    case AreaReinforcement ar: tipoId = PrimeiroTipo(doc, ar.GetRebarInSystemIds()); break;
                    case PathReinforcement pr: tipoId = PrimeiroTipo(doc, pr.GetRebarInSystemIds()); break;
                    default: return null;
                }
                var d = new ArmaduraDadosRevit();
                if (doc.GetElement(tipoId) is RebarBarType tipo)
                {
                    d.Diametro = tipo.BarNominalDiameter * Pe;
                    try { d.Nervurada = tipo.DeformationType == RebarDeformationType.Deformed; } catch { }
                    d.Material = NomeMaterial(doc, e, tipo);
                }
                if (e is Rebar r)
                {
                    try
                    {
                        if (doc.GetElement(r.GetShapeId()) is RebarShape forma)
                        {
                            d.Forma = forma.Name;
                            d.Estribo = forma.RebarStyle == RebarStyle.StirrupTie;
                        }
                    }
                    catch { /* armadura livre, sem forma */ }
                    Anel(r, d);
                }
                return d;
            }
            catch { return null; }
        }

        private static ElementId PrimeiroTipo(Document doc, IEnumerable<ElementId> ids)
        {
            foreach (ElementId i in ids)
                if (doc.GetElement(i) is RebarInSystem ris) return ris.GetTypeId();
            return ElementId.InvalidElementId;
        }

        private static string NomeMaterial(Document doc, Element armadura, Element tipo)
        {
            try
            {
                ElementId mat = armadura.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM)?.AsElementId();
                if (mat == null || mat == ElementId.InvalidElementId) mat = tipo.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM)?.AsElementId();
                return (doc.GetElement(mat ?? ElementId.InvalidElementId) as Material)?.Name;
            }
            catch { return null; }
        }

        /// <summary>Anel fechado (o eixo da primeira barra é um círculo completo): normal e diâmetro, no sistema do Revit.</summary>
        private static void Anel(Rebar rebar, ArmaduraDadosRevit d)
        {
            try
            {
                int pos = 0;
                while (pos < rebar.NumberOfBarPositions && !rebar.DoesBarExistAtPosition(pos)) pos++;
                if (pos >= rebar.NumberOfBarPositions) return;
                IList<Curve> curvas = rebar.GetCenterlineCurves(false, true, false, MultiplanarOption.IncludeAllMultiplanarCurves, pos);
                if (curvas.Count == 0 || curvas.Any(c => !(c is Arc))) return;
                var arcos = curvas.Cast<Arc>().ToList();
                double raio = arcos[0].Radius;
                double total = arcos.Sum(a => a.Length);
                if (arcos.Any(a => Math.Abs(a.Radius - raio) > 1e-6 || !a.Center.IsAlmostEqualTo(arcos[0].Center))) return;
                if (Math.Abs(total - 2 * Math.PI * raio) > 0.01 * total) return;
                Arc a0 = arcos[0];
                XYZ ini = a0.IsBound ? a0.GetEndPoint(0) : a0.Center + a0.XDirection * raio;
                XYZ meio = a0.Center - (ini - a0.Center);
                XYZ n = a0.Normal, dir = meio - ini;
                d.NormalAnel = new[] { n.X, n.Y, n.Z };
                d.DiametroAnel = new[] { dir.X, dir.Y, dir.Z };
            }
            catch { /* sem a geometria: o IFC considera o anel horizontal */ }
        }

        private static List<BarraRevit> DoSistema(Document doc, IEnumerable<ElementId> ids)
        {
            var lista = new List<BarraRevit>();
            foreach (ElementId i in ids)
                if (doc.GetElement(i) is RebarInSystem ris)
                    lista.AddRange(Uniformes(ris.NumberOfBarPositions, ris.DoesBarExistAtPosition, ris.TotalLength, ris.Quantity,
                        KgPorMetro(doc, ris, ris.GetTypeId())));
            return lista;
        }

        private static List<BarraRevit> DoRebar(Document doc, Rebar rebar)
        {
            double kgPorM = KgPorMetro(doc, rebar, rebar.GetTypeId());
            if (!Variavel(rebar))
                return Uniformes(rebar.NumberOfBarPositions, rebar.DoesBarExistAtPosition, rebar.TotalLength, rebar.Quantity, kgPorM);

            var lista = new List<BarraRevit>();
            var parametro = new ElementId(BuiltInParameter.REBAR_ELEM_LENGTH);
            for (int i = 0; i < rebar.NumberOfBarPositions; i++)
            {
                if (!rebar.DoesBarExistAtPosition(i)) continue;
                double pes = 0;
                try
                {
                    if (PodeVariarPorBarra(rebar) && rebar.GetParameterValueAtIndex(parametro, i) is DoubleParameterValue valor)
                        pes = valor.Value;
                }
                catch { /* sem valor por posição: usa a geometria */ }
                if (pes <= 0)
                {
                    try
                    {
                        pes = rebar.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeAllMultiplanarCurves, i)
                            .Sum(c => c.Length);
                    }
                    catch { pes = 0; }
                }
                double m = pes * Pe;
                lista.Add(new BarraRevit { Comprimento = m, Peso = m * kgPorM });
            }
            // Algum comprimento que não deu para ler: a média do conjunto (melhor que nada no IFC).
            if (lista.Any(b => b.Comprimento <= 0) && rebar.Quantity > 0)
            {
                double medio = rebar.TotalLength / rebar.Quantity * Pe;
                foreach (BarraRevit b in lista.Where(b => b.Comprimento <= 0)) { b.Comprimento = medio; b.Peso = medio * kgPorM; }
            }
            return lista;
        }

        /// <summary>Conjunto com barras de comprimentos diferentes (o exportador cria um IfcReinforcingBar por barra).</summary>
        private static bool Variavel(Rebar rebar) =>
#if REVIT_ANTES_2025
            rebar.DistributionType == DistributionType.VaryingLength;
#else
            rebar.HasVariableLengthBars;
#endif

        private static bool PodeVariarPorBarra(Rebar rebar) =>
#if REVIT_ANTES_2025
            true;   // sem CanHaveVaryingLengthBars antes do 2025: tenta e, se falhar, usa a geometria
#else
            rebar.CanHaveVaryingLengthBars;
#endif

        private static List<BarraRevit> Uniformes(int posicoes, Func<int, bool> existe, double totalPes, int quantidade, double kgPorM)
        {
            var lista = new List<BarraRevit>();
            if (quantidade <= 0) return lista;
            double m = totalPes / quantidade * Pe;
            for (int i = 0; i < posicoes; i++)
                if (existe(i)) lista.Add(new BarraRevit { Comprimento = m, Peso = m * kgPorM });
            return lista;
        }

        /// <summary>kg por metro de barra: área do diâmetro nominal × densidade do material da armadura (0 = desconhecido).</summary>
        private static double KgPorMetro(Document doc, Element armadura, ElementId tipoId)
        {
            if (!(doc.GetElement(tipoId) is RebarBarType tipo)) return 0;
            double d = tipo.BarNominalDiameter * Pe;
            if (d <= 0) return 0;
            return Math.PI * d * d / 4 * Densidade(doc, armadura, tipo);
        }

        private static double Densidade(Document doc, Element armadura, Element tipo)
        {
            try
            {
                ElementId mat = armadura.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM)?.AsElementId();
                if (mat == null || mat == ElementId.InvalidElementId) mat = tipo.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM)?.AsElementId();
                if (doc.GetElement(mat ?? ElementId.InvalidElementId) is Material material &&
                    doc.GetElement(material.StructuralAssetId) is PropertySetElement pse)
                {
                    double ro = UnitUtils.ConvertFromInternalUnits(pse.GetStructuralAsset().Density, UnitTypeId.KilogramsPerCubicMeter);
                    if (ro > 100) return ro;
                }
            }
            catch { /* sem densidade no material */ }
            return DensidadeAco;
        }
    }
}
