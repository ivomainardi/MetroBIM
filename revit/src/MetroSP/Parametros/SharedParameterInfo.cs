using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;

namespace MetroSP.Parametros
{
    /// <summary>Uma linha da lista de parâmetros compartilhados do arquivo aberto.</summary>
    public class SharedParameterInfo
    {
        public string Nome { get; set; }
        public Guid Guid { get; set; }
        public ForgeTypeId Spec { get; set; }
        public string TipoDeDado { get; set; }
        /// <summary>Tipo como aparece no .txt (TEXT, LENGTH...), ou null se não for conhecido.</summary>
        public string TipoTxt { get; set; }
        public string Grupo { get; set; }
        public string InstanciaOuTipo { get; set; }
        public string Categorias { get; set; }
        /// <summary>true = parâmetro de projeto (vinculado a categorias), como lista o diálogo Parâmetros de projeto.</summary>
        public bool Vinculado { get; set; } = true;
        /// <summary>false = parâmetro de projeto comum (sem GUID), que não vem de .txt.</summary>
        public bool Compartilhado { get; set; } = true;
        public string GuidTexto => Compartilhado ? Guid.ToString() : "(não compartilhado)";
        public string Onde => !Vinculado ? "Só em famílias / tags"
            : Compartilhado ? "Projeto (compartilhado)" : "Projeto (não compartilhado)";
        public bool VisivelBool { get; set; } = true;
        public string Visivel => VisivelBool ? "Sim" : "Não";
        public string Formula { get; set; }

        // Preenchidos a partir dos arquivos de referência (ReferenceLibrary).
        public List<SharedParamDef> Origens { get; set; } = new List<SharedParamDef>();
        public string Origem { get; set; }
        public string GrupoNoArquivo { get; set; }
        public string Situacao { get; set; }
        /// <summary>0 = ok, 1 = não está nas referências, 2 = nome ou tipo diferente.</summary>
        public int Gravidade { get; set; }

        // Comparação com o .txt base (MetroShared), preenchida por SharedParameterCollector.MarcarPadrao.
        public string Padrao { get; set; }
        /// <summary>0 = no padrão (ou sem base), 1 = fora do padrão, 2 = duplica ou diverge de um parâmetro do padrão.</summary>
        public int GravidadePadrao { get; set; }

        // Uso no arquivo, preenchido por UsoParametros.Analisar.
        public string Uso { get; set; } = UsoParametros.NaoVerificado;
        /// <summary>true = a análise não achou uso e o parâmetro pode ser deletado.</summary>
        public bool PodeDeletar { get; set; }
        public bool UsoVerificado => Uso != UsoParametros.NaoVerificado;

        /// <summary>Elemento do parâmetro no documento (ParameterElement no projeto, FamilyParameter na família).</summary>
        internal ElementId Id { get; set; }
        internal Definition Definicao { get; set; }
        internal FamilyParameter ParametroFamilia { get; set; }

        /// <summary>Chave estável entre recargas do modelo.</summary>
        internal string Chave => Compartilhado ? Guid.ToString() : "ns:" + Nome;
    }

    /// <summary>Linha da aba "Comparar com .txt".</summary>
    public class ComparacaoItem
    {
        public string Situacao { get; set; }
        public string NomeModelo { get; set; }
        public string NomeTxt { get; set; }
        public string TipoModelo { get; set; }
        public string TipoNoTxt { get; set; }
        public Guid Guid { get; set; }
        public string GrupoNoTxt { get; set; }
        public SharedParameterInfo Modelo { get; set; }
        public SharedParamDef Txt { get; set; }
        public bool Compativel => Situacao == Comparacao.Compativel;
        public bool SoNoTxt => Situacao == Comparacao.SoNoTxt;
    }

    public static class Comparacao
    {
        public const string Compativel = "Compatível";
        public const string NomeDiferente = "Nome diferente";
        public const string TipoDiferente = "Tipo diferente";
        public const string NomeEmUso = "Nome em uso (outro GUID)";
        public const string SoNoModelo = "Só no modelo";
        public const string SoNoTxt = "Só no .txt";

        /// <summary>
        /// Compara os parâmetros do modelo com um arquivo de referência. guidsDoArquivo: todos os GUIDs
        /// presentes no arquivo (inclusive fora do escopo mostrado), para não marcá-los como "só no .txt".
        /// </summary>
        public static List<ComparacaoItem> Comparar(List<SharedParameterInfo> modelo, SharedParamTxt txt, IEnumerable<Guid> guidsDoArquivo = null)
        {
            var lista = new List<ComparacaoItem>();
            foreach (SharedParameterInfo m in modelo)
            {
                var c = new ComparacaoItem { NomeModelo = m.Nome, TipoModelo = m.TipoTxt ?? m.TipoDeDado, Guid = m.Guid, Modelo = m };
                if (txt.PorGuid.TryGetValue(m.Guid, out SharedParamDef d))
                {
                    Preencher(c, d);
                    c.Situacao = d.Nome != m.Nome ? NomeDiferente
                        : !SharedParameterCollector.MesmoTipo(m, d.TipoDeDado) ? TipoDiferente
                        : Compativel;
                }
                else if (txt.PorNome.TryGetValue(m.Nome, out SharedParamDef outro))
                {
                    Preencher(c, outro);
                    c.Situacao = NomeEmUso;
                }
                else
                {
                    c.Situacao = SoNoModelo;
                }
                lista.Add(c);
            }

            var doModelo = new HashSet<Guid>(guidsDoArquivo ?? modelo.Select(m => m.Guid));
            foreach (SharedParamDef d in txt.Definicoes.Where(d => !doModelo.Contains(d.Guid)))
            {
                var c = new ComparacaoItem { Guid = d.Guid, Situacao = SoNoTxt };
                Preencher(c, d);
                lista.Add(c);
            }

            // Pendências primeiro.
            return lista.OrderBy(c => Ordem(c.Situacao)).ThenBy(c => c.NomeModelo ?? c.NomeTxt, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static void Preencher(ComparacaoItem c, SharedParamDef d)
        {
            c.Txt = d;
            c.NomeTxt = d.Nome;
            c.TipoNoTxt = d.TipoDeDado;
            c.GrupoNoTxt = d.Grupo;
        }

        private static int Ordem(string s) =>
            s == NomeDiferente ? 0 : s == TipoDiferente ? 1 : s == NomeEmUso ? 2 : s == SoNoModelo ? 3 : s == Compativel ? 4 : 5;
    }

    /// <summary>Coleta todos os parâmetros compartilhados do documento (projeto ou família).</summary>
    public static class SharedParameterCollector
    {
        public static List<SharedParameterInfo> Collect(Document doc)
        {
            List<SharedParameterInfo> itens = doc.IsFamilyDocument ? FromFamily(doc) : FromProject(doc);
            return itens.OrderBy(i => i.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>
        /// Preenche Arquivo de origem, Grupo no .txt e Situação a partir dos arquivos de referência,
        /// procurando pelo GUID (o Revit não guarda de qual .txt o parâmetro veio).
        /// </summary>
        public static void Enriquecer(List<SharedParameterInfo> itens, ReferenceLibrary refs)
        {
            foreach (SharedParameterInfo i in itens)
            {
                if (!i.Compartilhado)
                {
                    i.Origens = new List<SharedParamDef>();
                    i.Origem = null;
                    i.GrupoNoArquivo = null;
                    i.Situacao = "Não compartilhado (sem GUID, não vem de .txt)";
                    i.Gravidade = 0;
                    continue;
                }
                i.Origens = refs.Buscar(i.Guid);
                SharedParamDef d = i.Origens.FirstOrDefault();
                i.GrupoNoArquivo = d?.Grupo;
                if (refs.Arquivos.Count == 0)
                {
                    i.Origem = null;
                    i.Situacao = "(nenhuma referência)";
                    i.Gravidade = 0;
                }
                else if (d == null)
                {
                    i.Origem = "não encontrado nas referências";
                    i.Situacao = "Não está nas referências";
                    i.Gravidade = 1;
                }
                else
                {
                    i.Origem = string.Join(", ", i.Origens.Select(o => o.Arquivo.Nome));
                    if (d.Nome != i.Nome)
                    {
                        i.Situacao = $"Nome diferente: \"{d.Nome}\"";
                        i.Gravidade = 2;
                    }
                    else if (!MesmoTipo(i, d.TipoDeDado))
                    {
                        i.Situacao = $"Tipo diferente: {d.TipoDeDado}";
                        i.Gravidade = 2;
                    }
                    else
                    {
                        i.Situacao = "OK";
                        i.Gravidade = 0;
                    }
                }
            }
        }

        /// <summary>
        /// Compara cada parâmetro com o .txt base (o MetroShared, a mesma base das outras ferramentas):
        /// no padrão, nome diferente do padrão, duplicado (mesmo nome do padrão com outro GUID), parecido
        /// com um nome do padrão, ou fora do padrão. Marca também nomes repetidos dentro do próprio arquivo.
        /// </summary>
        public static void MarcarPadrao(List<SharedParameterInfo> itens, SharedParamTxt padrao)
        {
            Dictionary<string, SharedParamDef> parecidos = null;
            if (padrao != null)
            {
                parecidos = new Dictionary<string, SharedParamDef>();
                foreach (SharedParamDef d in padrao.Definicoes)
                {
                    string k = NomeNormalizado(d.Nome);
                    if (k.Length > 0 && !parecidos.ContainsKey(k)) parecidos[k] = d;
                }
            }
            // Nomes repetidos no arquivo com GUIDs diferentes (ex.: "Classificação" e "Classificacao").
            var repetidos = new HashSet<string>(itens
                .GroupBy(i => NomeNormalizado(i.Nome))
                .Where(g => g.Key.Length > 0 && g.Select(i => i.Chave).Distinct().Count() > 1)
                .Select(g => g.Key));

            foreach (SharedParameterInfo i in itens)
            {
                string texto;
                int grav;
                if (padrao == null)
                {
                    texto = "(sem base)";
                    grav = 0;
                }
                else if (i.Compartilhado && padrao.PorGuid.TryGetValue(i.Guid, out SharedParamDef mesmo))
                {
                    if (mesmo.Nome == i.Nome) { texto = "No padrão"; grav = 0; }
                    else { texto = $"Nome diferente do padrão: \"{mesmo.Nome}\""; grav = 2; }
                }
                else if (padrao.PorNome.TryGetValue(i.Nome, out SharedParamDef homonimo))
                {
                    texto = i.Compartilhado
                        ? $"Duplicado: o padrão tem \"{homonimo.Nome}\" com outro GUID"
                        : $"Duplicado: o padrão tem \"{homonimo.Nome}\" (este não é compartilhado)";
                    grav = 2;
                }
                else if (parecidos.TryGetValue(NomeNormalizado(i.Nome), out SharedParamDef parecido))
                {
                    texto = $"Parecido com o padrão: \"{parecido.Nome}\"";
                    grav = 2;
                }
                else
                {
                    texto = "Fora do padrão";
                    grav = 1;
                }
                if (repetidos.Contains(NomeNormalizado(i.Nome)))
                {
                    texto += " · nome repetido no arquivo";
                    grav = Math.Max(grav, 1);
                }
                i.Padrao = texto;
                i.GravidadePadrao = grav;
            }
        }

        /// <summary>Nome sem acentos, maiúsculas, espaços e pontuação, para achar duplicados escritos de outro jeito.</summary>
        public static string NomeNormalizado(string nome)
        {
            if (string.IsNullOrEmpty(nome)) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char c in nome.Normalize(System.Text.NormalizationForm.FormD))
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        private static List<SharedParameterInfo> FromProject(Document doc)
        {
            var itens = new List<SharedParameterInfo>();
            foreach (SharedParameterElement spe in new FilteredElementCollector(doc).OfClass(typeof(SharedParameterElement)).Cast<SharedParameterElement>())
            {
                InternalDefinition def = spe.GetDefinition();
                ForgeTypeId spec = def.GetDataType();
                var info = new SharedParameterInfo
                {
                    Nome = def.Name,
                    Guid = spe.GuidValue,
                    Id = spe.Id,
                    Definicao = def,
                    Spec = spec,
                    TipoDeDado = SpecLabel(spec),
                    TipoTxt = TipoLegado(spec),
                    Grupo = GroupLabel(def.GetGroupTypeId()),
                    VisivelBool = def.Visible,
                };

                // Vínculo de projeto (parâmetro de projeto/compartilhado aplicado a categorias).
                if (doc.ParameterBindings.get_Item(def) is ElementBinding binding)
                {
                    info.InstanciaOuTipo = binding is InstanceBinding ? "Instância" : "Tipo";
                    info.Categorias = string.Join(", ", binding.Categories.Cast<Category>().Select(c => c.Name).OrderBy(n => n));
                }
                else
                {
                    // Existe no arquivo porque alguma família ou etiqueta carregada usa o parâmetro;
                    // por isso não aparece no diálogo Parâmetros de projeto do Revit.
                    info.Vinculado = false;
                    info.InstanciaOuTipo = "-";
                    info.Categorias = "-";
                }
                itens.Add(info);
            }

            // Parâmetros de projeto não compartilhados: aparecem no diálogo do Revit, mas não têm GUID.
            DefinitionBindingMapIterator it = doc.ParameterBindings.ForwardIterator();
            while (it.MoveNext())
            {
                if (!(it.Key is InternalDefinition def) || !(it.Current is ElementBinding binding)) continue;
                if (doc.GetElement(def.Id) is SharedParameterElement) continue;
                ForgeTypeId spec = def.GetDataType();
                itens.Add(new SharedParameterInfo
                {
                    Nome = def.Name,
                    Compartilhado = false,
                    Id = def.Id,
                    Definicao = def,
                    Spec = spec,
                    TipoDeDado = SpecLabel(spec),
                    TipoTxt = TipoLegado(spec),
                    Grupo = GroupLabel(def.GetGroupTypeId()),
                    VisivelBool = def.Visible,
                    InstanciaOuTipo = binding is InstanceBinding ? "Instância" : "Tipo",
                    Categorias = string.Join(", ", binding.Categories.Cast<Category>().Select(c => c.Name).OrderBy(n => n)),
                });
            }
            return itens;
        }

        private static List<SharedParameterInfo> FromFamily(Document doc)
        {
            FamilyManager fm = doc.FamilyManager;
            return fm.GetParameters()
                .Where(p => p.IsShared)
                .Select(p => new SharedParameterInfo
                {
                    Nome = p.Definition.Name,
                    Guid = p.GUID,
                    Id = p.Id,
                    Definicao = p.Definition,
                    ParametroFamilia = p,
                    Spec = p.Definition.GetDataType(),
                    TipoDeDado = SpecLabel(p.Definition.GetDataType()),
                    TipoTxt = TipoLegado(p.Definition.GetDataType()),
                    Grupo = GroupLabel(p.Definition.GetGroupTypeId()),
                    InstanciaOuTipo = p.IsInstance ? "Instância" : "Tipo",
                    Categorias = doc.OwnerFamily?.FamilyCategory?.Name,
                    VisivelBool = ((InternalDefinition)p.Definition).Visible,
                    Formula = p.Formula,
                })
                .ToList();
        }

        private static Dictionary<string, string> _legados;

        /// <summary>Nome do tipo como o .txt grava (TEXT, LENGTH...). null quando não conhecemos o equivalente.</summary>
        public static string TipoLegado(ForgeTypeId spec)
        {
            if (spec == null || string.IsNullOrEmpty(spec.TypeId)) return null;
            if (spec.TypeId.StartsWith("autodesk.revit.category.family", StringComparison.OrdinalIgnoreCase)) return "FAMILYTYPE";
            if (_legados == null)
            {
                var pares = new List<(ForgeTypeId, string)>
                {
                    (SpecTypeId.String.Text, "TEXT"),
                    (SpecTypeId.String.MultilineText, "MULTILINETEXT"),
                    (SpecTypeId.String.Url, "URL"),
                    (SpecTypeId.Int.Integer, "INTEGER"),
                    (SpecTypeId.Number, "NUMBER"),
                    (SpecTypeId.Length, "LENGTH"),
                    (SpecTypeId.Area, "AREA"),
                    (SpecTypeId.Volume, "VOLUME"),
                    (SpecTypeId.Angle, "ANGLE"),
                    (SpecTypeId.Slope, "SLOPE"),
                    (SpecTypeId.Currency, "CURRENCY"),
                    (SpecTypeId.Mass, "MASS"),
                    (SpecTypeId.Boolean.YesNo, "YESNO"),
                    (SpecTypeId.Reference.Material, "MATERIAL"),
                    (SpecTypeId.Reference.Image, "IMAGE"),
                };
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (id, nome) in pares) d[SemVersao(id.TypeId)] = nome;
                _legados = d;
            }
            return _legados.TryGetValue(SemVersao(spec.TypeId), out string n) ? n : null;
        }

        /// <summary>
        /// Compara o tipo do modelo com o DATATYPE do .txt. Se não for possível saber
        /// (tipo de disciplina sem equivalente conhecido), considera igual.
        /// </summary>
        public static bool MesmoTipo(SharedParameterInfo m, string tipoTxt)
        {
            if (string.IsNullOrEmpty(tipoTxt) || m.Spec == null) return true;
            if (tipoTxt.Contains(":")) return string.Equals(SemVersao(tipoTxt), SemVersao(m.Spec.TypeId), StringComparison.OrdinalIgnoreCase);
            if (m.TipoTxt == null) return true;
            return string.Equals(m.TipoTxt, tipoTxt, StringComparison.OrdinalIgnoreCase);
        }

        private static string SemVersao(string typeId) => Regex.Replace(typeId ?? "", @"-\d+(\.\d+)*$", "");

        private static string SpecLabel(ForgeTypeId t)
        {
            try { return LabelUtils.GetLabelForSpec(t); } catch { return t?.TypeId; }
        }

        private static string GroupLabel(ForgeTypeId t)
        {
            try { return LabelUtils.GetLabelForGroup(t); } catch { return t?.TypeId; }
        }
    }

    /// <summary>Grava um .txt de parâmetros compartilhados reutilizável no Revit, mantendo os GUIDs.</summary>
    public static class SharedParameterExporter
    {
        /// <summary>
        /// Usa a própria API do Revit para gravar o arquivo (formato garantido). O arquivo de
        /// parâmetros configurado no Revit é trocado temporariamente e restaurado no final.
        /// Retorna os nomes que não puderam ser exportados.
        /// </summary>
        public static List<string> ExportarTxt(Application app, IEnumerable<SharedParameterInfo> itens, string caminho)
        {
            var falhas = new List<string>();
            string original = app.SharedParametersFilename;
            if (File.Exists(caminho)) File.Delete(caminho);
            File.Create(caminho).Dispose(); // o Revit preenche o cabeçalho ao abrir um arquivo vazio
            try
            {
                app.SharedParametersFilename = caminho;
                DefinitionFile arquivo = app.OpenSharedParameterFile()
                    ?? throw new InvalidOperationException("O Revit não conseguiu abrir o arquivo de destino.");

                foreach (SharedParameterInfo i in itens)
                {
                    if (!i.Compartilhado) { falhas.Add(i.Nome + " (não compartilhado, sem GUID)"); continue; }
                    if (i.Spec == null) { falhas.Add(i.Nome + " (tipo desconhecido)"); continue; }
                    try
                    {
                        string nomeGrupo = string.IsNullOrWhiteSpace(i.GrupoNoArquivo) ? "MetroSP" : i.GrupoNoArquivo;
                        DefinitionGroup grupo = arquivo.Groups.get_Item(nomeGrupo) ?? arquivo.Groups.Create(nomeGrupo);
                        var opcoes = new ExternalDefinitionCreationOptions(i.Nome, i.Spec)
                        {
                            GUID = i.Guid,
                            Visible = i.VisivelBool,
                            Description = i.Origens.FirstOrDefault()?.Descricao ?? "",
                        };
                        grupo.Definitions.Create(opcoes);
                    }
                    catch (Exception ex)
                    {
                        falhas.Add($"{i.Nome} ({ex.Message})");
                    }
                }
            }
            finally
            {
                try { app.SharedParametersFilename = original ?? ""; }
                catch { /* sem arquivo configurado antes: nada a restaurar */ }
            }
            return falhas;
        }
    }
}
