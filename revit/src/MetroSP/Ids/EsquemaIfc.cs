using System;
using System.Collections.Generic;
using System.Linq;

namespace MetroSP.Ids
{
    /// <summary>
    /// Classes IFC de cada esquema (IFC4 e IFC 4.3), geradas do esquema oficial (EsquemaIfc.Dados.cs). As janelas
    /// mostram estas listas em vez de deixar digitar a classe, para não sair classe errada no IFC.
    /// Esquemas: CategoriasIfc.Ifc4 e CategoriasIfc.Ifc4x3. Comparações sem diferenciar maiúsculas, como o exportador.
    /// </summary>
    public static partial class EsquemaIfc
    {
        private sealed class Esquema
        {
            public List<string> Classes;
            public Dictionary<string, List<string>> Tipos;
            public List<string> ClassesComTipo;
            public Dictionary<string, string> Canonico;
        }

        private static readonly Dictionary<string, Esquema> _cache = new Dictionary<string, Esquema>(StringComparer.OrdinalIgnoreCase);
        private static List<string> _entidades;
        private static Dictionary<string, string> _entidadesCanonico;

        public static string Nome(string esquema) => esquema == CategoriasIfc.Ifc4x3 ? "IFC 4.3" : "IFC4";

        private static Esquema De(string esquema)
        {
            string chave = esquema == CategoriasIfc.Ifc4x3 ? CategoriasIfc.Ifc4x3 : CategoriasIfc.Ifc4;
            lock (_cache)
            {
                if (_cache.TryGetValue(chave, out Esquema e)) return e;
                e = new Esquema
                {
                    Classes = new List<string>(), ClassesComTipo = new List<string>(),
                    Tipos = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
                    Canonico = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                };
                foreach (string item in chave == CategoriasIfc.Ifc4x3 ? Produtos4x3 : Produtos4)
                {
                    string[] p = item.Split('|');
                    var tipos = p.Length > 1 ? p[1].Split(',').ToList() : new List<string>();
                    e.Classes.Add(p[0]);
                    e.Tipos[p[0]] = tipos;
                    e.ClassesComTipo.Add(p[0]);
                    e.Canonico[p[0]] = p[0];
                    foreach (string t in tipos)
                    {
                        e.ClassesComTipo.Add(p[0] + "." + t);
                        e.Canonico[p[0] + "." + t] = p[0] + "." + t;
                    }
                }
                _cache[chave] = e;
                return e;
            }
        }

        /// <summary>Classes de elemento (IfcProduct concretas) do esquema, em ordem alfabética.</summary>
        public static IReadOnlyList<string> Classes(string esquema) => De(esquema).Classes;

        /// <summary>Valores do PredefinedType da classe (sem NOTDEFINED); vazio se a classe não tem ou não existe.</summary>
        public static IReadOnlyList<string> Tipos(string esquema, string classe)
        {
            string b = Base(esquema, classe);
            return b != null && De(esquema).Tipos.TryGetValue(b, out List<string> t) ? t : (IReadOnlyList<string>)Array.Empty<string>();
        }

        /// <summary>
        /// Classe de elemento que o valor da tabela de categorias representa. O exportador do Revit aceita também o nome do
        /// tipo (IfcAlarmType vale como IfcAlarm, e a tabela padrão do Revit vem assim), então "IfcXxxType" vale como IfcXxx.
        /// </summary>
        private static string Base(string esquema, string classe)
        {
            if (string.IsNullOrWhiteSpace(classe)) return null;
            string c = classe.Trim();
            Esquema e = De(esquema);
            if (e.Tipos.ContainsKey(c)) return c;
            if (c.Length > 4 && c.EndsWith("Type", StringComparison.OrdinalIgnoreCase) && e.Tipos.ContainsKey(c.Substring(0, c.Length - 4)))
                return c.Substring(0, c.Length - 4);
            return null;
        }

        /// <summary>
        /// Valores aceitos na tabela de categorias além das classes de elemento: os tipos (IfcDoorType, IfcValveType...) e os
        /// grupos que o exportador do Revit cria para algumas categorias (zonas de HVAC como IfcZone, áreas de tela como IfcGroup).
        /// </summary>
        private static readonly string[] Grupos =
            { "IfcGroup", "IfcZone", "IfcSystem", "IfcDistributionSystem", "IfcDistributionCircuit", "IfcBuildingSystem", "IfcBuiltSystem" };

        private static bool ExisteNoEsquema(string esquema, string nome) =>
            (esquema == CategoriasIfc.Ifc4x3 ? Entidades4x3 : Entidades4).Contains(nome, StringComparer.OrdinalIgnoreCase);

        /// <summary>Cada classe e cada Classe.TIPO, para o "Exportar para IFC como".</summary>
        public static IReadOnlyList<string> ClassesComTipo(string esquema) => De(esquema).ClassesComTipo;

        /// <summary>Vazio vale (não muda nada). Senão, a classe (ou o seu tipo, ou um grupo como IfcZone) tem de existir no esquema.</summary>
        public static bool ClasseValida(string esquema, string classe)
        {
            if (string.IsNullOrWhiteSpace(classe) || Base(esquema, classe) != null) return true;
            string c = classe.Trim();
            if (!ExisteNoEsquema(esquema, c)) return false;
            return c.EndsWith("Type", StringComparison.OrdinalIgnoreCase) || Grupos.Contains(c, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Nomes que não existem no IFC4/4.3 mas aparecem em tabelas antigas do Revit, com a classe certa.</summary>
        private static readonly Dictionary<string, string> Trocas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["IfcAssembly"] = "IfcElementAssembly",
        };

        /// <summary>Classe certa para um nome que não existe no esquema (ex.: IfcAssembly → IfcElementAssembly), ou null.</summary>
        public static string Sugestao(string esquema, string classe)
        {
            if (string.IsNullOrWhiteSpace(classe) || ClasseValida(esquema, classe)) return null;
            return Trocas.TryGetValue(classe.Trim(), out string certa) && ClasseValida(esquema, certa) ? certa : null;
        }

        /// <summary>Vazio vale. Senão, o tipo tem de ser um PredefinedType da classe.</summary>
        public static bool TipoValido(string esquema, string classe, string tipo) =>
            string.IsNullOrWhiteSpace(tipo) || Tipos(esquema, classe).Contains(tipo.Trim(), StringComparer.OrdinalIgnoreCase);

        /// <summary>"IfcWall" ou "IfcWall.SHEAR" do esquema; devolve o nome com a caixa certa, ou null.</summary>
        public static string ClasseComTipo(string esquema, string valor) =>
            valor != null && De(esquema).Canonico.TryGetValue(valor.Trim(), out string c) ? c : null;

        /// <summary>
        /// Entidades para os property sets: todas as derivadas de IfcObjectDefinition (inclui abstratas como IfcElement
        /// e os tipos, como IfcWallType), dos dois esquemas, porque o mesmo .txt vale para IFC4 e IFC 4.3.
        /// </summary>
        public static IReadOnlyList<string> Entidades()
        {
            if (_entidades == null)
            {
                var canon = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string e in Entidades4.Concat(Entidades4x3)) canon[e] = e;
                _entidadesCanonico = canon;
                _entidades = canon.Values.OrderBy(x => x, StringComparer.Ordinal).ToList();
            }
            return _entidades;
        }

        /// <summary>Nome da entidade com a caixa certa, ou null se não existe em nenhum dos esquemas.</summary>
        public static string Entidade(string nome)
        {
            Entidades();
            return nome != null && _entidadesCanonico.TryGetValue(nome.Trim(), out string c) ? c : null;
        }

        /// <summary>Em que esquema a entidade existe: "IFC4 e IFC 4.3", "só IFC4" ou "só IFC 4.3".</summary>
        public static string OndeExiste(string entidade)
        {
            bool em4 = Entidades4.Contains(entidade, StringComparer.OrdinalIgnoreCase);
            bool em43 = Entidades4x3.Contains(entidade, StringComparer.OrdinalIgnoreCase);
            return em4 && em43 ? "IFC4 e IFC 4.3" : em4 ? "só IFC4" : em43 ? "só IFC 4.3" : "fora dos esquemas";
        }
    }
}
