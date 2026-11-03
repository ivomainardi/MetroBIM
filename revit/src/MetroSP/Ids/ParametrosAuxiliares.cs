#if REVIT2027_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace MetroSP.Ids
{
    /// <summary>
    /// Revit 2027+: no property set guardado no modelo cada propriedade aponta para um só parâmetro. Quando o .txt
    /// tira a mesma propriedade de parâmetros diferentes conforme a entidade (Nivel = Reference Level na viga e
    /// Level na laje), a exportação cria um parâmetro compartilhado auxiliar, preenche em cada elemento com o valor
    /// do parâmetro certo para a entidade dele e aponta a propriedade para o auxiliar. Tudo acontece dentro da
    /// transação da exportação: sem "gravar GUID" ela é desfeita; com ela, <see cref="Remover"/> tira os auxiliares.
    /// </summary>
    internal static class ParametrosAuxiliares
    {
        private static readonly List<Guid> Criados = new List<Guid>();
        private static readonly Dictionary<ElementId, string> Entidades = new Dictionary<ElementId, string>();

        /// <summary>Cria, vincula e preenche os auxiliares do plano. Retorna quantos valores foram gravados.</summary>
        public static int Criar(Document doc, PsetsNoModelo.Plano plano)
        {
            Criados.Clear();
            Entidades.Clear();
            if (plano.Auxiliares.Count == 0) return 0;

            // 1. Para cada auxiliar, o valor de cada elemento (a origem certa para a entidade dele).
            var instancias = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.Category.CategoryType == CategoryType.Model).ToList();
            List<Element> tipos = null;
            var escolhas = new List<(PsetsNoModelo.Auxiliar Aux, List<(Element Elemento, Parameter Origem)> Valores)>();
            foreach (PsetsNoModelo.Auxiliar aux in plano.Auxiliares)
            {
                IEnumerable<Element> alvo = instancias;
                if (aux.DeTipo)
                    alvo = tipos ?? (tipos = new FilteredElementCollector(doc).WhereElementIsElementType()
                        .Where(e => e.Category != null && e.Category.CategoryType == CategoryType.Model).ToList());
                var valores = new List<(Element, Parameter)>();
                foreach (Element e in alvo)
                {
                    Parameter origem = Origem(doc, e, aux);
                    if (origem != null) valores.Add((e, origem));
                }
                escolhas.Add((aux, valores));
            }

            // 2. Parâmetros compartilhados num arquivo temporário, vinculados às categorias que têm valor.
            Autodesk.Revit.ApplicationServices.Application app = doc.Application;
            string arquivoOriginal = app.SharedParametersFilename;
            string temporario = Path.Combine(Path.GetTempPath(), "MetroSP-auxiliares-" + Guid.NewGuid().ToString("N") + ".txt");
            var definicoes = new Dictionary<PsetsNoModelo.Auxiliar, (ExternalDefinition Def, ForgeTypeId Spec, StorageType Guarda)>();
            try
            {
                File.WriteAllText(temporario, "");
                app.SharedParametersFilename = temporario;
                DefinitionFile arquivo = app.OpenSharedParameterFile();
                DefinitionGroup grupo = arquivo.Groups.Create("MetroSP");
                foreach (var (aux, valores) in escolhas)
                {
                    if (valores.Count == 0) continue;
                    (ForgeTypeId spec, StorageType guarda) = Especie(aux, valores[0].Origem);
                    var opcoes = new ExternalDefinitionCreationOptions(aux.Parametro, spec)
                    {
                        Description = $"Auxiliar da exportação IFC do MetroSP para {aux.PropertySet}.{aux.Propriedade}",
                    };
                    var def = (ExternalDefinition)grupo.Definitions.Create(opcoes);
                    CategorySet categorias = app.Create.NewCategorySet();
                    foreach (Category c in valores.Select(v => v.Elemento.Category).Where(c => c != null && c.AllowsBoundParameters)
                                                  .GroupBy(c => c.Id).Select(g => g.First()))
                        categorias.Insert(c);
                    if (categorias.IsEmpty) continue;
                    Binding vinculo = aux.DeTipo ? (Binding)app.Create.NewTypeBinding(categorias) : app.Create.NewInstanceBinding(categorias);
                    if (!doc.ParameterBindings.Insert(def, vinculo)) continue;
                    Criados.Add(def.GUID);
                    definicoes[aux] = (def, spec, guarda);
                }
            }
            finally
            {
                try { app.SharedParametersFilename = arquivoOriginal ?? ""; } catch { }
                try { File.Delete(temporario); } catch { }
            }
            doc.Regenerate();

            // 3. Valores.
            int gravados = 0;
            foreach (var (aux, valores) in escolhas)
            {
                if (!definicoes.TryGetValue(aux, out var d)) continue;
                foreach (var (elemento, origem) in valores)
                {
                    Parameter destino = elemento.get_Parameter(d.Def.GUID);
                    if (destino == null || destino.IsReadOnly) continue;
                    try { if (Copiar(doc, origem, destino, d.Guarda)) gravados++; }
                    catch { /* elemento que não aceita o valor (grupo, vínculo): fica sem */ }
                }
            }
            return gravados;
        }

        /// <summary>Tira os auxiliares criados nesta exportação. Retorna false se não havia nenhum.</summary>
        public static bool Remover(Document doc)
        {
            if (Criados.Count == 0) return false;
            var ids = new FilteredElementCollector(doc).OfClass(typeof(SharedParameterElement)).Cast<SharedParameterElement>()
                .Where(p => Criados.Contains(p.GuidValue)).Select(p => p.Id).ToList();
            if (ids.Count > 0) doc.Delete(ids);
            Criados.Clear();
            return true;
        }

        /// <summary>
        /// Parâmetro de origem do elemento: os blocos que valem para a entidade dele, na ordem do .txt, e o primeiro
        /// parâmetro com valor. Sem entidade conhecida (ou fora das entidades do grupo), vale qualquer bloco.
        /// </summary>
        private static Parameter Origem(Document doc, Element e, PsetsNoModelo.Auxiliar aux)
        {
            string entidade = Entidade(doc, e);
            bool conhecida = entidade != null && aux.EntidadesDoGrupo.Any(x => string.Equals(x, entidade, StringComparison.OrdinalIgnoreCase));
            Element tipo = aux.DeTipo ? null : doc.GetElement(e.GetTypeId());
            foreach (var (entidades, origens) in aux.Regras)
            {
                if (conhecida && !entidades.Any(x => ModeloIfc.Genericas.Contains(x) || string.Equals(x, entidade, StringComparison.OrdinalIgnoreCase)))
                    continue;
                foreach (string nome in origens)
                {
                    Parameter p = ModeloIfc.Procurar(e, nome) ?? (tipo != null ? ModeloIfc.Procurar(tipo, nome) : null);
                    if (p != null && ModeloIfc.TemValor(p)) return p;
                }
            }
            return null;
        }

        /// <summary>
        /// Entidade IFC do elemento: a indicada em "Exportar para IFC como" (instância ou tipo) ou a padrão da
        /// categoria. null quando não dá para saber com segurança.
        /// </summary>
        private static string Entidade(Document doc, Element e)
        {
            if (Entidades.TryGetValue(e.Id, out string guardada)) return guardada;
            string r = ModeloIfc.EntidadeDe(doc, e);
            Entidades[e.Id] = r;
            return r;
        }

        /// <summary>
        /// Tipo do auxiliar: o do parâmetro de origem quando é número (mantém a unidade, como o exportador faria
        /// com a origem), texto quando a origem é texto ou referência (nível, material: sai o nome).
        /// </summary>
        private static (ForgeTypeId, StorageType) Especie(PsetsNoModelo.Auxiliar aux, Parameter origem)
        {
            if (origem.StorageType == StorageType.Double || origem.StorageType == StorageType.Integer)
            {
                ForgeTypeId spec = origem.Definition.GetDataType();
                if (spec != null && !spec.Empty() && SpecUtils.IsSpec(spec)) return (spec, origem.StorageType);
                return origem.StorageType == StorageType.Double ? (SpecTypeId.Number, StorageType.Double) : (SpecTypeId.Int.Integer, StorageType.Integer);
            }
            return (SpecTypeId.String.Text, StorageType.String);
        }

        private static bool Copiar(Document doc, Parameter origem, Parameter destino, StorageType guarda)
        {
            switch (guarda)
            {
                case StorageType.String:
                    string t = origem.StorageType == StorageType.String ? origem.AsString()
                        : origem.StorageType == StorageType.ElementId ? (doc.GetElement(origem.AsElementId())?.Name ?? origem.AsValueString())
                        : origem.AsValueString();
                    return !string.IsNullOrEmpty(t) && destino.Set(t);
                case StorageType.Double:
                    if (origem.StorageType == StorageType.Double) return destino.Set(origem.AsDouble());
                    if (origem.StorageType == StorageType.Integer) return destino.Set((double)origem.AsInteger());
                    return false;
                case StorageType.Integer:
                    if (origem.StorageType == StorageType.Integer) return destino.Set(origem.AsInteger());
                    if (origem.StorageType == StorageType.Double) return destino.Set((int)Math.Round(origem.AsDouble()));
                    return false;
                default:
                    return false;
            }
        }
    }
}
#endif
