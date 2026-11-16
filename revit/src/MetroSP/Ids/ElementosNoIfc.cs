using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using MetroSP.Ifc;

namespace MetroSP.Ids
{
    /// <summary>
    /// Conferência do que saiu no IFC: quantos elementos por classe e quantos ambientes (IfcSpace). Se o IFC sair sem
    /// elementos (só pavimentos e ambientes), diz por quê, olhando a fase e a vista usadas na exportação.
    /// </summary>
    public static class ElementosNoIfc
    {
        public class Contagem
        {
            /// <summary>Elementos contidos nos pavimentos (e as partes deles), por classe IFC.</summary>
            public Dictionary<string, int> PorClasse { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public int Espacos { get; set; }
            /// <summary>Elementos que não são eixos (IfcGrid) nem anotações.</summary>
            public int Fisicos => PorClasse.Where(k => k.Key != "IfcGrid" && k.Key != "IfcAnnotation").Sum(k => k.Value);
            public int Total => PorClasse.Values.Sum();
        }

        /// <summary>Conta os elementos do IFC: os contidos na estrutura espacial e as partes deles (lances de escada etc.).</summary>
        public static Contagem Contar(StepModel m)
        {
            var c = new Contagem { Espacos = m.OfType("IFCSPACE").Count() };
            var vistos = new HashSet<int>();
            var fila = new Queue<int>();
            foreach (StepEntity rel in m.OfType("IFCRELCONTAINEDINSPATIALSTRUCTURE"))
                foreach (int id in StepModel.RefList(rel.Args[4])) fila.Enqueue(id);
            // Partes agregadas a um elemento (escada → lances, parede cortina → painéis), sem a estrutura espacial.
            var partes = new Dictionary<int, List<int>>();
            foreach (StepEntity rel in m.OfType("IFCRELAGGREGATES"))
            {
                int pai = StepModel.Ref(rel.Args[4]);
                if (pai == 0) continue;
                if (!partes.TryGetValue(pai, out List<int> l)) partes[pai] = l = new List<int>();
                l.AddRange(StepModel.RefList(rel.Args[5]));
            }
            while (fila.Count > 0)
            {
                int id = fila.Dequeue();
                if (!vistos.Add(id)) continue;
                string tipo = m.TypeOf(id);
                if (tipo == null || Espacial(tipo)) continue;
                string classe = NomeClasse(tipo);
                c.PorClasse[classe] = c.PorClasse.TryGetValue(classe, out int n) ? n + 1 : 1;
                if (partes.TryGetValue(id, out List<int> filhos)) foreach (int f in filhos) fila.Enqueue(f);
            }
            return c;
        }

        /// <summary>
        /// Linhas para o resumo e o log: o que filtrou a exportação (modelo inteiro ou vista, e a fase), quantos
        /// elementos saíram por classe e, se o IFC saiu sem elementos, um aviso com a causa provável.
        /// </summary>
        public static List<string> Conferir(Document doc, Contagem c, ElementId vista, ElementId fase, out bool problema)
        {
            var linhas = new List<string>();
            problema = false;
            bool porVista = vista != null && vista != ElementId.InvalidElementId;
            string nomeFase = fase != null && fase != ElementId.InvalidElementId ? doc.GetElement(fase)?.Name : null;
            linhas.Add("Saída: " + (porVista ? $"só o visível na vista \"{doc.GetElement(vista)?.Name}\"" : "modelo inteiro") +
                       " · fase \"" + (nomeFase ?? "?") + "\"");

            string classes = string.Join(", ", c.PorClasse.OrderByDescending(k => k.Value).ThenBy(k => k.Key)
                .Take(12).Select(k => $"{k.Key} {Num(k.Value)}"));
            if (c.PorClasse.Count > 12) classes += ", ...";
            linhas.Add($"Elementos no IFC: {Num(c.Total)}" + (classes.Length > 0 ? $" ({classes})" : "") + $" · ambientes (IfcSpace): {Num(c.Espacos)}");

            // Elementos que a fase deixou de fora (criados numa fase posterior à exportada).
            int futuros = 0, modelo = 0;
            if (fase != null && fase != ElementId.InvalidElementId)
            {
                foreach (Element e in new FilteredElementCollector(doc).WhereElementIsNotElementType().WhereElementIsViewIndependent())
                {
                    Category cat = e.Category;
                    if (cat == null || cat.CategoryType != CategoryType.Model || FiltroIfc.NaoExportadas.Contains(cat.BuiltInCategory)) continue;
                    if (e is SpatialElement || e.get_BoundingBox(null) == null) continue;
                    modelo++;
                    try { if (e.GetPhaseStatus(fase) == ElementOnPhaseStatus.Future) futuros++; }
                    catch { /* elemento sem fase */ }
                }
            }
            if (futuros > 0)
                linhas.Add($"{Num(futuros)} de {Num(modelo)} elementos do modelo foram criados numa fase posterior a \"{nomeFase}\" e ficaram fora do IFC.");

            if (c.Fisicos == 0)
            {
                problema = true;
                string motivo =
                    futuros > 0 && futuros * 2 >= modelo
                        ? $"A maior parte dos elementos foi criada depois da fase \"{nomeFase}\". Escolha a última fase em Extras › Fase (ou o padrão do exportador) e exporte de novo."
                    : porVista
                        ? "Confira se a vista mostra os elementos (filtros, ocultar/isolar, caixa de corte) ou exporte o modelo inteiro."
                        : "Confira a fase em Extras › Fase e a tabela de Categorias IFC (categorias marcadas para não exportar).";
                linhas.Add("ATENÇÃO: o IFC saiu sem paredes, pisos, portas ou outros elementos" +
                           (c.Espacos > 0 ? $", só com {Num(c.Espacos)} ambiente(s)" : "") + ". " + motivo);
            }
            return linhas;
        }

        private static readonly HashSet<string> Espaciais = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "IFCPROJECT", "IFCSITE", "IFCBUILDING", "IFCBUILDINGSTOREY", "IFCSPACE", "IFCFACILITY", "IFCFACILITYPART",
            "IFCRAILWAY", "IFCROAD", "IFCBRIDGE", "IFCMARINEFACILITY", "IFCEXTERNALSPATIALELEMENT", "IFCSPATIALZONE",
        };

        private static bool Espacial(string tipo) => Espaciais.Contains(tipo);

        /// <summary>IFCWALLSTANDARDCASE → IfcWallStandardCase (pelo esquema, quando conhecido).</summary>
        private static string NomeClasse(string tipo)
        {
            string nome = EsquemaIfc.Entidade(tipo);
            return nome ?? "Ifc" + (tipo.Length > 3 ? tipo.Substring(3, 1) + tipo.Substring(4).ToLowerInvariant() : tipo);
        }

        private static string Num(int n) => n.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"));
    }
}
