using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace MetroSP.Ids
{
    public enum CheckStatus
    {
        OK,
        Aviso,
        SemMapeamento,
        ParametroAusente,
        TipoIncompativel,
        ValoresVazios,
        ValorInvalido,
        SemElementos,
        NaoVerificado,
        ForaDoIfc,
    }

    public class CheckResult
    {
        public string Especificacao { get; set; }
        public string Requisito { get; set; }
        public string Entidade { get; set; }
        public CheckStatus Status { get; set; }
        public string Detalhe { get; set; }
        public List<ElementId> Elementos { get; set; } = new List<ElementId>();
        public int QtdElementos => Elementos.Count;
    }

    /// <summary>
    /// Verifica, ANTES da exportação, se o modelo e o mapeamento de exportação IFC
    /// conseguem atender aos requisitos de um IDS.
    /// </summary>
    public class MappingAnalyzer
    {
        private readonly Document _doc;
        private readonly ExportMapping _mapping;
        private readonly bool _checkValues;
        private readonly Dictionary<ElementId, string> _entityCache = new Dictionary<ElementId, string>();
        private List<Element> _elements;

        // Atributos IFC que o exportador lê de parâmetros com nome fixo.
        private static readonly Dictionary<string, string> AttributeParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = "IfcName",
            ["Description"] = "IfcDescription",
            ["ObjectType"] = "IfcObjectType",
            ["Tag"] = "IfcTag",
            ["LongName"] = "IfcLongName",
        };

        // Atributos que o exportador sempre preenche com um valor padrão.
        private static readonly HashSet<string> AttributesWithDefault = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "GlobalId", "Name", "ObjectType", "Tag",
        };

        public MappingAnalyzer(Document doc, ExportMapping mapping, bool checkValues)
        {
            _doc = doc;
            _mapping = mapping;
            _checkValues = checkValues;
        }

        /// <summary>Elementos de modelo deixados de fora porque não vão para o IFC, por motivo.</summary>
        public Dictionary<string, List<ElementId>> Fora { get; } = new Dictionary<string, List<ElementId>>();

        /// <summary>
        /// elementos: os que entram na verificação (null = os elementos do modelo que vão para o IFC).
        /// fase: fase da exportação (null = última fase do projeto).
        /// </summary>
        public List<CheckResult> Run(IdsDocument ids, List<Element> elementos = null, ElementId fase = null)
        {
            if (elementos == null)
            {
                fase = fase ?? FiltroIfc.Fase(_doc);
                _elements = new List<Element>();
                foreach (Element e in new FilteredElementCollector(_doc).WhereElementIsNotElementType().WhereElementIsViewIndependent())
                {
                    if (e.Category == null || e.Category.CategoryType != CategoryType.Model) continue;
                    string motivo = FiltroIfc.MotivoFora(_doc, e, fase);
                    if (motivo == null) { _elements.Add(e); continue; }
                    if (motivo == "Não é elemento de modelo") continue;
                    if (!Fora.TryGetValue(motivo, out List<ElementId> l)) Fora[motivo] = l = new List<ElementId>();
                    l.Add(e.Id);
                }
            }
            else _elements = elementos;

            var results = new List<CheckResult>();
            foreach (IdsSpecification spec in ids.Specifications)
                results.AddRange(CheckSpecification(spec));
            foreach (var k in Fora.OrderByDescending(k => k.Value.Count))
                results.Add(new CheckResult
                {
                    Especificacao = "(fora do IFC)",
                    Requisito = k.Key,
                    Status = CheckStatus.ForaDoIfc,
                    Detalhe = "Não vão para o IFC e por isso não entram na verificação. Duplo clique seleciona.",
                    Elementos = k.Value,
                });
            return results;
        }

        private IEnumerable<CheckResult> CheckSpecification(IdsSpecification spec)
        {
            List<IdsEntityFacet> entities = spec.ApplicableEntities.ToList();
            if (entities.Count == 0)
            {
                yield return new CheckResult
                {
                    Especificacao = spec.Name,
                    Requisito = "(aplicabilidade)",
                    Status = CheckStatus.NaoVerificado,
                    Detalhe = "A aplicabilidade não usa entidade IFC; ainda não suportado na verificação de mapeamento.",
                };
                yield break;
            }

            string filtroExtra = spec.Applicability.Any(f => !(f is IdsEntityFacet))
                ? " A aplicabilidade tem outros filtros (propriedade/classificação...); foram considerados todos os elementos da entidade."
                : "";

            // Elementos agrupados pela entidade IFC com que vão exportar.
            var porEntidade = _elements
                .Select(e => new { Element = e, Entity = ResolveEntity(e) })
                .Where(x => x.Entity != null && entities.Any(f => Matches(f.Name, x.Entity)))
                .GroupBy(x => x.Entity, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (porEntidade.Count == 0)
            {
                yield return new CheckResult
                {
                    Especificacao = spec.Name,
                    Requisito = "(aplicabilidade)",
                    Entidade = string.Join(", ", entities),
                    Status = CheckStatus.SemElementos,
                    Detalhe = "Nenhum elemento do modelo exporta como esta entidade. Verifique a tabela de classes IFC ou o parâmetro \"Exportar para IFC como\".",
                };
                yield break;
            }

            foreach (var grupo in porEntidade)
            {
                List<Element> elems = grupo.Select(x => x.Element).ToList();
                foreach (IdsFacet req in spec.Requirements)
                {
                    CheckResult r = CheckRequirement(req, grupo.Key, elems);
                    r.Especificacao = spec.Name;
                    r.Entidade = grupo.Key;
                    if (filtroExtra.Length > 0) r.Detalhe += filtroExtra;
                    yield return r;
                }
            }
        }

        private CheckResult CheckRequirement(IdsFacet req, string entity, List<Element> elems)
        {
            switch (req)
            {
                case IdsPropertyFacet p: return CheckProperty(p, entity, elems);
                case IdsAttributeFacet a: return CheckAttribute(a, elems);
                case IdsClassificationFacet c: return CheckClassification(c, elems);
                case IdsMaterialFacet m: return CheckMaterial(m, elems);
                default:
                    return new CheckResult
                    {
                        Requisito = req.ToString(),
                        Status = CheckStatus.NaoVerificado,
                        Detalhe = "Tipo de requisito ainda não verificado na análise de mapeamento.",
                    };
            }
        }

        // ---------- Propriedades ----------

        private CheckResult CheckProperty(IdsPropertyFacet p, string entity, List<Element> elems)
        {
            var r = new CheckResult { Requisito = $"{p} ({Card(p)})" + (p.DataType != null ? $" [{p.DataType}]" : "") };

            string pset = p.PropertySet?.Simple;
            string prop = p.BaseName?.Simple;
            if (pset == null || prop == null)
            {
                r.Status = CheckStatus.NaoVerificado;
                r.Detalhe = "Pset ou propriedade definidos por padrão/enumeração; verificação de mapeamento exige nome fixo.";
                return r;
            }

            List<PropertyMapping> maps = _mapping.Find(entity, pset, prop).ToList();
            string paramName;
            PropertyMapping map = maps.FirstOrDefault();

            if (map == null)
            {
                if (p.Cardinality == IdsCardinality.Prohibited)
                {
                    r.Status = CheckStatus.OK;
                    r.Detalhe = "Proibido e não mapeado.";
                    return r;
                }
                if (pset.StartsWith("Pset_", StringComparison.OrdinalIgnoreCase) || pset.StartsWith("Qto_", StringComparison.OrdinalIgnoreCase))
                {
                    // Psets padrão: o exportador procura um parâmetro com o nome da propriedade (ou calcula as quantidades).
                    paramName = prop;
                    r.Detalhe = $"Pset padrão buildingSMART sem mapeamento explícito. Considerado o parâmetro \"{prop}\" " +
                                "(o exportador também pode calcular quantidades ou usar parâmetros internos; confirme no IFC exportado).";
                }
                else
                {
                    r.Status = p.Cardinality == IdsCardinality.Required ? CheckStatus.SemMapeamento : CheckStatus.Aviso;
                    r.Detalhe = $"Nenhum mapeamento gera {pset}.{prop} para {entity}. Adicione ao arquivo de property sets definidos pelo usuário.";
                    return r;
                }
            }
            else
            {
                paramName = map.RevitParameter;
                r.Detalhe = $"Mapeado para o parâmetro \"{paramName}\" ({map.FileType}, {(map.OnType ? "tipo" : "instância")}) em {map.Source}.";

                if (p.Cardinality == IdsCardinality.Prohibited)
                {
                    r.Status = CheckStatus.Aviso;
                    r.Detalhe = $"O padrão Metrô proíbe esta propriedade, mas ela está mapeada para \"{paramName}\".";
                    return r;
                }
                if (p.DataType != null && map.IfcType != null && !string.Equals(p.DataType, map.IfcType, StringComparison.OrdinalIgnoreCase))
                {
                    r.Status = CheckStatus.TipoIncompativel;
                    r.Detalhe = $"O padrão Metrô pede {p.DataType}, mas o mapeamento exporta {map.IfcType} (tipo \"{map.FileType}\").";
                    return r;
                }
            }

            return CheckParameterOnElements(r, paramName, map, p.Value, p.Cardinality, elems);
        }

        private CheckResult CheckParameterOnElements(CheckResult r, string paramName, PropertyMapping map, IdsValue required,
            IdsCardinality card, List<Element> elems)
        {
            var ausentes = new List<ElementId>();
            var vazios = new List<ElementId>();
            var invalidos = new List<ElementId>();
            string tipoRevit = null;

            foreach (Element e in elems)
            {
                Parameter prm = FindParameter(e, paramName, map?.OnType);
                if (prm == null) { ausentes.Add(e.Id); continue; }

                tipoRevit = tipoRevit ?? RevitTypeName(prm);
                if (map != null && !TypesCompatible(map.FileType, prm))
                {
                    r.Status = CheckStatus.TipoIncompativel;
                    r.Detalhe += $" O parâmetro \"{paramName}\" no Revit é {RevitTypeName(prm)}, incompatível com \"{map.FileType}\".";
                    r.Elementos = elems.Select(x => x.Id).ToList();
                    return r;
                }

                if (!_checkValues) continue;

                string valor = ValueAsString(prm);
                if (string.IsNullOrEmpty(valor)) vazios.Add(e.Id);
                else if (required != null && !ValueMatches(required, valor)) invalidos.Add(e.Id);
            }

            if (ausentes.Count > 0)
            {
                r.Status = card == IdsCardinality.Required ? CheckStatus.ParametroAusente : CheckStatus.Aviso;
                r.Detalhe += $" O parâmetro \"{paramName}\" não existe em {ausentes.Count} de {elems.Count} elementos " +
                             "(verifique o vínculo do parâmetro às categorias).";
                r.Elementos = ausentes;
                return r;
            }
            if (vazios.Count > 0 && card == IdsCardinality.Required)
            {
                r.Status = CheckStatus.ValoresVazios;
                r.Detalhe += $" Mapeamento OK; {vazios.Count} de {elems.Count} elementos estão sem valor.";
                r.Elementos = vazios;
                return r;
            }
            if (invalidos.Count > 0)
            {
                r.Status = CheckStatus.ValorInvalido;
                r.Detalhe += $" {invalidos.Count} elementos têm valor fora do exigido ({required}).";
                r.Elementos = invalidos;
                return r;
            }

            r.Status = CheckStatus.OK;
            r.Detalhe += $" Presente em {elems.Count} elementos.";
            return r;
        }

        // ---------- Atributos, classificação, material ----------

        private CheckResult CheckAttribute(IdsAttributeFacet a, List<Element> elems)
        {
            var r = new CheckResult { Requisito = $"{a} ({Card(a)})" };
            string name = a.Name?.Simple;
            if (name == null)
            {
                r.Status = CheckStatus.NaoVerificado;
                r.Detalhe = "Nome do atributo definido por padrão/enumeração.";
                return r;
            }

            if (!AttributeParams.TryGetValue(name, out string paramName))
            {
                r.Status = AttributesWithDefault.Contains(name) ? CheckStatus.OK : CheckStatus.NaoVerificado;
                r.Detalhe = AttributesWithDefault.Contains(name)
                    ? "Preenchido automaticamente pelo exportador."
                    : $"O atributo {name} não tem parâmetro de sobrescrita conhecido; confirme no IFC exportado.";
                return r;
            }

            bool temPadrao = AttributesWithDefault.Contains(name);
            bool algumTemParam = elems.Any(e => FindParameter(e, paramName, null) != null);
            if (!algumTemParam)
            {
                if (temPadrao && a.Value == null)
                {
                    r.Status = CheckStatus.OK;
                    r.Detalhe = $"Preenchido pelo exportador com o valor padrão (pode ser sobrescrito pelo parâmetro \"{paramName}\").";
                }
                else
                {
                    r.Status = temPadrao ? CheckStatus.Aviso : CheckStatus.ParametroAusente;
                    r.Detalhe = $"Crie o parâmetro \"{paramName}\" para controlar o atributo {name}" +
                                (a.Value != null ? $" (o padrão Metrô exige: {a.Value})." : ".");
                    r.Elementos = elems.Select(e => e.Id).ToList();
                }
                return r;
            }

            r.Detalhe = $"Atributo {name} vem do parâmetro \"{paramName}\".";
            // Com valor padrão, parâmetro vazio não é erro: o exportador usa o padrão.
            IdsCardinality card = temPadrao ? IdsCardinality.Optional : a.Cardinality;
            return CheckParameterOnElements(r, paramName, null, a.Value, card, elems);
        }

        private CheckResult CheckClassification(IdsClassificationFacet c, List<Element> elems)
        {
            var r = new CheckResult { Requisito = $"{c} ({Card(c)})" };
            string sistema = c.System?.Simple;
            var sem = new List<ElementId>();

            foreach (Element e in elems)
            {
                IEnumerable<string> codigos = ClassificationValues(e);
                bool ok = sistema == null
                    ? codigos.Any()
                    : codigos.Any(v => v.IndexOf("[" + sistema + "]", StringComparison.OrdinalIgnoreCase) >= 0);
                if (!ok) sem.Add(e.Id);
            }

            r.Detalhe = "Fonte: parâmetros \"ClassificationCode\" no formato [Sistema]Código:Descrição.";
            if (sem.Count == 0) { r.Status = CheckStatus.OK; return r; }
            r.Status = c.Cardinality == IdsCardinality.Required ? CheckStatus.ValoresVazios : CheckStatus.Aviso;
            r.Detalhe += $" {sem.Count} de {elems.Count} elementos sem classificação" + (sistema != null ? $" do sistema \"{sistema}\"." : ".");
            r.Elementos = sem;
            return r;
        }

        private CheckResult CheckMaterial(IdsMaterialFacet m, List<Element> elems)
        {
            var r = new CheckResult { Requisito = $"{m} ({Card(m)})" };
            var sem = elems.Where(e => !e.GetMaterialIds(false).Any() && !e.GetMaterialIds(true).Any()).Select(e => e.Id).ToList();
            if (sem.Count == 0)
            {
                r.Status = CheckStatus.OK;
                r.Detalhe = "Todos os elementos têm material." + (m.Value != null ? $" Valor exigido ({m.Value}) não verificado." : "");
                return r;
            }
            r.Status = m.Cardinality == IdsCardinality.Required ? CheckStatus.ValoresVazios : CheckStatus.Aviso;
            r.Detalhe = $"{sem.Count} de {elems.Count} elementos sem material.";
            r.Elementos = sem;
            return r;
        }

        // ---------- Entidade IFC de cada elemento ----------

        /// <summary>Entidade IFC com que o elemento será exportado, ou null se não exporta/indeterminado.</summary>
        public string ResolveEntity(Element e)
        {
            if (_entityCache.TryGetValue(e.Id, out string cached)) return cached;

            Element type = _doc.GetElement(e.GetTypeId());
            string valor =
                BuiltInString(e, "IFC_EXPORT_ELEMENT_AS") ??
                StringParam(e, "IfcExportAs") ??
                BuiltInString(type, "IFC_EXPORT_ELEMENT_TYPE_AS") ??
                StringParam(type, "IfcExportAs");

            string entity;
            if (!string.IsNullOrWhiteSpace(valor))
            {
                entity = valor.Split('.')[0].Trim();
                if (entity.Equals("DontExport", StringComparison.OrdinalIgnoreCase)) entity = null;
                else if (entity.EndsWith("Type", StringComparison.OrdinalIgnoreCase) && entity.Length > 7)
                    entity = entity.Substring(0, entity.Length - 4);
            }
            else
            {
                entity = _mapping.IfcClassForCategory(e.Category.Name) ?? DefaultIfcClass(e.Category);
                if (entity != null && entity.Equals("Not Exported", StringComparison.OrdinalIgnoreCase)) entity = null;
            }

            _entityCache[e.Id] = entity;
            return entity;
        }

        /// <summary>Classes padrão do exportador para as categorias mais comuns (usadas se não houver tabela de classes).</summary>
        internal static string DefaultIfcClass(Category c)
        {
            switch (c.BuiltInCategory)
            {
                case BuiltInCategory.OST_Walls: return "IfcWall";
                case BuiltInCategory.OST_Floors: return "IfcSlab";
                case BuiltInCategory.OST_Roofs: return "IfcRoof";
                case BuiltInCategory.OST_Ceilings: return "IfcCovering";
                case BuiltInCategory.OST_Columns:
                case BuiltInCategory.OST_StructuralColumns: return "IfcColumn";
                case BuiltInCategory.OST_StructuralFraming: return "IfcBeam";
                case BuiltInCategory.OST_StructuralFoundation: return "IfcFooting";
                case BuiltInCategory.OST_Doors: return "IfcDoor";
                case BuiltInCategory.OST_Windows: return "IfcWindow";
                case BuiltInCategory.OST_Stairs: return "IfcStair";
                case BuiltInCategory.OST_Ramps: return "IfcRamp";
                case BuiltInCategory.OST_StairsRailing: return "IfcRailing";
                case BuiltInCategory.OST_CurtainWallPanels: return "IfcPlate";
                case BuiltInCategory.OST_CurtainWallMullions: return "IfcMember";
                case BuiltInCategory.OST_Rooms: return "IfcSpace";
                case BuiltInCategory.OST_MEPSpaces: return "IfcSpace";
                case BuiltInCategory.OST_Furniture: return "IfcFurniture";
                case BuiltInCategory.OST_PipeCurves: return "IfcPipeSegment";
                case BuiltInCategory.OST_PipeFitting: return "IfcPipeFitting";
                case BuiltInCategory.OST_DuctCurves: return "IfcDuctSegment";
                case BuiltInCategory.OST_DuctFitting: return "IfcDuctFitting";
                case BuiltInCategory.OST_CableTray: return "IfcCableCarrierSegment";
                case BuiltInCategory.OST_Conduit: return "IfcCableCarrierSegment";
                case BuiltInCategory.OST_GenericModel: return "IfcBuildingElementProxy";
                default: return "IfcBuildingElementProxy";
            }
        }

        // ---------- Utilitários ----------

        private static bool Matches(IdsValue v, string actual)
        {
            if (v == null) return true;
            if (v.FixedValues.Any()) return v.FixedValues.Any(x => string.Equals(x, actual, StringComparison.OrdinalIgnoreCase));
            if (v.Pattern != null) return Regex.IsMatch(actual, "^(?:" + v.Pattern + ")$", RegexOptions.IgnoreCase);
            return false;
        }

        internal static bool ValueMatches(IdsValue v, string actual)
        {
            if (v.Simple != null) return v.Simple == actual;
            if (v.Enumeration.Count > 0) return v.Enumeration.Contains(actual);
            if (v.Pattern != null) return Regex.IsMatch(actual, "^(?:" + v.Pattern + ")$");
            return true; // limites numéricos: deixados para a validação do IFC exportado
        }

        /// <summary>Procura o parâmetro na instância e/ou no tipo.</summary>
        private Parameter FindParameter(Element e, string name, bool? onType)
        {
            Element type = _doc.GetElement(e.GetTypeId());
            if (onType == true) return type?.LookupParameter(name) ?? e.LookupParameter(name);
            return e.LookupParameter(name) ?? type?.LookupParameter(name);
        }

        private IEnumerable<string> ClassificationValues(Element e)
        {
            Element type = _doc.GetElement(e.GetTypeId());
            foreach (Element alvo in new[] { e, type })
            {
                if (alvo == null) continue;
                foreach (Parameter p in alvo.Parameters)
                {
                    if (p.StorageType != StorageType.String) continue;
                    if (!p.Definition.Name.StartsWith("ClassificationCode", StringComparison.OrdinalIgnoreCase)) continue;
                    string v = p.AsString();
                    if (!string.IsNullOrWhiteSpace(v)) yield return v;
                }
            }
        }

        internal static string ValueAsString(Parameter p)
        {
            if (!p.HasValue) return null;
            switch (p.StorageType)
            {
                case StorageType.String: return p.AsString();
                case StorageType.Integer:
                    if (p.Definition.GetDataType() == SpecTypeId.Boolean.YesNo) return p.AsInteger() == 1 ? "TRUE" : "FALSE";
                    return p.AsInteger().ToString();
                case StorageType.Double: return p.AsValueString() ?? p.AsDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
                case StorageType.ElementId: return p.AsElementId() == ElementId.InvalidElementId ? null : p.AsValueString();
                default: return null;
            }
        }

        internal static string RevitTypeName(Parameter p)
        {
            ForgeTypeId t = p.Definition.GetDataType();
            if (t == SpecTypeId.String.Text) return "Texto";
            if (t == SpecTypeId.Boolean.YesNo) return "Sim/Não";
            if (t == SpecTypeId.Int.Integer) return "Inteiro";
            if (t == SpecTypeId.Number) return "Número";
            if (t == SpecTypeId.Length) return "Comprimento";
            if (t == SpecTypeId.Area) return "Área";
            if (t == SpecTypeId.Volume) return "Volume";
            try { return LabelUtils.GetLabelForSpec(t); } catch { return p.StorageType.ToString(); }
        }

        internal static bool TypesCompatible(string fileType, Parameter p)
        {
            ForgeTypeId t = p.Definition.GetDataType();
            switch ((fileType ?? "").ToLowerInvariant())
            {
                case "boolean":
                case "logical": return t == SpecTypeId.Boolean.YesNo;
                case "integer":
                case "count": return p.StorageType == StorageType.Integer;
                case "length":
                case "positivelength": return t == SpecTypeId.Length;
                case "area": return t == SpecTypeId.Area;
                case "volume": return t == SpecTypeId.Volume;
                case "real": return p.StorageType == StorageType.Double || p.StorageType == StorageType.Integer;
                default: return true; // Text/Label/Identifier aceitam qualquer parâmetro (exportado como texto)
            }
        }

        private static string BuiltInString(Element e, string builtInName)
        {
            if (e == null || !Enum.TryParse(builtInName, out BuiltInParameter bip)) return null;
            Parameter p = e.get_Parameter(bip);
            return p != null && p.StorageType == StorageType.String ? NullIfEmpty(p.AsString()) : null;
        }

        private static string StringParam(Element e, string name)
        {
            Parameter p = e?.LookupParameter(name);
            return p != null && p.StorageType == StorageType.String ? NullIfEmpty(p.AsString()) : null;
        }

        private static string NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

        private static string Card(IdsFacet f) =>
            f.Cardinality == IdsCardinality.Required ? "obrigatório" :
            f.Cardinality == IdsCardinality.Optional ? "opcional" : "proibido";
    }
}
