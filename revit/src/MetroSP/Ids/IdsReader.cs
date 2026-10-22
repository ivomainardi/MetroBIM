using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace MetroSP.Ids
{
    /// <summary>
    /// Lê arquivos IDS 1.0 (buildingSMART). Ignora namespaces para tolerar versões/rascunhos.
    /// </summary>
    public static class IdsReader
    {
        public static IdsDocument Load(string path)
        {
            using (var stream = File.OpenRead(path))
                return Load(XDocument.Load(stream));
        }

        public static IdsDocument Load(XDocument xml)
        {
            XElement root = xml.Root ?? throw new InvalidDataException("Arquivo IDS vazio.");
            if (root.Name.LocalName != "ids")
                throw new InvalidDataException("O arquivo não é um IDS (elemento raiz <ids> não encontrado).");

            var doc = new IdsDocument();
            XElement info = Child(root, "info");
            doc.Title = Text(info, "title");
            doc.Version = Text(info, "version");
            doc.Author = Text(info, "author");

            foreach (XElement spec in Descendants(Child(root, "specifications"), "specification"))
            {
                var s = new IdsSpecification
                {
                    Name = (string)spec.Attribute("name"),
                    Identifier = (string)spec.Attribute("identifier"),
                    Description = (string)spec.Attribute("description"),
                    IfcVersion = (string)spec.Attribute("ifcVersion"),
                };
                foreach (XElement f in Child(spec, "applicability")?.Elements() ?? Enumerable.Empty<XElement>())
                {
                    IdsFacet facet = ReadFacet(f);
                    if (facet != null) s.Applicability.Add(facet);
                }
                foreach (XElement f in Child(spec, "requirements")?.Elements() ?? Enumerable.Empty<XElement>())
                {
                    IdsFacet facet = ReadFacet(f);
                    if (facet != null) s.Requirements.Add(facet);
                }
                doc.Specifications.Add(s);
            }
            return doc;
        }

        private static IdsFacet ReadFacet(XElement e)
        {
            IdsFacet facet;
            switch (e.Name.LocalName)
            {
                case "entity":
                    facet = ReadEntity(e);
                    break;
                case "property":
                    facet = new IdsPropertyFacet
                    {
                        PropertySet = ReadValue(Child(e, "propertySet")),
                        BaseName = ReadValue(Child(e, "baseName")),
                        Value = ReadValue(Child(e, "value")),
                        DataType = ((string)e.Attribute("dataType"))?.ToUpperInvariant(),
                    };
                    break;
                case "attribute":
                    facet = new IdsAttributeFacet { Name = ReadValue(Child(e, "name")), Value = ReadValue(Child(e, "value")) };
                    break;
                case "classification":
                    facet = new IdsClassificationFacet { System = ReadValue(Child(e, "system")), Value = ReadValue(Child(e, "value")) };
                    break;
                case "material":
                    facet = new IdsMaterialFacet { Value = ReadValue(Child(e, "value")) };
                    break;
                case "partOf":
                    facet = new IdsPartOfFacet { Relation = (string)e.Attribute("relation"), Entity = ReadEntity(Child(e, "entity")) };
                    break;
                default:
                    return null;
            }

            facet.Cardinality = ParseCardinality((string)e.Attribute("cardinality"));
            facet.Instructions = (string)e.Attribute("instructions");
            return facet;
        }

        private static IdsEntityFacet ReadEntity(XElement e) => e == null ? null : new IdsEntityFacet
        {
            Name = ReadValue(Child(e, "name")),
            PredefinedType = ReadValue(Child(e, "predefinedType")),
        };

        private static IdsCardinality ParseCardinality(string value)
        {
            switch (value)
            {
                case "optional": return IdsCardinality.Optional;
                case "prohibited": return IdsCardinality.Prohibited;
                default: return IdsCardinality.Required;
            }
        }

        private static IdsValue ReadValue(XElement e)
        {
            if (e == null) return null;
            var v = new IdsValue();

            XElement simple = Child(e, "simpleValue");
            if (simple != null)
            {
                v.Simple = simple.Value.Trim();
                return v;
            }

            XElement restriction = Child(e, "restriction");
            if (restriction == null) return null;

            foreach (XElement r in restriction.Elements())
            {
                string val = (string)r.Attribute("value");
                switch (r.Name.LocalName)
                {
                    case "enumeration": v.Enumeration.Add(val); break;
                    case "pattern": v.Pattern = val; break;
                    case "minInclusive": v.MinInclusive = val; break;
                    case "maxInclusive": v.MaxInclusive = val; break;
                    case "minExclusive": v.MinExclusive = val; break;
                    case "maxExclusive": v.MaxExclusive = val; break;
                }
            }
            return v;
        }

        private static XElement Child(XElement e, string localName) =>
            e?.Elements().FirstOrDefault(x => x.Name.LocalName == localName);

        private static System.Collections.Generic.IEnumerable<XElement> Descendants(XElement e, string localName) =>
            e?.Elements().Where(x => x.Name.LocalName == localName) ?? Enumerable.Empty<XElement>();

        private static string Text(XElement e, string localName) => Child(e, localName)?.Value.Trim();
    }
}
