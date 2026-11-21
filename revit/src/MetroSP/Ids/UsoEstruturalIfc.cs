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
        /// <summary>Por que cada elemento ficou como estava (para o registro).</summary>
        private readonly Dictionary<string, int> _ficaram = new Dictionary<string, int>(StringComparer.Ordinal);
        private int _total;

        private void Ficou(string motivo) => _ficaram[motivo] = _ficaram.TryGetValue(motivo, out int n) ? n + 1 : 1;

        /// <summary>Contagem dos elementos de Structural Framing e do que aconteceu com eles (para o registro).</summary>
        public string Diagnostico => _total == 0 ? null :
            $"Perfis pelo uso estrutural, conferência: {_total} elemento(s) de Structural Framing; {_alterados.Count} com a classe trocada" +
            (_ficaram.Count == 0 ? "." : "; ficaram como estavam: " +
                string.Join(", ", _ficaram.OrderByDescending(k => k.Value).Select(k => $"{k.Value} {k.Key}")) + ".");

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
                r._total++;
                try
                {
                    Parameter como = Compat.ExportarComo(fi, false);
                    if (como == null || como.IsReadOnly) { r.Ficou("sem o parâmetro \"Exportar para IFC como\" editável"); continue; }
                    if (Preenchido(como)) { r.Ficou($"com classe na instância ({Valor(como)})"); continue; }
                    // Parâmetro compartilhado IfcExportAs (modelos e famílias antigas): o exportador também lê, então é dado do Revit.
                    Parameter compartilhado = fi.LookupParameter("IfcExportAs");
                    if (compartilhado != null && compartilhado.IsShared && Preenchido(compartilhado) && compartilhado.Id != como.Id)
                    { r.Ficou($"com IfcExportAs na instância ({Valor(compartilhado)})"); continue; }
                    ElementId tipoId = fi.GetTypeId();
                    if (!tiposComClasse.TryGetValue(tipoId, out bool tipoTem))
                    {
                        Element tipo = doc.GetElement(tipoId);
                        Parameter pt = Compat.ExportarComo(tipo, true);
                        Parameter ptc = tipo?.LookupParameter("IfcExportAs[Type]") ?? tipo?.LookupParameter("IfcExportAs");
                        tiposComClasse[tipoId] = tipoTem = Preenchido(pt) || Preenchido(ptc);
                        if (tipoTem) r._valorTipo[tipoId] = Valor(Preenchido(pt) ? pt : ptc);
                    }
                    if (tipoTem) { r.Ficou($"com classe no tipo ({r._valorTipo[tipoId]})"); continue; }

                    string classe = Classe(fi);
                    if (classe == null) { r.Ficou($"vigas (uso {UsoTexto(fi)})"); continue; }
                    como.Set(classe);
                    r._alterados.Add(fi);
                    string ent = classe.Split('.')[0];
                    r._porClasse[ent] = r._porClasse.TryGetValue(ent, out int n) ? n + 1 : 1;
                }
                catch (Exception ex) { r.Ficou("com erro ao gravar (" + ex.Message + ")"); }
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

        private readonly Dictionary<ElementId, string> _valorTipo = new Dictionary<ElementId, string>();

        private static string Valor(Parameter p) => p == null ? "" : (p.StorageType == StorageType.String ? p.AsString() : p.AsValueString())?.Trim();

        private static string UsoTexto(FamilyInstance fi)
        {
            try { return fi.StructuralUsage.ToString(); } catch { return "?"; }
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
