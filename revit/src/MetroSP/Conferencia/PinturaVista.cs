using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MetroSP.Conferencia
{
    /// <summary>
    /// Pinta elementos na vista ativa (sobreposição de gráficos por elemento) para achar as pendências no modelo,
    /// e desfaz depois, devolvendo a sobreposição que cada elemento tinha antes.
    /// Chamar só no contexto da API (ExternalEvent).
    /// </summary>
    public static class PinturaVista
    {
        // (documento, vista, elemento) → sobreposição original, para "Limpar pintura".
        private static readonly Dictionary<(string Doc, long Vista, long Elemento), OverrideGraphicSettings> Originais =
            new Dictionary<(string, long, long), OverrideGraphicSettings>();

        public static readonly Color Vermelho = new Color(214, 69, 65);
        public static readonly Color Amarelo = new Color(232, 180, 0);

        public static bool TemPintura(Document doc) => Originais.Keys.Any(k => k.Doc == Chave(doc));

        /// <summary>Pinta os elementos na vista ativa. Retorna quantos foram pintados (ou -1 se a vista não deixa).</summary>
        public static int Pintar(UIDocument uidoc, ICollection<ElementId> ids, Color cor)
        {
            Document doc = uidoc.Document;
            View vista = uidoc.ActiveView;
            if (vista == null || !vista.AreGraphicsOverridesAllowed()) return -1;

            ElementId solido = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                .FirstOrDefault(f => f.GetFillPattern().IsSolidFill)?.Id ?? ElementId.InvalidElementId;

            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(cor);
            ogs.SetCutLineColor(cor);
            if (solido != ElementId.InvalidElementId)
            {
                ogs.SetSurfaceForegroundPatternId(solido);
                ogs.SetSurfaceForegroundPatternColor(cor);
                ogs.SetCutForegroundPatternId(solido);
                ogs.SetCutForegroundPatternColor(cor);
            }

            int n = 0;
            using (var t = new Transaction(doc, "MetroSP: pintar pendências"))
            {
                t.Start();
                foreach (ElementId id in ids)
                {
                    try
                    {
                        var k = (Chave(doc), vista.Id.Value, id.Value);
                        if (!Originais.ContainsKey(k)) Originais[k] = vista.GetElementOverrides(id);
                        vista.SetElementOverrides(id, ogs);
                        n++;
                    }
                    catch { /* elemento que a vista não aceita sobrepor */ }
                }
                t.Commit();
            }
            return n;
        }

        /// <summary>Devolve a aparência original de tudo o que foi pintado neste documento (em todas as vistas).</summary>
        public static int Limpar(Document doc)
        {
            string d = Chave(doc);
            var chaves = Originais.Keys.Where(k => k.Doc == d).ToList();
            if (chaves.Count == 0) return 0;
            int n = 0;
            using (var t = new Transaction(doc, "MetroSP: limpar pintura"))
            {
                t.Start();
                foreach (var k in chaves)
                {
                    try
                    {
                        if (doc.GetElement(new ElementId(k.Vista)) is View v)
                        {
                            v.SetElementOverrides(new ElementId(k.Elemento), Originais[k]);
                            n++;
                        }
                    }
                    catch { /* elemento ou vista apagados */ }
                    Originais.Remove(k);
                }
                t.Commit();
            }
            return n;
        }

        private static string Chave(Document doc) => string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName;
    }
}
