using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;

namespace MetroSP.Parametros
{
    /// <summary>Um parâmetro disponível nos elementos do escopo (origem ou destino da cópia).</summary>
    public class ParamRef
    {
        public string Nome { get; set; }
        public Guid? Guid { get; set; }
        public BuiltInParameter BuiltIn { get; set; } = BuiltInParameter.INVALID;
        public ForgeTypeId Spec { get; set; }
        public StorageType Storage { get; set; }
        public bool DoTipo { get; set; }
        /// <summary>true se em algum elemento o parâmetro pode ser editado.</summary>
        public bool Editavel { get; set; }
        public string TipoLabel { get; set; }

        public bool EhDoRevit => BuiltIn != BuiltInParameter.INVALID;
        public string Origem => Guid != null ? "compartilhado" : EhDoRevit ? "Revit" : "projeto/família";
        public string Chave => (Guid?.ToString() ?? (EhDoRevit ? "bip:" + (int)BuiltIn : "nome:" + Nome + "|" + Storage + "|" + Spec?.TypeId)) + (DoTipo ? "|T" : "|I");
        public string Rotulo => $"{Nome}   —   {TipoLabel} · {(DoTipo ? "tipo" : "instância")} · {Origem}";
        public override string ToString() => Rotulo;

        public bool Medida => Spec != null && !string.IsNullOrEmpty(Spec.TypeId) && Storage == StorageType.Double && UnitUtils.IsMeasurableSpec(Spec);
        public bool SimNao => Spec != null && Spec.TypeId == SpecTypeId.Boolean.YesNo.TypeId;

        public Parameter Achar(Element e)
        {
            if (e == null) return null;
            if (Guid != null) return e.get_Parameter(Guid.Value);
            if (EhDoRevit) return e.get_Parameter(BuiltIn);
            return e.GetParameters(Nome).FirstOrDefault(p => !p.IsShared && p.StorageType == Storage);
        }

        /// <summary>Parâmetro e o elemento que o guarda (a própria instância ou o tipo dela).</summary>
        public (Parameter Param, Element Dono) Resolver(Document doc, Element instancia)
        {
            Element dono = DoTipo ? doc.GetElement(instancia.GetTypeId()) : instancia;
            return (Achar(dono), dono);
        }
    }

    /// <summary>Opções de conversão escolhidas na janela.</summary>
    public class OpcoesCopia
    {
        /// <summary>Unidade usada quando um lado é medida (comprimento, área...) e o outro é número ou texto sem unidade.</summary>
        public ForgeTypeId Unidade { get; set; }
        public bool TextoComUnidade { get; set; } = true;
        public bool Arredondar { get; set; }
        public bool Sobrescrever { get; set; }
    }

    /// <summary>Uma linha da análise: o que acontece com um elemento.</summary>
    public class LinhaCopia
    {
        public string Situacao { get; set; }
        public string Elemento { get; set; }
        public long Id { get; set; }
        public string Categoria { get; set; }
        public string ValorOrigem { get; set; }
        public string ValorAtual { get; set; }
        public string ValorNovo { get; set; }
        public string Motivo { get; set; }

        public ElementId ElementoId { get; set; }
        public ElementId DonoId { get; set; }
        public object Valor { get; set; }
        public bool Grava => Situacao == ValueCopier.Copia || Situacao == ValueCopier.Sobrescreve;
    }

    public class AnaliseCopia
    {
        public List<LinhaCopia> Linhas { get; } = new List<LinhaCopia>();
        /// <summary>Gravações efetivas (uma por dono: parâmetros de tipo são gravados uma vez por tipo).</summary>
        public List<(ElementId Dono, object Valor)> Gravacoes { get; } = new List<(ElementId, object)>();
        public List<string> Impactos { get; } = new List<string>();
        public List<string> Propostas { get; } = new List<string>();
        public int Conta(string situacao) => Linhas.Count(l => l.Situacao == situacao);
    }

    /// <summary>
    /// Copia valores de um parâmetro para outro nos elementos do escopo, convertendo entre tipos de dado
    /// (texto, inteiro, número, medidas com unidade, sim/não). Primeiro analisa (nada é alterado),
    /// depois grava numa única transação.
    /// </summary>
    public static class ValueCopier
    {
        public const string Copia = "Copia";
        public const string Sobrescreve = "Sobrescreve";
        public const string JaIgual = "Já igual";
        public const string Mantido = "Destino preenchido (mantido)";
        public const string OrigemVazia = "Origem vazia";
        public const string NaoConverte = "Não converte";
        public const string SomenteLeitura = "Destino somente leitura";
        public const string SemDestino = "Sem o parâmetro de destino";
        public const string SemOrigem = "Sem o parâmetro de origem";
        public const string ConflitoTipo = "Conflito no tipo";
        public const string Bloqueado = "Bloqueado por outro usuário";

        // ------------------------------------------------------------------ escopo e candidatos

        public static List<Element> Elementos(Document doc, ICollection<ElementId> selecao, ElementId vista)
        {
            IEnumerable<Element> elems;
            if (selecao != null)
                elems = selecao.Select(doc.GetElement).Where(e => e != null && !(e is ElementType));
            else
                elems = (vista != null ? new FilteredElementCollector(doc, vista) : new FilteredElementCollector(doc))
                    .WhereElementIsNotElementType();
            return elems.Where(e => e.Category != null && e.Category.CategoryType == CategoryType.Model && !(e is View)).ToList();
        }

        /// <summary>Parâmetros presentes nos elementos (uma instância por tipo basta para conhecer os parâmetros).</summary>
        public static List<ParamRef> Candidatos(Document doc, IEnumerable<Element> elementos)
        {
            var mapa = new Dictionary<string, ParamRef>();
            void Incluir(Element e, bool doTipo)
            {
                if (e == null) return;
                foreach (Parameter p in e.Parameters)
                {
                    if (p.Definition == null || p.StorageType == StorageType.None) continue;
                    var r = new ParamRef
                    {
                        Nome = p.Definition.Name,
                        Storage = p.StorageType,
                        DoTipo = doTipo,
                        Spec = SafeSpec(p.Definition),
                    };
                    if (p.IsShared) r.Guid = p.GUID;
                    else if (p.Definition is InternalDefinition d && d.BuiltInParameter != BuiltInParameter.INVALID) r.BuiltIn = d.BuiltInParameter;
                    if (!mapa.TryGetValue(r.Chave, out ParamRef existente))
                    {
                        r.TipoLabel = TipoLabel(r);
                        mapa[r.Chave] = existente = r;
                    }
                    if (!p.IsReadOnly) existente.Editavel = true;
                }
            }

            foreach (var grupo in elementos.GroupBy(e => e.GetTypeId()).Take(3000))
            {
                Element inst = grupo.First();
                Incluir(inst, false);
                Incluir(doc.GetElement(grupo.Key), true);
            }
            return mapa.Values.OrderBy(r => r.Nome, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.DoTipo).ToList();
        }

        private static ForgeTypeId SafeSpec(Definition d)
        {
            try { return d.GetDataType(); } catch { return null; }
        }

        private static string TipoLabel(ParamRef r)
        {
            if (r.Spec != null && !string.IsNullOrEmpty(r.Spec.TypeId))
            {
                try { return LabelUtils.GetLabelForSpec(r.Spec); } catch { /* sem rótulo */ }
            }
            return r.Storage == StorageType.String ? "Texto" : r.Storage == StorageType.Integer ? "Inteiro"
                : r.Storage == StorageType.Double ? "Número" : "Referência";
        }

        /// <summary>Unidade padrão do projeto para o lado que é medida (ou null se nenhum lado for).</summary>
        public static ForgeTypeId SpecDaUnidade(ParamRef origem, ParamRef destino)
        {
            if (origem == null || destino == null) return null;
            if (origem.Medida && destino.Medida) return null;
            if (destino.Medida) return destino.Spec;
            if (origem.Medida && (destino.Storage != StorageType.Double || !destino.Medida)) return origem.Spec;
            return null;
        }

        // ------------------------------------------------------------------ análise

        public static AnaliseCopia Analisar(Document doc, IList<Element> elementos, ParamRef origem, ParamRef destino, OpcoesCopia op)
        {
            var a = new AnaliseCopia();
            Units unidades = doc.GetUnits();
            var porDono = new Dictionary<ElementId, List<LinhaCopia>>();

            foreach (Element e in elementos)
            {
                var l = new LinhaCopia
                {
                    Elemento = NomeElemento(doc, e),
                    Id = e.Id.Value,
                    ElementoId = e.Id,
                    Categoria = e.Category?.Name,
                };
                a.Linhas.Add(l);

                Parameter po = origem.Resolver(doc, e).Param;
                var (pd, dono) = destino.Resolver(doc, e);
                if (po == null) { l.Situacao = SemOrigem; continue; }
                if (pd == null) { l.Situacao = SemDestino; continue; }
                l.ValorOrigem = Exibir(po);
                l.ValorAtual = Exibir(pd);
                l.DonoId = dono.Id;

                if (Vazio(po)) { l.Situacao = OrigemVazia; continue; }
                if (pd.IsReadOnly) { l.Situacao = SomenteLeitura; continue; }
                if (doc.IsWorkshared && WorksharingUtils.GetCheckoutStatus(doc, dono.Id) == CheckoutStatus.OwnedByOtherUser)
                {
                    l.Situacao = Bloqueado;
                    continue;
                }

                if (!Converter(unidades, po, origem, destino, op, out object valor, out string motivo))
                {
                    l.Situacao = NaoConverte;
                    l.Motivo = motivo;
                    continue;
                }
                l.Valor = valor;
                l.ValorNovo = ExibirValor(unidades, destino, valor);

                if (Igual(pd, valor)) l.Situacao = JaIgual;
                else if (!Vazio(pd) && !op.Sobrescrever) l.Situacao = Mantido;
                else l.Situacao = Vazio(pd) ? Copia : Sobrescreve;

                if (!porDono.TryGetValue(dono.Id, out var lista)) porDono[dono.Id] = lista = new List<LinhaCopia>();
                lista.Add(l);
            }

            // Parâmetro de tipo: um valor por tipo. Instâncias pedindo valores diferentes = conflito.
            foreach (var kv in porDono)
            {
                List<LinhaCopia> ls = kv.Value;
                List<LinhaCopia> gravar = ls.Where(x => x.Grava).ToList();
                if (gravar.Count == 0) continue;
                if (destino.DoTipo && ls.Select(x => x.ValorNovo).Distinct().Count() > 1)
                {
                    foreach (LinhaCopia x in ls)
                    {
                        x.Situacao = ConflitoTipo;
                        x.Motivo = "instâncias do mesmo tipo têm valores de origem diferentes";
                    }
                    continue;
                }
                a.Gravacoes.Add((kv.Key, gravar[0].Valor));
            }

            Resumir(doc, a, elementos, origem, destino, op);
            return a;
        }

        private static void Resumir(Document doc, AnaliseCopia a, IList<Element> elementos, ParamRef origem, ParamRef destino, OpcoesCopia op)
        {
            int copia = a.Conta(Copia), sobrescreve = a.Conta(Sobrescreve), falha = a.Conta(NaoConverte);
            a.Impactos.Add($"{elementos.Count} elementos analisados: {copia} recebem valor, {sobrescreve} têm o valor atual substituído, " +
                           $"{a.Conta(JaIgual)} já estão iguais.");
            if (a.Conta(Mantido) > 0) a.Impactos.Add($"{a.Conta(Mantido)} já têm valor no destino e serão mantidos (\"Só preencher vazios\").");
            if (falha > 0) a.Impactos.Add($"{falha} valores não convertem e não serão gravados.");
            if (a.Conta(SomenteLeitura) > 0) a.Impactos.Add($"{a.Conta(SomenteLeitura)} elementos têm o destino somente leitura (fórmula, grupo ou parâmetro calculado).");
            if (a.Conta(SemDestino) > 0) a.Impactos.Add($"{a.Conta(SemDestino)} elementos não têm o parâmetro de destino (categoria não vinculada).");
            if (a.Conta(Bloqueado) > 0) a.Impactos.Add($"{a.Conta(Bloqueado)} elementos estão com outro usuário (worksharing).");
            if (a.Conta(ConflitoTipo) > 0) a.Impactos.Add($"{a.Conta(ConflitoTipo)} elementos em conflito: o destino é de tipo e instâncias do mesmo tipo pedem valores diferentes.");
            if (destino.DoTipo && a.Gravacoes.Count > 0)
                a.Impactos.Add($"O destino é parâmetro de TIPO: {a.Gravacoes.Count} tipos serão alterados, o que vale para todas as instâncias deles no modelo, inclusive fora do escopo.");
            int emGrupo = a.Linhas.Where(l => l.Grava).Count(l => doc.GetElement(l.ElementoId)?.GroupId != ElementId.InvalidElementId);
            if (emGrupo > 0) a.Impactos.Add($"{emGrupo} elementos estão em grupos: a gravação pode falhar se o parâmetro não puder variar por grupo.");

            // Propostas conforme o par de tipos.
            bool mesmoTipo = origem.Storage == destino.Storage && origem.Spec?.TypeId == destino.Spec?.TypeId;
            if (mesmoTipo) a.Propostas.Add("Mesmo tipo de dado: cópia exata, sem conversão.");
            else
            {
                if (origem.Medida && destino.Medida)
                    a.Propostas.Add($"{origem.TipoLabel} → {destino.TipoLabel}: unidades incompatíveis, não há conversão. Escolha um destino do mesmo tipo, Número ou Texto.");
                ForgeTypeId specUnidade = SpecDaUnidade(origem, destino);
                if (specUnidade != null && op.Unidade != null)
                {
                    string un = Rotulo(op.Unidade);
                    a.Propostas.Add(destino.Medida
                        ? $"Números sem unidade serão lidos em {un}. Se a origem estiver em outra unidade, troque em \"Unidade\"."
                        : $"Os valores serão escritos em {un}. Troque em \"Unidade\" se o destino espera outra.");
                }
                if (destino.Storage == StorageType.String && origem.Medida)
                    a.Propostas.Add(op.TextoComUnidade ? "O texto vai com a unidade (ex.: \"2,50 m\"). Desmarque \"Texto com unidade\" para gravar só o número." : "O texto vai só com o número, sem unidade.");
                if (destino.Storage == StorageType.Integer && !destino.SimNao && falha > 0 && !op.Arredondar &&
                    a.Linhas.Any(l => l.Situacao == NaoConverte && l.Motivo == "tem casas decimais"))
                    a.Propostas.Add("Há valores com casas decimais: marque \"Arredondar decimais\" para gravá-los como inteiros.");
                if (destino.SimNao)
                    a.Propostas.Add("Sim/Não aceita: Sim, Não, S, N, Yes, No, 1, 0, Verdadeiro e Falso.");
                if (origem.Storage == StorageType.String && destino.Storage != StorageType.String && falha > 0)
                    a.Propostas.Add("Alguns textos não são números válidos (veja \"Não converte\"). Corrija na origem ou exporte a análise para tratar.");
            }
            if (a.Conta(Mantido) > 0 && !op.Sobrescrever)
                a.Propostas.Add("Para substituir os valores já preenchidos, marque \"Sobrescrever\".");
            if (a.Conta(ConflitoTipo) > 0)
                a.Propostas.Add("Para os conflitos, use um destino de instância ou uniformize a origem entre as instâncias do tipo.");
        }

        // ------------------------------------------------------------------ conversão

        /// <summary>Converte o valor do parâmetro de origem para o tipo do destino.</summary>
        public static bool Converter(Units unidades, Parameter po, ParamRef origem, ParamRef destino, OpcoesCopia op, out object valor, out string motivo)
        {
            valor = null;
            motivo = null;
            bool mesmoSpec = origem.Spec?.TypeId == destino.Spec?.TypeId && origem.Storage == destino.Storage;

            switch (destino.Storage)
            {
                case StorageType.String:
                    valor = po.StorageType == StorageType.String ? po.AsString()
                        : origem.Medida && !op.TextoComUnidade ? Numero(UnitUtils.ConvertFromInternalUnits(po.AsDouble(), UnidadeOuProjeto(unidades, origem.Spec, op)))
                        : po.AsValueString();
                    return valor != null;

                case StorageType.Integer:
                    if (destino.SimNao) return SimNao(po, out valor, out motivo);
                    if (po.StorageType == StorageType.Integer) { valor = po.AsInteger(); return true; }
                    if (!NumeroDe(unidades, po, origem, op, out double n, out motivo)) return false;
                    if (Math.Abs(n - Math.Round(n)) > 1e-9 && !op.Arredondar) { motivo = "tem casas decimais"; return false; }
                    valor = (int)Math.Round(n, MidpointRounding.AwayFromZero);
                    return true;

                case StorageType.Double:
                    if (mesmoSpec) { valor = po.AsDouble(); return true; }
                    if (origem.Medida && destino.Medida) { motivo = $"{origem.TipoLabel} → {destino.TipoLabel}: unidades incompatíveis"; return false; }
                    if (destino.Medida)
                    {
                        ForgeTypeId un = UnidadeOuProjeto(unidades, destino.Spec, op);
                        if (po.StorageType == StorageType.String)
                        {
                            string t = po.AsString()?.Trim();
                            if (TryDouble(t, out double d)) { valor = UnitUtils.ConvertToInternalUnits(d, un); return true; }
                            // Texto com unidade ("250 cm", "2,5 m"): o Revit interpreta.
                            if (UnitFormatUtils.TryParse(unidades, destino.Spec, t, out double v)) { valor = v; return true; }
                            motivo = "texto não é uma medida";
                            return false;
                        }
                        if (po.StorageType == StorageType.Integer) { valor = UnitUtils.ConvertToInternalUnits(po.AsInteger(), un); return true; }
                        valor = UnitUtils.ConvertToInternalUnits(po.AsDouble(), un);
                        return true;
                    }
                    // Destino é número sem unidade.
                    if (!NumeroDe(unidades, po, origem, op, out double num, out motivo)) return false;
                    valor = num;
                    return true;

                default:
                    motivo = "destino do tipo referência não é suportado";
                    return false;
            }
        }

        /// <summary>Valor numérico da origem (medidas na unidade escolhida).</summary>
        private static bool NumeroDe(Units unidades, Parameter po, ParamRef origem, OpcoesCopia op, out double n, out string motivo)
        {
            motivo = null;
            n = 0;
            switch (po.StorageType)
            {
                case StorageType.Integer: n = po.AsInteger(); return true;
                case StorageType.Double:
                    n = origem.Medida ? UnitUtils.ConvertFromInternalUnits(po.AsDouble(), UnidadeOuProjeto(unidades, origem.Spec, op)) : po.AsDouble();
                    return true;
                case StorageType.String:
                    if (TryDouble(po.AsString()?.Trim(), out n)) return true;
                    motivo = "texto não é um número";
                    return false;
                default:
                    motivo = "origem do tipo referência não vira número";
                    return false;
            }
        }

        private static bool SimNao(Parameter po, out object valor, out string motivo)
        {
            valor = null;
            motivo = null;
            if (po.StorageType == StorageType.Integer)
            {
                int i = po.AsInteger();
                if (i == 0 || i == 1) { valor = i; return true; }
            }
            else if (po.StorageType == StorageType.Double)
            {
                double d = po.AsDouble();
                if (d == 0 || d == 1) { valor = (int)d; return true; }
            }
            else if (po.StorageType == StorageType.String)
            {
                string t = (po.AsString() ?? "").Trim().ToLowerInvariant();
                if (new[] { "sim", "s", "yes", "y", "1", "true", "verdadeiro", "x" }.Contains(t)) { valor = 1; return true; }
                if (new[] { "não", "nao", "n", "no", "0", "false", "falso" }.Contains(t)) { valor = 0; return true; }
            }
            motivo = "não é Sim/Não";
            return false;
        }

        private static ForgeTypeId UnidadeOuProjeto(Units unidades, ForgeTypeId spec, OpcoesCopia op)
        {
            if (op.Unidade != null)
            {
                try { if (UnitUtils.GetValidUnits(spec).Any(u => u.TypeId == op.Unidade.TypeId)) return op.Unidade; }
                catch { /* spec sem unidades */ }
            }
            return unidades.GetFormatOptions(spec).GetUnitTypeId();
        }

        public static bool TryDouble(string s, out double d)
        {
            d = 0;
            if (string.IsNullOrEmpty(s)) return false;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out d) ||
                   double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
        }

        private static string Numero(double d) => d.ToString("0.######", CultureInfo.CurrentCulture);

        public static string Rotulo(ForgeTypeId unidade)
        {
            try { return LabelUtils.GetLabelForUnit(unidade); } catch { return unidade?.TypeId; }
        }

        // ------------------------------------------------------------------ exibição e comparação

        private static bool Vazio(Parameter p)
        {
            if (!p.HasValue) return true;
            if (p.StorageType == StorageType.String) return string.IsNullOrEmpty(p.AsString());
            if (p.StorageType == StorageType.ElementId) return p.AsElementId() == ElementId.InvalidElementId;
            return false;
        }

        private static string Exibir(Parameter p)
        {
            if (!p.HasValue) return "";
            return p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
        }

        private static string ExibirValor(Units unidades, ParamRef destino, object valor)
        {
            switch (valor)
            {
                case string s: return s;
                case int i: return destino.SimNao ? (i == 1 ? "Sim" : "Não") : i.ToString();
                case double d:
                    if (destino.Spec != null && !string.IsNullOrEmpty(destino.Spec.TypeId))
                    {
                        try { return UnitFormatUtils.Format(unidades, destino.Spec, d, false); } catch { /* sem formato */ }
                    }
                    return Numero(d);
                default: return valor?.ToString();
            }
        }

        private static bool Igual(Parameter p, object valor)
        {
            if (!p.HasValue) return false;
            switch (valor)
            {
                case string s: return p.StorageType == StorageType.String && (p.AsString() ?? "") == s;
                case int i: return p.StorageType == StorageType.Integer && p.AsInteger() == i;
                case double d: return p.StorageType == StorageType.Double && Math.Abs(p.AsDouble() - d) < 1e-9;
                default: return false;
            }
        }

        private static string NomeElemento(Document doc, Element e)
        {
            string tipo = (doc.GetElement(e.GetTypeId()) as ElementType)?.Name;
            string familia = (e as FamilyInstance)?.Symbol?.FamilyName;
            string nome = familia != null ? $"{familia}: {tipo}" : tipo ?? e.Name;
            return string.IsNullOrEmpty(nome) ? e.Name : nome;
        }

        // ------------------------------------------------------------------ gravação

        /// <summary>Grava a análise numa única transação. Retorna (gravados, falhas).</summary>
        public static (int Ok, int Falhas) Executar(Document doc, AnaliseCopia analise, ParamRef destino)
        {
            int ok = 0, falhas = 0;
            using (var t = new Transaction(doc, "MetroSP: Copiar valores entre parâmetros"))
            {
                t.Start();
                foreach (var (donoId, valor) in analise.Gravacoes)
                {
                    Parameter p = destino.Achar(doc.GetElement(donoId));
                    bool gravou = false;
                    try
                    {
                        if (p != null && !p.IsReadOnly)
                            gravou = valor is string s ? p.Set(s) : valor is int i ? p.Set(i) : p.Set((double)valor);
                    }
                    catch { /* elemento em grupo ou bloqueado */ }
                    if (gravou) ok++; else falhas++;
                }
                t.Commit();
            }
            return (ok, falhas);
        }
    }
}
