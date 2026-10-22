using System.Collections.Generic;
using System.Linq;

namespace MetroSP.Ids
{
    /// <summary>
    /// Valor de um facet IDS: valor simples ou restrição (enumeração, padrão regex, limites).
    /// </summary>
    public class IdsValue
    {
        public string Simple { get; set; }
        public List<string> Enumeration { get; } = new List<string>();
        public string Pattern { get; set; }
        public string MinInclusive { get; set; }
        public string MaxInclusive { get; set; }
        public string MinExclusive { get; set; }
        public string MaxExclusive { get; set; }

        /// <summary>Valores fixos possíveis (simples ou enumeração). Vazio se for padrão/limite.</summary>
        public IEnumerable<string> FixedValues =>
            Simple != null ? new[] { Simple } : Enumeration;

        public override string ToString()
        {
            if (Simple != null) return Simple;
            var partes = new List<string>();
            if (Enumeration.Count > 0) partes.Add("um de: " + string.Join(", ", Enumeration));
            if (Pattern != null) partes.Add("padrão: " + Pattern);
            if (MinInclusive != null) partes.Add(">= " + MinInclusive);
            if (MinExclusive != null) partes.Add("> " + MinExclusive);
            if (MaxInclusive != null) partes.Add("<= " + MaxInclusive);
            if (MaxExclusive != null) partes.Add("< " + MaxExclusive);
            return string.Join("; ", partes);
        }
    }

    public enum IdsCardinality { Required, Optional, Prohibited }

    public abstract class IdsFacet
    {
        public IdsCardinality Cardinality { get; set; } = IdsCardinality.Required;
        public string Instructions { get; set; }
    }

    public class IdsEntityFacet : IdsFacet
    {
        public IdsValue Name { get; set; }
        public IdsValue PredefinedType { get; set; }
        public override string ToString() => PredefinedType == null ? $"{Name}" : $"{Name}.{PredefinedType}";
    }

    public class IdsPropertyFacet : IdsFacet
    {
        public IdsValue PropertySet { get; set; }
        public IdsValue BaseName { get; set; }
        public IdsValue Value { get; set; }
        public string DataType { get; set; }
        public override string ToString() => $"{PropertySet}.{BaseName}";
    }

    public class IdsAttributeFacet : IdsFacet
    {
        public IdsValue Name { get; set; }
        public IdsValue Value { get; set; }
        public override string ToString() => $"Atributo {Name}";
    }

    public class IdsClassificationFacet : IdsFacet
    {
        public IdsValue System { get; set; }
        public IdsValue Value { get; set; }
        public override string ToString() => $"Classificação {System}";
    }

    public class IdsMaterialFacet : IdsFacet
    {
        public IdsValue Value { get; set; }
        public override string ToString() => "Material";
    }

    public class IdsPartOfFacet : IdsFacet
    {
        public string Relation { get; set; }
        public IdsEntityFacet Entity { get; set; }
        public override string ToString() => $"Parte de {Entity} ({Relation})";
    }

    public class IdsSpecification
    {
        public string Name { get; set; }
        public string Identifier { get; set; }
        public string Description { get; set; }
        public string IfcVersion { get; set; }
        public List<IdsFacet> Applicability { get; } = new List<IdsFacet>();
        public List<IdsFacet> Requirements { get; } = new List<IdsFacet>();

        /// <summary>Entidades IFC às quais a especificação se aplica (pode ser vazio se a aplicabilidade não usa entity).</summary>
        public IEnumerable<IdsEntityFacet> ApplicableEntities => Applicability.OfType<IdsEntityFacet>();
    }

    public class IdsDocument
    {
        public string Title { get; set; }
        public string Version { get; set; }
        public string Author { get; set; }
        public List<IdsSpecification> Specifications { get; } = new List<IdsSpecification>();
    }
}
