using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace MetroSP.Ids
{
    /// <summary>
    /// Conferência das vigas (Structural Framing) que saem como IfcBeam, mas que pelo uso estrutural do Revit ou pela
    /// posição parecem outra coisa: contraventamento (Brace, Horizontal Bracing, Kicker Bracing) → IfcMember.BRACE;
    /// terça → IfcMember.PURLIN; banzo de treliça → IfcMember.CHORD; diagonal de treliça → IfcMember; uso Pilar ou eixo
    /// vertical (até 5°) → IfcColumn. A classe é decisão do projetista: o plugin não troca nada, só avisa no registro
    /// para corrigir no Revit ("Exportar para IFC como" / IfcExportAs ou o uso estrutural).
    /// </summary>
    internal sealed class UsoEstruturalIfc
    {
        private const int Exemplos = 10;
        private readonly Dictionary<string, List<long>> _sugeridos = new Dictionary<string, List<long>>(StringComparer.Ordinal);

        /// <summary>Resumo para o registro, ou null se nada chamou a atenção.</summary>
        public string Aviso
        {
            get
            {
                if (_sugeridos.Count == 0) return null;
                int total = _sugeridos.Sum(k => k.Value.Count);
                return $"Perfis, conferência (o plugin não troca a classe, é decisão do projetista): {total} elemento(s) de " +
                       "Structural Framing saem como IfcBeam, mas pelo uso estrutural ou pela posição seriam outra classe: " +
                       string.Join("; ", _sugeridos.OrderByDescending(k => k.Value.Count).Select(k =>
                           $"{k.Key} {k.Value.Count} (ex.: {string.Join(", ", k.Value.Take(Exemplos))}{(k.Value.Count > Exemplos ? "..." : "")})")) +
                       ". Para mudar, ajuste no Revit o \"Exportar para IFC como\" (ou IfcExportAs) ou o uso estrutural.";
            }
        }

        public static UsoEstruturalIfc Conferir(Document doc, ElementId vistaId)
        {
            var r = new UsoEstruturalIfc();
            bool porVista = vistaId != null && vistaId != ElementId.InvalidElementId && doc.GetElement(vistaId) is View;
            var coletor = porVista ? new FilteredElementCollector(doc, vistaId) : new FilteredElementCollector(doc);
            var classeDoTipo = new Dictionary<ElementId, string>();
            foreach (FamilyInstance fi in coletor.OfCategory(BuiltInCategory.OST_StructuralFraming)
                         .WhereElementIsNotElementType().OfType<FamilyInstance>())
            {
                try
                {
                    // Classe que vai sair: a da instância, senão a do tipo, senão IfcBeam (categoria).
                    string classe = Valor(Compat.ExportarComo(fi, false)) ?? Valor(Compartilhado(fi, false));
                    if (classe == null)
                    {
                        ElementId tipoId = fi.GetTypeId();
                        if (!classeDoTipo.TryGetValue(tipoId, out classe))
                        {
                            Element tipo = doc.GetElement(tipoId);
                            classe = Valor(Compat.ExportarComo(tipo, true)) ?? Valor(Compartilhado(tipo, true));
                            classeDoTipo[tipoId] = classe;
                        }
                    }
                    string ent = (classe ?? "IfcBeam").Split('.')[0].Trim();
                    if (!ent.Equals("IfcBeam", StringComparison.OrdinalIgnoreCase) && !ent.Equals("IfcBeamType", StringComparison.OrdinalIgnoreCase)) continue;

                    string sugerida = Sugerida(fi);
                    if (sugerida == null) continue;
                    if (!r._sugeridos.TryGetValue(sugerida, out var l)) r._sugeridos[sugerida] = l = new List<long>();
                    l.Add(Compat.Numero(fi.Id));
                }
                catch { /* elemento sem os dados: fica fora da conferência */ }
            }
            return r;
        }

        /// <summary>Classe pelo uso estrutural (ou pela direção do eixo), ou null quando IfcBeam combina.</summary>
        internal static string Sugerida(FamilyInstance fi)
        {
            StructuralInstanceUsage uso;
            try { uso = fi.StructuralUsage; } catch { uso = StructuralInstanceUsage.Undefined; }
            switch (uso)
            {
                case StructuralInstanceUsage.Brace:
                case StructuralInstanceUsage.HorizontalBracing:
                case StructuralInstanceUsage.KickerBracing:
                    return "IfcMember.BRACE (contraventamento)";
                case StructuralInstanceUsage.Purlin: return "IfcMember.PURLIN (terça)";
                case StructuralInstanceUsage.TrussChord: return "IfcMember.CHORD (banzo de treliça)";
                case StructuralInstanceUsage.TrussWeb: return "IfcMember (diagonal de treliça)";
                case StructuralInstanceUsage.Column: return "IfcColumn (uso pilar)";
            }
            if (fi.StructuralType == StructuralType.Brace) return "IfcMember.BRACE (contraventamento)";
            return Vertical(fi) ? "IfcColumn (peça vertical)" : null;
        }

        /// <summary>Eixo da barra a até 5° da vertical (pela linha de locação).</summary>
        private static bool Vertical(FamilyInstance fi)
        {
            if (!(fi.Location is LocationCurve lc) || lc.Curve == null || !lc.Curve.IsBound) return false;
            XYZ d = lc.Curve.GetEndPoint(1) - lc.Curve.GetEndPoint(0);
            double comp = d.GetLength();
            if (comp < 1e-6) return false;
            return Math.Abs(d.Z) / comp >= Math.Cos(5 * Math.PI / 180);
        }

        /// <summary>Parâmetro compartilhado IfcExportAs (modelos e famílias antigas), que o exportador também lê.</summary>
        private static Parameter Compartilhado(Element e, bool doTipo)
        {
            if (e == null) return null;
            Parameter p = (doTipo ? e.LookupParameter("IfcExportAs[Type]") : null) ?? e.LookupParameter("IfcExportAs");
            return p != null && p.IsShared ? p : null;
        }

        private static string Valor(Parameter p)
        {
            if (p == null || !p.HasValue) return null;
            string t = (p.StorageType == StorageType.String ? p.AsString() : p.AsValueString())?.Trim();
            if (string.IsNullOrEmpty(t) || t.Equals("By Type", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("Por tipo", StringComparison.OrdinalIgnoreCase) || t.Equals("Default", StringComparison.OrdinalIgnoreCase)) return null;
            return t;
        }
    }
}
