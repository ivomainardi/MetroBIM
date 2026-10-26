using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;

namespace MetroSP.Parametros
{
    /// <summary>Impactos de renomear um parâmetro compartilhado de projeto (mostrados antes de confirmar).</summary>
    public class ProjectRenamePlan
    {
        public SharedParameterElement Element { get; set; }
        public ElementBinding Binding { get; set; }
        public int ElementosComValor { get; set; }
        public List<string> Tabelas { get; } = new List<string>();
        public List<string> FiltrosDeVista { get; } = new List<string>();
    }

    /// <summary>
    /// Renomeia parâmetros compartilhados mantendo o GUID.
    ///
    /// Família (.rfa): ReplaceParameter compartilhado -> família (nome temporário) -> compartilhado
    /// com uma definição de MESMO GUID e nome novo. Valores, fórmulas e associações são preservados.
    ///
    /// Projeto (.rvt): o Revit não permite renomear parâmetro compartilhado. Numa única transação:
    ///   1. tenta renomear direto (se a versão do Revit permitir, termina aqui);
    ///   2. senão, guarda os valores de todos os elementos e os campos das tabelas,
    ///      apaga o parâmetro, recria com o MESMO GUID e o nome novo, refaz o vínculo
    ///      (categorias, grupo, instância/tipo) e restaura valores e campos das tabelas.
    /// Tags e famílias continuam funcionando porque referenciam o GUID.
    /// Regras de filtros de vista que usam o parâmetro são perdidas (avisado antes).
    /// </summary>
    public static class SharedParameterRenamer
    {
        // ---------- Família ----------

        public static void RenameInFamily(Application app, Document doc, FamilyParameter param, string novoNome)
        {
            FamilyManager fm = doc.FamilyManager;
            Guid guid = param.GUID;

            if (fm.get_Parameter(novoNome) != null)
                throw new InvalidOperationException($"Já existe um parâmetro chamado \"{novoNome}\" nesta família.");

            using (var t = new Transaction(doc, "MetroSP: Renomear parâmetro compartilhado"))
            {
                t.Start();
                DadosParametro dados = ParaParametroDeFamilia(doc, param, out FamilyParameter familia);
                string final = ParaCompartilhado(app, doc, familia, dados, novoNome);
                if (final != novoNome)
                    throw new InvalidOperationException(
                        $"O Revit manteve o nome \"{final}\" para este GUID nesta família (ele ainda é usado por uma família aninhada). " +
                        "Renomeie primeiro na família aninhada. Nada foi alterado.");
                t.Commit();
            }
        }

        /// <summary>O que é preciso para recriar o parâmetro compartilhado depois de passá-lo a parâmetro de família.</summary>
        internal class DadosParametro
        {
            public Guid Guid;
            public ForgeTypeId Grupo;
            public ForgeTypeId TipoDado;
            public bool Instancia;
            public bool Visivel;
        }

        /// <summary>
        /// Passo 1 (dentro de uma transação aberta): troca o compartilhado por um parâmetro de família de nome
        /// temporário, mantendo valores, fórmulas e associações, e apaga o registro do GUID no documento.
        /// Sem apagar esse registro, o Revit reaproveita o nome antigo quando o GUID volta (era a causa de o
        /// nome "não mudar"). Se o GUID ainda é usado por uma família aninhada, o registro fica e o nome também.
        /// </summary>
        internal static DadosParametro ParaParametroDeFamilia(Document doc, FamilyParameter param, out FamilyParameter familia)
        {
            var dados = new DadosParametro
            {
                Guid = param.GUID,
                Grupo = param.Definition.GetGroupTypeId(),
                TipoDado = param.Definition.GetDataType(),
                Instancia = param.IsInstance,
                Visivel = ((InternalDefinition)param.Definition).Visible,
            };
            familia = doc.FamilyManager.ReplaceParameter(param, $"__metrosp_tmp_{Guid.NewGuid():N}", dados.Grupo, dados.Instancia);
            ApagarRegistroDoGuid(doc, dados.Guid);
            return dados;
        }

        /// <summary>
        /// Passo 2 (dentro de uma transação aberta): volta a compartilhado com o MESMO GUID e o nome novo.
        /// Retorna o nome que o Revit realmente deu ao parâmetro.
        /// </summary>
        internal static string ParaCompartilhado(Application app, Document doc, FamilyParameter familia, DadosParametro dados, string novoNome)
        {
            string final = null;
            WithTempDefinition(app, novoNome, dados.TipoDado, dados.Guid, dados.Visivel, novaDefinicao =>
            {
                FamilyParameter resultado = doc.FamilyManager.ReplaceParameter(familia, novaDefinicao, dados.Grupo, dados.Instancia);
                if (resultado.GUID != dados.Guid)
                    throw new InvalidOperationException("O GUID resultante não confere. Operação desfeita.");
                final = resultado.Definition.Name;
            });
            return final;
        }

        /// <summary>
        /// Apaga o SharedParameterElement do GUID, se existir e se apagar não levar junto nenhum outro elemento.
        /// Retorna true se o registro não existe mais. Precisa de transação aberta.
        /// </summary>
        internal static bool ApagarRegistroDoGuid(Document doc, Guid guid)
        {
            SharedParameterElement spe = SharedParameterElement.Lookup(doc, guid);
            if (spe == null) return true;
            using (var st = new SubTransaction(doc))
            {
                st.Start();
                try
                {
                    ElementId id = spe.Id;
                    ICollection<ElementId> apagados = doc.Delete(id);
                    if (apagados.All(x => x == id) && SharedParameterElement.Lookup(doc, guid) == null)
                    {
                        st.Commit();
                        return true;
                    }
                }
                catch { /* em uso: fica */ }
                if (st.HasStarted() && !st.HasEnded()) st.RollBack();
                return false;
            }
        }

        // ---------- Projeto ----------

        /// <summary>Levanta o que será afetado, sem alterar nada.</summary>
        public static ProjectRenamePlan PlanProjectRename(Document doc, SharedParameterElement spe)
        {
            InternalDefinition def = spe.GetDefinition();
            var plan = new ProjectRenamePlan
            {
                Element = spe,
                Binding = doc.ParameterBindings.get_Item(def) as ElementBinding,
            };

            plan.ElementosComValor = ElementsWithParameter(doc, spe.GuidValue, plan.Binding is TypeBinding)
                .Count(e => e.get_Parameter(spe.GuidValue)?.HasValue == true);

            foreach (ViewSchedule vs in new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>())
            {
                ScheduleDefinition sd = vs.Definition;
                if (sd.GetFieldOrder().Any(id => sd.GetField(id).ParameterId == spe.Id))
                    plan.Tabelas.Add(vs.Name);
            }

            foreach (ParameterFilterElement f in new FilteredElementCollector(doc).OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>())
            {
                if (f.GetElementFilterParameters().Contains(spe.Id))
                    plan.FiltrosDeVista.Add(f.Name);
            }
            return plan;
        }

        /// <summary>Executa a renomeação no projeto. Retorna avisos (valores ou campos que não puderam ser restaurados).</summary>
        public static List<string> RenameInProject(Application app, Document doc, ProjectRenamePlan plan, string novoNome)
        {
            SharedParameterElement spe = plan.Element;
            InternalDefinition def = spe.GetDefinition();
            Guid guid = spe.GuidValue;
            var avisos = new List<string>();

            if (plan.Binding == null)
                throw new InvalidOperationException(
                    "Este parâmetro não está vinculado ao projeto (vem só de famílias carregadas). " +
                    "Renomeie nas famílias e recarregue-as no projeto.");

            using (var t = new Transaction(doc, "MetroSP: Renomear parâmetro compartilhado"))
            {
                t.Start();

                if (TryDirectRename(spe, novoNome))
                {
                    t.Commit();
                    return avisos;
                }

                // 1. Guardar tudo o que se perde ao apagar o parâmetro.
                bool porTipo = plan.Binding is TypeBinding;
                var categorias = new CategorySet();
                foreach (Category c in plan.Binding.Categories) categorias.Insert(c);
                ForgeTypeId grupo = def.GetGroupTypeId();
                ForgeTypeId tipoDado = def.GetDataType();
                bool visivel = def.Visible;
                bool variaEntreGrupos = def.VariesAcrossGroups;

                List<SavedValue> valores = ElementsWithParameter(doc, guid, porTipo)
                    .Select(e => SavedValue.From(e, e.get_Parameter(guid)))
                    .Where(v => v != null)
                    .ToList();

                var campos = new List<(ViewSchedule View, int Index, string Titulo)>();
                foreach (ViewSchedule vs in new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>())
                {
                    ScheduleDefinition sd = vs.Definition;
                    IList<ScheduleFieldId> ordem = sd.GetFieldOrder();
                    for (int i = 0; i < ordem.Count; i++)
                    {
                        ScheduleField f = sd.GetField(ordem[i]);
                        if (f.ParameterId == spe.Id) campos.Add((vs, i, f.ColumnHeading));
                    }
                }

                // 2. Apagar e recriar com o mesmo GUID.
                doc.Delete(spe.Id);

                WithTempDefinition(app, novoNome, tipoDado, guid, visivel, novaDefinicao =>
                {
                    Binding binding = porTipo
                        ? (Binding)app.Create.NewTypeBinding(categorias)
                        : app.Create.NewInstanceBinding(categorias);
                    if (!doc.ParameterBindings.Insert(novaDefinicao, binding, grupo))
                        throw new InvalidOperationException("O Revit recusou recriar o vínculo do parâmetro. Nada foi alterado.");
                });

                SharedParameterElement novo = SharedParameterElement.Lookup(doc, guid)
                    ?? throw new InvalidOperationException("O parâmetro recriado não foi encontrado. Nada foi alterado.");
                if (novo.GetDefinition().Name != novoNome)
                    throw new InvalidOperationException(
                        $"O Revit manteve o nome \"{novo.GetDefinition().Name}\" porque o parâmetro também está em famílias carregadas. " +
                        "Marque \"Renomear também em todas as famílias carregadas\". Nada foi alterado.");
                if (variaEntreGrupos)
                {
                    try { novo.GetDefinition().SetAllowVaryBetweenGroups(doc, true); }
                    catch { avisos.Add("Não foi possível reativar \"Valores podem variar por instância de grupo\"."); }
                }

                // 3. Restaurar valores e campos das tabelas.
                int falhas = valores.Count(v => !v.Restore(doc, guid));
                if (falhas > 0) avisos.Add($"{falhas} de {valores.Count} valores não puderam ser restaurados (elementos somente leitura ou em grupos).");

                foreach (var (view, index, titulo) in campos)
                {
                    try
                    {
                        ScheduleDefinition sd = view.Definition;
                        SchedulableField sf = sd.GetSchedulableFields().FirstOrDefault(f => f.ParameterId == novo.Id);
                        if (sf == null) { avisos.Add($"Tabela \"{view.Name}\": campo não disponível para recriar."); continue; }
                        ScheduleField campo = sd.InsertField(sf, Math.Min(index, sd.GetFieldCount()));
                        if (!string.IsNullOrEmpty(titulo) && titulo != novoNome) campo.ColumnHeading = titulo;
                    }
                    catch (Exception ex) { avisos.Add($"Tabela \"{view.Name}\": {ex.Message}"); }
                }

                if (plan.FiltrosDeVista.Count > 0)
                    avisos.Add("Revise as regras destes filtros de vista: " + string.Join(", ", plan.FiltrosDeVista));

                t.Commit();
            }
            return avisos;
        }

        /// <summary>Algumas versões do Revit podem aceitar renomear direto. Testa numa subtransação.</summary>
        private static bool TryDirectRename(SharedParameterElement spe, string novoNome)
        {
            using (var st = new SubTransaction(spe.Document))
            {
                try
                {
                    st.Start();
                    spe.Name = novoNome;
                    if (spe.GetDefinition().Name == novoNome)
                    {
                        st.Commit();
                        return true;
                    }
                }
                catch { /* não suportado: segue o caminho de recriar */ }
                if (st.HasStarted() && !st.HasEnded()) st.RollBack();
                return false;
            }
        }

        internal static IEnumerable<Element> ElementsWithParameter(Document doc, Guid guid, bool porTipo)
        {
            var collector = new FilteredElementCollector(doc);
            IEnumerable<Element> elems = porTipo ? collector.WhereElementIsElementType() : collector.WhereElementIsNotElementType();
            return elems.Where(e => e.get_Parameter(guid) != null).ToList();
        }

        /// <summary>
        /// Cria um .txt temporário com uma única definição (mesmo GUID, nome novo), executa a ação
        /// e restaura o arquivo de parâmetros compartilhados original do usuário.
        /// </summary>
        internal static void WithTempDefinition(Application app, string nome, ForgeTypeId tipoDado, Guid guid, bool visivel,
            Action<ExternalDefinition> acao)
        {
            string arquivoOriginal = app.SharedParametersFilename;
            string caminho = Path.Combine(Path.GetTempPath(), $"MetroSP_{Guid.NewGuid():N}.txt");
            try
            {
                File.WriteAllText(caminho, string.Empty);
                app.SharedParametersFilename = caminho;
                DefinitionFile arquivo = app.OpenSharedParameterFile();
                DefinitionGroup grupo = arquivo.Groups.Create("MetroSP");
                var opcoes = new ExternalDefinitionCreationOptions(nome, tipoDado) { GUID = guid, Visible = visivel };
                acao((ExternalDefinition)grupo.Definitions.Create(opcoes));
            }
            finally
            {
                app.SharedParametersFilename = arquivoOriginal ?? string.Empty;
                try { File.Delete(caminho); } catch { /* ignorar */ }
            }
        }

        /// <summary>Valor de parâmetro guardado antes de apagar o parâmetro.</summary>
        internal class SavedValue
        {
            private ElementId _id;
            private StorageType _tipo;
            private object _valor;

            public static SavedValue From(Element e, Parameter p)
            {
                if (p == null || !p.HasValue || p.IsReadOnly) return null;
                var v = new SavedValue { _id = e.Id, _tipo = p.StorageType };
                switch (p.StorageType)
                {
                    case StorageType.String: v._valor = p.AsString(); break;
                    case StorageType.Integer: v._valor = p.AsInteger(); break;
                    case StorageType.Double: v._valor = p.AsDouble(); break;
                    case StorageType.ElementId: v._valor = p.AsElementId(); break;
                    default: return null;
                }
                return v;
            }

            /// <summary>true se o elemento já tem o valor guardado (nada a restaurar).</summary>
            public bool JaIgual(Document doc, Guid guid)
            {
                Parameter p = doc.GetElement(_id)?.get_Parameter(guid);
                if (p == null || p.StorageType != _tipo) return false;
                switch (_tipo)
                {
                    case StorageType.String: return (p.AsString() ?? "") == ((string)_valor ?? "");
                    case StorageType.Integer: return p.AsInteger() == (int)_valor;
                    case StorageType.Double: return Math.Abs(p.AsDouble() - (double)_valor) < 1e-9;
                    case StorageType.ElementId: return p.AsElementId() == (ElementId)_valor;
                }
                return false;
            }

            public bool Restore(Document doc, Guid guid)
            {
                Parameter p = doc.GetElement(_id)?.get_Parameter(guid);
                if (p == null || p.IsReadOnly) return false;
                try
                {
                    switch (_tipo)
                    {
                        case StorageType.String: return p.Set((string)_valor);
                        case StorageType.Integer: return p.Set((int)_valor);
                        case StorageType.Double: return p.Set((double)_valor);
                        case StorageType.ElementId: return p.Set((ElementId)_valor);
                    }
                }
                catch { /* elemento bloqueado */ }
                return false;
            }
        }
    }
}
