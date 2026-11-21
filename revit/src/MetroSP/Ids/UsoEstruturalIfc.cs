using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace MetroSP.Ids
{
    /// <summary>
    /// Vigas (Structural Framing) pelo uso estrutural do Revit: o exportador do IFC4 leva toda a categoria para IfcBeam,
    /// inclusive contraventamentos e peças verticais. Só nesta exportação, e só nos elementos sem "Exportar para IFC como"
    /// na instância ou no tipo (o que vem do Revit ou do arquivo de coordenação nunca é trocado):
    /// contraventamento (Brace, Horizontal Bracing, Kicker Bracing) → IfcMember.BRACE; terça → IfcMember.PURLIN;
    /// banzo de treliça → IfcMember.CHORD; diagonal de treliça → IfcMember; uso Pilar, ou eixo vertical (até 5°) → IfcColumn.
    /// Chamar dentro da transação da exportação; <see cref="Restaurar"/> limpa o que foi gravado.
    /// </summary>
    internal sealed class UsoEstruturalIfc
    {
        private readonly List<Element> _alterados = new List<Element>();
        private readonly Dictionary<string, int> _porClasse = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Resumo para o registro, ou null se nada mudou.</summary>
        public string Aviso => _alterados.Count == 0 ? null :
            $"Perfis pelo uso estrutural do Revit: {_alterados.Count} elemento(s) de Structural Framing saíram como " +
            string.Join(", ", _porClasse.OrderByDescending(k => k.Value).Select(k => $"{k.Key} ({k.Value})")) + " em vez de IfcBeam.";

        public static UsoEstruturalIfc Preparar(Document doc, ElementId vistaId)
        {
            var r = new UsoEstruturalIfc();
            bool porVista = vistaId != null && vistaId != ElementId.InvalidElementId && doc.GetElement(vistaId) is View;
            var coletor = porVista ? new FilteredElementCollector(doc, vistaId) : new FilteredElementCollector(doc);
            var tiposComClasse = new Dictionary<ElementId, bool>();
            foreach (FamilyInstance fi in coletor.OfCategory(BuiltInCategory.OST_StructuralFraming)
                         .WhereElementIsNotElementType().OfType<FamilyInstance>())
            {
                try
                {
                    Parameter como = Compat.ExportarComo(fi, false);
                    if (como == null || como.IsReadOnly || Preenchido(como)) continue;
                    ElementId tipoId = fi.GetTypeId();
                    if (!tiposComClasse.TryGetValue(tipoId, out bool tipoTem))
                        tiposComClasse[tipoId] = tipoTem = Preenchido(Compat.ExportarComo(doc.GetElement(tipoId), true));
                    if (tipoTem) continue;

                    string classe = Classe(fi);
                    if (classe == null) continue;
                    como.Set(classe);
                    r._alterados.Add(fi);
                    string ent = classe.Split('.')[0];
                    r._porClasse[ent] = r._porClasse.TryGetValue(ent, out int n) ? n + 1 : 1;
                }
                catch { /* elemento que não aceita o parâmetro: fica como o exportador faz */ }
            }
            return r;
        }

        /// <summary>Classe IFC pelo uso estrutural (ou pela direção do eixo), ou null para deixar IfcBeam.</summary>
        internal static string Classe(FamilyInstance fi)
        {
            StructuralInstanceUsage uso;
            try { uso = fi.StructuralUsage; } catch { uso = StructuralInstanceUsage.Undefined; }
            switch (uso)
            {
                case StructuralInstanceUsage.Brace:
                case StructuralInstanceUsage.HorizontalBracing:
                case StructuralInstanceUsage.KickerBracing:
                    return "IfcMember.BRACE";
                case StructuralInstanceUsage.Purlin: return "IfcMember.PURLIN";
                case StructuralInstanceUsage.TrussChord: return "IfcMember.CHORD";
                case StructuralInstanceUsage.TrussWeb: return "IfcMember";
                case StructuralInstanceUsage.Column: return "IfcColumn";
            }
            if (fi.StructuralType == StructuralType.Brace) return "IfcMember.BRACE";
            return Vertical(fi) ? "IfcColumn" : null;
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

        private static bool Preenchido(Parameter p)
        {
            if (p == null || !p.HasValue) return false;
            string t = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
            return !string.IsNullOrWhiteSpace(t) && !t.Equals("By Type", StringComparison.OrdinalIgnoreCase) &&
                   !t.Equals("Por tipo", StringComparison.OrdinalIgnoreCase) && !t.Equals("Default", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Limpa o "Exportar para IFC como" gravado (para quando a transação é gravada).</summary>
        public void Restaurar()
        {
            foreach (Element e in _alterados)
                try { Compat.ExportarComo(e, false)?.Set(""); } catch { }
            _alterados.Clear();
        }
    }
}
