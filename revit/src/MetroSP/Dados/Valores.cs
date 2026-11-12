using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using MetroSP.Parametros;
using MetroSP.Ui;

namespace MetroSP.Dados
{
    /// <summary>
    /// Valores de parâmetros como texto, ida e volta: o texto que se mostra (e vai para a planilha) é o mesmo que se
    /// lê de volta. Sim/Não vira "Sim"/"Não"; medidas usam as unidades do projeto, sem o símbolo quando possível.
    /// </summary>
    public static class Valores
    {
        public static string Texto(Document doc, Parameter p)
        {
            if (p == null || !p.HasValue) return "";
            switch (p.StorageType)
            {
                case StorageType.String: return p.AsString() ?? "";
                case StorageType.Integer:
                    if (SimNao(p)) return p.AsInteger() == 1 ? "Sim" : "Não";
                    return p.AsValueString() ?? p.AsInteger().ToString(CultureInfo.CurrentCulture);
                case StorageType.Double:
                    ForgeTypeId spec = Spec(p);
                    if (spec != null && UnitUtils.IsMeasurableSpec(spec))
                    {
                        try { return UnitFormatUtils.Format(doc.GetUnits(), spec, p.AsDouble(), true); } catch { /* sem formato */ }
                    }
                    return p.AsDouble().ToString("0.##########", CultureInfo.CurrentCulture);
                case StorageType.ElementId:
                    return p.AsValueString() ?? "";
                default: return "";
            }
        }

        public static bool Vazio(Parameter p)
        {
            if (p == null || !p.HasValue) return true;
            if (p.StorageType == StorageType.String) return string.IsNullOrEmpty(p.AsString());
            if (p.StorageType == StorageType.ElementId) return p.AsElementId() == ElementId.InvalidElementId;
            return false;
        }

        public static ForgeTypeId Spec(Parameter p)
        {
            try { return p.Definition?.GetDataType(); } catch { return null; }
        }

        public static bool SimNao(Parameter p) => p.StorageType == StorageType.Integer && Spec(p)?.TypeId == SpecTypeId.Boolean.YesNo.TypeId;

        public static bool Editavel(Parameter p) => p != null && !p.IsReadOnly && p.StorageType != StorageType.ElementId && p.StorageType != StorageType.None;

        /// <summary>Converte o texto para o valor do parâmetro (string, int ou double em unidades internas). Vazio limpa textos.</summary>
        public static bool Converter(Document doc, Parameter p, string texto, out object valor, out string motivo)
        {
            valor = null;
            motivo = null;
            texto = texto ?? "";
            string t = texto.Trim();
            switch (p.StorageType)
            {
                case StorageType.String:
                    valor = texto;
                    return true;
                case StorageType.Integer:
                    if (SimNao(p))
                    {
                        string s = t.ToLowerInvariant();
                        if (new[] { "sim", "s", "yes", "y", "1", "true", "verdadeiro", "x" }.Contains(s)) { valor = 1; return true; }
                        if (new[] { "não", "nao", "n", "no", "0", "false", "falso" }.Contains(s)) { valor = 0; return true; }
                        motivo = "não é Sim/Não";
                        return false;
                    }
                    if (int.TryParse(t, NumberStyles.Integer, CultureInfo.CurrentCulture, out int i) || int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out i))
                    {
                        valor = i;
                        return true;
                    }
                    motivo = t.Length == 0 ? "vazio não vale para inteiro" : "não é um número inteiro";
                    return false;
                case StorageType.Double:
                    if (t.Length == 0) { motivo = "vazio não vale para número"; return false; }
                    ForgeTypeId spec = Spec(p);
                    if (spec != null && UnitUtils.IsMeasurableSpec(spec))
                    {
                        if (UnitFormatUtils.TryParse(doc.GetUnits(), spec, t, out double v)) { valor = v; return true; }
                        // "2.5" com ponto num Revit em português.
                        if (ValueCopier.TryDouble(t, out double d))
                        {
                            valor = UnitUtils.ConvertToInternalUnits(d, doc.GetUnits().GetFormatOptions(spec).GetUnitTypeId());
                            return true;
                        }
                        motivo = "não é uma medida";
                        return false;
                    }
                    if (ValueCopier.TryDouble(t, out double n)) { valor = n; return true; }
                    motivo = "não é um número";
                    return false;
                default:
                    motivo = "parâmetro de referência não é editado por texto";
                    return false;
            }
        }

        public static bool Igual(Parameter p, object valor)
        {
            if (valor == null) return false;
            switch (valor)
            {
                case string s: return p.StorageType == StorageType.String && (p.AsString() ?? "") == s;
                case int i: return p.HasValue && p.StorageType == StorageType.Integer && p.AsInteger() == i;
                case double d: return p.HasValue && p.StorageType == StorageType.Double && Math.Abs(p.AsDouble() - d) < 1e-9;
                default: return false;
            }
        }

        public static bool Gravar(Parameter p, object valor)
        {
            if (p == null || p.IsReadOnly) return false;
            try
            {
                switch (valor)
                {
                    case string s: return p.Set(s);
                    case int i: return p.Set(i);
                    case double d: return p.Set(d);
                    default: return false;
                }
            }
            catch { return false; }
        }

        public static string Familia(Document doc, Element e) =>
            (e as FamilyInstance)?.Symbol?.FamilyName ?? (doc.GetElement(e.GetTypeId()) as ElementType)?.FamilyName ?? "";

        public static string Tipo(Document doc, Element e) => (doc.GetElement(e.GetTypeId()) as ElementType)?.Name ?? "";

        public static string Nome(Document doc, Element e)
        {
            string f = Familia(doc, e), t = Tipo(doc, e);
            string n = f.Length > 0 && t.Length > 0 ? f + ": " + t : t.Length > 0 ? t : e.Name;
            return string.IsNullOrEmpty(n) ? e.Name : n;
        }

        /// <summary>Elemento com o parâmetro: a própria instância ou o tipo.</summary>
        public static (Parameter Param, Element Dono) Resolver(Document doc, Element e, ParamRef r) => r.Resolver(doc, e);

        /// <summary>
        /// Preenche "{Nome}" com o valor do parâmetro (da instância ou, se não houver, do tipo). Também aceita
        /// {Categoria}, {Família}, {Tipo} e {Id}. Parâmetros que não existem viram vazio e entram em <paramref name="faltando"/>.
        /// </summary>
        public static string Compor(Document doc, Element e, string modelo, ISet<string> faltando = null)
        {
            if (string.IsNullOrEmpty(modelo)) return "";
            return System.Text.RegularExpressions.Regex.Replace(modelo, @"\{([^{}]+)\}", m =>
            {
                string nome = m.Groups[1].Value.Trim();
                switch (nome.ToLowerInvariant())
                {
                    case "categoria": return e.Category?.Name ?? "";
                    case "família": case "familia": return Familia(doc, e);
                    case "tipo": return Tipo(doc, e);
                    case "id": return e.Id.Value.ToString(CultureInfo.InvariantCulture);
                }
                Parameter p = e.LookupParameter(nome) ?? doc.GetElement(e.GetTypeId())?.LookupParameter(nome);
                if (p == null) { faltando?.Add(nome); return ""; }
                return Texto(doc, p);
            });
        }
    }

    /// <summary>
    /// Barra de escopo comum às ferramentas de dados: seleção, vista ativa ou modelo, e as categorias.
    /// </summary>
    public class EscopoDados
    {
        private readonly Document _doc;
        private readonly ICollection<ElementId> _selecao;
        private List<Element> _todos = new List<Element>();

        public ComboBox Escopo { get; }
        public SeletorCategorias Categorias { get; }
        public FrameworkElement Painel { get; }
        public event Action Mudou;

        public EscopoDados(Autodesk.Revit.UI.UIDocument uidoc)
        {
            _doc = uidoc.Document;
            ICollection<ElementId> sel = uidoc.Selection.GetElementIds();
            _selecao = sel.Count > 0 ? sel : null;

            Escopo = new ComboBox { Padding = new Thickness(4, 3, 4, 3), MinWidth = 200, Margin = new Thickness(0, 0, 10, 0) };
            if (_selecao != null) Escopo.Items.Add($"Seleção atual ({_selecao.Count})");
            Escopo.Items.Add("Vista ativa");
            Escopo.Items.Add("Todo o modelo");
            Escopo.SelectedIndex = 0;
            Categorias = new SeletorCategorias();

            var p = new StackPanel { Orientation = Orientation.Horizontal };
            p.Children.Add(Theme.Rotulo("Escopo"));
            p.Children.Add(Escopo);
            p.Children.Add(Categorias);
            Painel = p;

            Escopo.SelectionChanged += (s, e) => { Carregar(); Mudou?.Invoke(); };
            Categorias.Mudou += () => Mudou?.Invoke();
            Carregar();
        }

        private void Carregar()
        {
            string esc = Escopo.SelectedItem as string ?? "";
            _todos = esc.StartsWith("Seleção") ? ValueCopier.Elementos(_doc, _selecao, null)
                : esc == "Vista ativa" ? ValueCopier.Elementos(_doc, null, _doc.ActiveView.Id)
                : ValueCopier.Elementos(_doc, null, null);
            Categorias.Definir(_todos.GroupBy(e => e.Category.Name).ToDictionary(g => g.Key, g => g.Count()));
        }

        /// <summary>Elementos do escopo nas categorias marcadas.</summary>
        public List<Element> Elementos() => _todos.Where(e => Categorias.Aceita(e.Category.Name)).ToList();

        public string Descricao => (Escopo.SelectedItem as string ?? "") + (Categorias.Todas ? "" : " · " + Categorias.Marcadas.Count() + " categorias");
    }
}
