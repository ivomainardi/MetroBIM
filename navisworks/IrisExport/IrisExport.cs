// =====================================================================
//  Iris · plugin do Navisworks: "Exportar para o Iris"
//  Grava as tarefas do TimeLiner (estrutura, datas previstas e reais, %,
//  campos User 1…10) com os objetos de cada tarefa pelo GlobalId do IFC,
//  num arquivo .iris4d.json que o Iris lê em Fiscalização › Cronograma ›
//  Importar › Navisworks. Só lê o documento: não muda nada no Navisworks.
// =====================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using Autodesk.Navisworks.Api.Timeliner;
using NwApp = Autodesk.Navisworks.Api.Application;

namespace IrisExport
{
    [Plugin("IrisExport", "IRIS", DisplayName = "Exportar para o Iris", ToolTip = "Exporta as tarefas do TimeLiner com os objetos de cada uma (GlobalId do IFC) para o cronograma do Iris")]
    // ícones do botão: os PNG ficam na pasta da DLL (o instalar.ps1 copia junto)
    [AddInPlugin(AddInLocation.AddIn, Icon = "iris_16.png", LargeIcon = "iris_32.png")]
    public class ExportarParaIris : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            var doc = NwApp.ActiveDocument;
            if (doc == null || doc.IsClear) { MessageBox.Show("Abra o modelo e o cronograma do TimeLiner antes de exportar.", "Iris"); return 0; }
            var tl = doc.GetTimeliner();
            if (tl == null || tl.Tasks.Count == 0) { MessageBox.Show("O TimeLiner deste arquivo não tem tarefas.", "Iris"); return 0; }
            var nome = Path.GetFileNameWithoutExtension(doc.FileName ?? "");
            using (var dlg = new SaveFileDialog { Title = "Exportar para o Iris", Filter = "Cronograma do Iris (*.iris4d.json)|*.iris4d.json", FileName = (nome.Length > 0 ? nome : "cronograma") + ".iris4d.json" })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return 0;
                System.Windows.Forms.Cursor.Current = Cursors.WaitCursor;
                Exportador.Resultado r = null; Exception falha = null;
                // registro tarefa a tarefa (com o tempo de cada uma) ao lado do arquivo: se o Navisworks fechar no meio,
                // a última linha mostra em qual tarefa parou
                var registro = Path.Combine(Path.GetDirectoryName(dlg.FileName), Path.GetFileName(dlg.FileName).Replace(".iris4d.json", "") + ".iris4d.log");
                var prog = NwApp.BeginProgress("Exportar para o Iris", "Lendo as tarefas e os objetos do TimeLiner…");
                var relogio = System.Diagnostics.Stopwatch.StartNew();
                // grava num .parcial e só troca pelo arquivo escolhido no fim: cancelado ou com erro, não fica arquivo pela metade
                var parcial = dlg.FileName + ".parcial";
                using (var log = new StreamWriter(registro, false, new UTF8Encoding(false)) { AutoFlush = true })
                {
                    try
                    {
                        using (var saida = new StreamWriter(parcial, false, new UTF8Encoding(false), 1 << 16))
                            r = new Exportador(doc, f => prog.Update(f), log).Exportar(tl, saida);
                        if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName);
                        File.Move(parcial, dlg.FileName);
                    }
                    catch (OperationCanceledException) { r = null; }
                    catch (Exception e) { r = null; falha = e; log.WriteLine("ERRO: " + e); }
                    finally
                    {
                        NwApp.EndProgress(); System.Windows.Forms.Cursor.Current = Cursors.Default;
                        try { if (File.Exists(parcial)) File.Delete(parcial); } catch { }
                    }
                }
                if (falha != null) { MessageBox.Show("A exportação parou com um erro:\n" + falha.Message + "\n\nDetalhes em " + registro, "Iris"); return 0; }
                if (r == null) { MessageBox.Show("Exportação cancelada. Nenhum arquivo foi gravado.", "Iris"); return 0; }
                var msg = $"{r.Tarefas} tarefas exportadas · {r.ComObjetos} com objetos ({r.Objetos} GlobalId) · {relogio.Elapsed.TotalSeconds:0} s.";
                if (r.Erros > 0) msg += $"\n\n{r.Erros} tarefa(s) saíram sem objetos porque o Navisworks deu erro ao ler a seleção. Detalhes em {registro}";
                msg += $"\n\nTempo de cada tarefa em {registro}";
                if (r.SemGlobalId > 0) msg += $"\n\n{r.SemGlobalId} objeto(s) ligados às tarefas não têm GlobalId do IFC. Para o Iris achar os objetos, carregue no Navisworks o mesmo IFC que você abre no Iris.";
                MessageBox.Show(msg, "Iris");
            }
            return 0;
        }
    }

    public class Exportador
    {
        public class Resultado { public int Tarefas, ComObjetos, Objetos, SemGlobalId, Erros; }

        readonly Document doc;
        readonly Func<double, bool> avancar;     // barra de progresso do Navisworks; false = o usuário cancelou
        // onde o GlobalId foi achado (categoria, propriedade): achado no primeiro objeto, os outros só leem essa propriedade
        readonly List<KeyValuePair<string, string>> ondeGid = new List<KeyValuePair<string, string>>();
        int varridos, total, feitas, semGid;
        long msSelecao, msIds;
        // tempo (em ticks do Stopwatch) de cada parte de IdsDaSelecao, para o .log: ler o GlobalId, olhar o que há dentro, buscar nos grupos
        long tkCopia, tkLer, tkDentro, tkBusca; int nItens;
        string diagnostico;      // propriedades do primeiro objeto sem GlobalId (para saber onde o GlobalId foi parar)
        static readonly Regex IFC_ID = new Regex(@"^[0-9A-Za-z_$]{22}$");
        static readonly Regex NOME_GID = new Regex(@"^(ifc ?)?(global ?id|guid)$|^ifc ?guid$|^guid ?ifc$", RegexOptions.IgnoreCase);

        readonly TextWriter log;
        public Exportador(Document doc, Func<double, bool> avancar = null, TextWriter log = null) { this.doc = doc; this.avancar = avancar; this.log = log; }

        static int Conta(IEnumerable<TimelinerTask> ts) => ts.Sum(t => 1 + Conta(t.Children.OfType<TimelinerTask>()));

        // o JSON vai direto para o arquivo, tarefa a tarefa (sem montar tudo na memória antes)
        public Resultado Exportar(DocumentTimeliner tl, TextWriter saida)
        {
            var r = new Resultado(); var j = new Json(saida);
            total = Math.Max(1, Conta(tl.Tasks.OfType<TimelinerTask>()));
            j.Abre('{');
            j.Campo("formato", "iris-4d"); j.Campo("versao", 1); j.Campo("origem", "Navisworks");
            j.Campo("programa", "Navisworks " + typeof(NwApp).Assembly.GetName().Version);
            j.Campo("documento", doc.FileName ?? ""); j.Campo("exportadoEm", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
            j.Campo("dataStatus", (string)null);      // o TimeLiner não tem data de status: o Iris usa a data da importação
            j.Nome("modelos"); j.Abre('[');
            foreach (Model m in doc.Models) j.Valor(string.IsNullOrEmpty(m.SourceFileName) ? m.FileName : m.SourceFileName);
            j.Fecha(']');
            j.Nome("tarefas"); j.Abre('[');
            foreach (var t in tl.Tasks.OfType<TimelinerTask>()) Tarefa(j, t, 0, r);
            j.Fecha(']');
            j.Campo("semGlobalId", semGid);
            if (diagnostico != null) j.Campo("diagnostico", diagnostico);
            j.Fecha('}');
            r.SemGlobalId = semGid;
            log?.WriteLine("classes com objetos com GlobalId dentro: " + string.Join(", ", classes.Where(c => c.Value[1] > 0).Select(c => $"{c.Key} ({c.Value[1]} de {c.Value[0]})")));
            log?.WriteLine($"total: seleções do Navisworks {msSelecao / 1000.0:0.0} s · GlobalIds {msIds / 1000.0:0.0} s");
            return r;
        }

        void Tarefa(Json j, TimelinerTask t, int nivel, Resultado r)
        {
            if (avancar != null && !avancar(Math.Min(1, (double)++feitas / total))) throw new OperationCanceledException();
            var filhas = t.Children.OfType<TimelinerTask>().ToList();
            // a raiz da fonte de dados ("New Data Source (Root)") não vira tarefa: as de dentro sobem um nível
            if (t.IsDataSourceRootTask) { foreach (var f in filhas) Tarefa(j, f, nivel, r); return; }
            j.Abre('{');
            j.Campo("id", !string.IsNullOrEmpty(t.SynchronizationId) ? t.SynchronizationId : t.Guid.ToString());
            j.Campo("codigo", t.DisplayId ?? ""); j.Campo("nome", t.DisplayName ?? ""); j.Campo("nivel", nivel); j.Campo("resumo", filhas.Count > 0);
            j.Campo("tipo", t.SimulationTaskTypeName ?? "");
            j.Campo("inicio", Data(t.PlannedStartDate)); j.Campo("fim", Data(t.PlannedEndDate));
            j.Campo("inicioReal", Data(t.ActualStartDate)); j.Campo("fimReal", Data(t.ActualEndDate));
            j.Campo("avanco", Avanco(t));
            j.Nome("campos"); j.Abre('{');
            var user = new[] { t.User1, t.User2, t.User3, t.User4, t.User5, t.User6, t.User7, t.User8, t.User9, t.User10 };
            for (int i = 0; i < user.Length; i++) if (!string.IsNullOrWhiteSpace(user[i])) j.Campo("User " + (i + 1), user[i].Trim());
            j.Fecha('}');
            var sel = t.Selection; var gids = new List<string>();
            var tipoSel = sel == null || sel.IsClear ? null : sel.HasExplicitSelection ? "explicita" : sel.HasSelectionSources ? "conjuntos" : sel.HasSearch ? "busca" : "";
            if (tipoSel != null) j.Campo("selecao", tipoSel);
            if (tipoSel != null)
            {
                log?.WriteLine($"{feitas}/{total} [{t.DisplayId}] {t.DisplayName} ({tipoSel})");
                var relogio = System.Diagnostics.Stopwatch.StartNew(); long msSel = 0;
                try
                {
                    var itens = sel.GetSelectedItems(doc); msSel = relogio.ElapsedMilliseconds;
                    gids.AddRange(IdsDaSelecao(itens));
                }
                catch (Exception e)
                {
                    gids.Clear(); r.Erros++;
                    j.Campo("erro", e.Message);
                    log?.WriteLine("  erro: " + e.Message);
                }
                // tempo do Navisworks resolvendo a seleção (conjuntos, buscas) e o do plugin juntando os GlobalId
                log?.WriteLine($"  {relogio.ElapsedMilliseconds} ms (seleção {msSel} ms · {nItens} escolhidos: copiar {Ms(tkCopia)} ms, ler {Ms(tkLer)} ms, dentro {Ms(tkDentro)} ms, busca {Ms(tkBusca)} ms) · {gids.Count} objetos");
                msSelecao += msSel; msIds += relogio.ElapsedMilliseconds - msSel;
            }
            j.Nome("globalIds"); j.Abre('['); foreach (var g in gids) j.Valor(g); j.Fecha(']');
            j.Fecha('}');
            r.Tarefas++; if (gids.Count > 0) { r.ComObjetos++; r.Objetos += gids.Count; }
            foreach (var f in filhas) Tarefa(j, f, nivel + 1, r);
        }

        static long Ms(long ticks) => ticks * 1000 / System.Diagnostics.Stopwatch.Frequency;

        static string Data(DateTime? d) => d.HasValue ? d.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;

        static double Avanco(TimelinerTask t)
        {
            try { var p = t.ProgressPercent; return p.HasValue ? Math.Round(Math.Max(0, Math.Min(100, p.Value)), 2) : 0; }
            catch { return 0; }
        }

        // GlobalId do objeto escolhido; se ele é um grupo (arquivo, pavimento, camada), os dos objetos dentro dele;
        // se é só a geometria de um objeto, o do objeto acima.
        // Nada aqui usa ModelItem como chave de Dictionary/HashSet: no Navisworks 2027 cada consulta assim levava
        // ~5 ms com 246 mil objetos (2 h só para montar os grupos). Ler a propriedade direto leva ~0,3 ms
        List<string> IdsDaSelecao(ModelItemCollection itens)
        {
            var ids = new List<string>(); var vistos = new HashSet<string>(); ModelItemCollection grupos = null;
            void Junta(string g) { if (g != null && vistos.Add(g)) ids.Add(g); }
            tkLer = tkDentro = tkBusca = 0; nItens = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // copia para um vetor de uma vez: o foreach na coleção do Navisworks custava ~1,5 ms por objeto
            var lista = new ModelItem[itens.Count]; itens.CopyTo(lista, 0);
            tkCopia = sw.ElapsedTicks;
            foreach (var it in lista)
            {
                nItens++;
                long t0 = sw.ElapsedTicks;
                var g = Gid(it);
                long t1 = sw.ElapsedTicks; tkLer += t1 - t0;
                if (amostras < 5 && g != null) Amostra(it, g);
                if (g != null)
                {
                    Junta(g);
                    // pavimento, edifício, conjunto com peças (têm GlobalId e objetos com GlobalId dentro): junta os de dentro
                    // também, percorrendo aqui mesmo. Um objeto comum só tem a geometria dele dentro, sem GlobalId
                    if (VerDentro(it, out var classe))
                    {
                        var tem = PrimeiroFilho(it) is ModelItem f && Gid(f) != null;
                        classe[0]++; if (tem) classe[1]++;
                        int lidos = 0;
                        if (tem) foreach (ModelItem d in it.Descendants) { if (++lidos % 2000 == 0) Respira(); Junta(Gid(d)); }
                    }
                    tkDentro += sw.ElapsedTicks - t1;
                    continue;
                }
                var temDentro = PrimeiroFilho(it) != null;
                tkDentro += sw.ElapsedTicks - t1;
                if (temDentro) { (grupos ?? (grupos = new ModelItemCollection())).Add(it); continue; }
                // geometria solta: o primeiro objeto acima com GlobalId (poucos níveis)
                string ga = null; int n = 0;
                for (var a = it.Parent; a != null && ga == null && n < 6; a = a.Parent, n++) ga = Gid(a);
                if (ga != null) Junta(ga); else SemGid(it);
            }
            if (grupos != null)
            {
                long t2 = sw.ElapsedTicks;
                if (ondeGid.Count == 0) Descobre(grupos);
                // muitos grupos pequenos (objetos sem GlobalId próprio, com as peças dentro): percorre aqui mesmo.
                // Poucos grupos grandes (arquivo, camada): a busca do Navisworks
                if (grupos.Count > 20)
                {
                    int lidos = 0;
                    foreach (ModelItem gr in grupos)
                        foreach (ModelItem d in gr.Descendants) { if (++lidos % 2000 == 0) Respira(); Junta(Gid(d)); }
                }
                // uma busca do Navisworks dentro dos grupos acha os objetos que têm a propriedade do GlobalId
                else foreach (var onde in ondeGid)
                {
                    var busca = new Search();
                    busca.Selection.CopyFrom(grupos);
                    busca.Locations = SearchLocations.DescendantsAndSelf; busca.PruneBelowMatch = false;   // sem isso, achado um pavimento, não desce aos objetos dele
                    busca.SearchConditions.Add(SearchCondition.HasPropertyByDisplayName(onde.Key, onde.Value));
                    int lidos = 0;
                    foreach (ModelItem d in busca.FindAll(doc, false)) { if (++lidos % 2000 == 0) Respira(); Junta(Ler(d, onde)); }
                }
                tkBusca += sw.ElapsedTicks - t2;
            }
            return ids;
        }

        // para o .log: se o GUID da instância do Navisworks, comprimido, dá o GlobalId (aí não precisaria ler a propriedade)
        int amostras;
        void Amostra(ModelItem it, string g)
        {
            amostras++;
            try
            {
                var ig = it.InstanceGuid; var b = ig.ToByteArray();
                var invertido = new Guid(new byte[] { b[3], b[2], b[1], b[0], b[5], b[4], b[7], b[6], b[8], b[9], b[10], b[11], b[12], b[13], b[14], b[15] });
                log?.WriteLine($"  amostra: GlobalId {g} · GUID da instância {ig} → {Comprime(ig)} / {Comprime(invertido)} · classe {it.ClassName} / {it.ClassDisplayName} · coleção {it.IsCollection} · composto {it.IsComposite} · camada {it.IsLayer}");
            }
            catch (Exception e) { log?.WriteLine("  amostra: " + e.Message); }
        }

        // GUID (128 bits) → GlobalId do IFC: 22 caracteres de 6 bits (o primeiro com 2) no alfabeto do IFC
        const string ALFABETO = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";
        public static string Comprime(Guid guid)
        {
            var h = guid.ToString("N");
            ulong alto = Convert.ToUInt64(h.Substring(0, 16), 16), baixo = Convert.ToUInt64(h.Substring(16), 16);
            var c = new char[22];
            for (int i = 21; i >= 0; i--) { c[i] = ALFABETO[(int)(baixo & 63)]; baixo = (baixo >> 6) | (alto << 58); alto >>= 6; }
            return new string(c);
        }

        // Abrir os filhos custa ~3 ms por objeto, e quase nenhum objeto tem outro com GlobalId dentro. Mas há classes
        // que têm (cobertura com placas, janela, escada, telhado, parede com partes): aprende pela classe (ClassDisplayName).
        // Cada classe é aberta nos 30 primeiros objetos; se nenhum tinha nada com GlobalId dentro, as outras dela não são
        // mais abertas. Estrutura espacial e conjuntos são sempre abertos
        static readonly Regex CLASSE_COM_DENTRO = new Regex(@"^Ifc(Project|Site|Building|BuildingStorey|Space|SpatialZone|ExternalSpatialElement|Facility|FacilityPart\w*|Bridge|BridgePart|Road|RoadPart|Railway|RailwayPart|MarineFacility|MarinePart|Tunnel|TunnelPart|ElementAssembly)$", RegexOptions.IgnoreCase);
        readonly Dictionary<string, int[]> classes = new Dictionary<string, int[]>();   // classe → { abertos, com GlobalId dentro }
        bool VerDentro(ModelItem it, out int[] classe)
        {
            var c = it.ClassDisplayName ?? "";
            if (!classes.TryGetValue(c, out classe)) classes[c] = classe = new int[2];
            return classe[0] < 30 || classe[1] > 0 || CLASSE_COM_DENTRO.IsMatch(c);
        }

        // Children.First é uma chamada só (Children.Any() do LINQ monta o enumerador)
        static ModelItem PrimeiroFilho(ModelItem it) { try { return it.Children.First; } catch { return null; } }


        void SemGid(ModelItem it) { semGid++; if (diagnostico == null) diagnostico = Descreve(it); }

        // GlobalId do próprio objeto, lendo só a propriedade já achada; antes de achar, varre as propriedades dele
        // (com limite, para um modelo sem GlobalId não varrer objeto por objeto)
        string Gid(ModelItem it)
        {
            foreach (var onde in ondeGid) { var g = Ler(it, onde); if (g != null) return g; }
            if (ondeGid.Count > 0 || varridos >= 5000) return null;
            varridos++;
            return Varre(it);
        }

        static string Ler(ModelItem it, KeyValuePair<string, string> onde)
        {
            var v = Texto(it.PropertyCategories.FindPropertyByDisplayName(onde.Key, onde.Value)?.Value);
            return v != null && IFC_ID.IsMatch(v) ? v : null;
        }

        // procura nas propriedades do objeto uma chamada GlobalId/IfcGUID/GUID com valor de 22 caracteres e guarda onde achou
        string Varre(ModelItem it)
        {
            foreach (var cat in it.PropertyCategories)
                foreach (var p in cat.Properties)
                {
                    if (!NOME_GID.IsMatch((p.DisplayName ?? "").Trim()) && !NOME_GID.IsMatch((p.Name ?? "").Trim())) continue;
                    var v = Texto(p.Value);
                    if (v == null || !IFC_ID.IsMatch(v)) continue;
                    var o = new KeyValuePair<string, string>(cat.DisplayName, p.DisplayName);
                    if (!ondeGid.Contains(o)) { ondeGid.Add(o); log?.WriteLine($"GlobalId em \"{o.Key} › {o.Value}\""); }
                    return v;
                }
            return null;
        }

        // grupo escolhido antes de saber onde está o GlobalId: varre os de dentro até achar o primeiro
        void Descobre(ModelItemCollection grupos)
        {
            foreach (ModelItem gr in grupos)
                foreach (ModelItem d in gr.Descendants)
                {
                    if (ondeGid.Count > 0 || varridos >= 5000) return;
                    varridos++; Varre(d);
                }
        }

        // nas etapas longas, atualiza a barra de progresso para o Navisworks não parecer travado e atender o cancelar
        void Respira()
        {
            if (avancar != null && !avancar(Math.Min(1, (double)feitas / total))) throw new OperationCanceledException();
        }

        static string Texto(VariantData v)
        {
            if (v == null) return null;
            try
            {
                if (v.IsDisplayString) return v.ToDisplayString().Trim();
                if (v.IsIdentifierString) return v.ToIdentifierString().Trim();
                var s = v.ToString(); var i = s.IndexOf(':');
                return (i >= 0 ? s.Substring(i + 1) : s).Trim();
            }
            catch { return null; }
        }

        static string Descreve(ModelItem it)
        {
            var sb = new StringBuilder(it.DisplayName + " (" + it.ClassDisplayName + "): ");
            foreach (var cat in it.PropertyCategories.Take(12))
                sb.Append(cat.DisplayName).Append(" [").Append(string.Join(", ", cat.Properties.Take(12).Select(p => p.DisplayName))).Append("] ");
            return sb.ToString().Trim();
        }
    }

    // JSON mínimo (sem dependências), escrito direto no arquivo: textos escapados, números com ponto
    class Json
    {
        readonly TextWriter w;
        public Json(TextWriter saida) { w = saida; }
        bool primeiro = true;
        void Virgula() { if (!primeiro) w.Write(','); primeiro = false; }
        public void Abre(char c) { Virgula(); w.Write(c); primeiro = true; }
        public void Fecha(char c) { w.Write(c); primeiro = false; }
        public void Nome(string n) { Virgula(); Texto(n); w.Write(':'); primeiro = true; }
        public void Campo(string n, string v) { Nome(n); Valor(v); }
        public void Campo(string n, double v) { Nome(n); Valor(v); }
        public void Campo(string n, bool v) { Nome(n); Virgula(); w.Write(v ? "true" : "false"); }
        public void Valor(string v) { Virgula(); if (v == null) w.Write("null"); else Texto(v); }
        public void Valor(double v) { Virgula(); w.Write(v.ToString("0.##", CultureInfo.InvariantCulture)); }
        void Texto(string s)
        {
            w.Write('"');
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '"': w.Write("\\\""); break;
                    case '\\': w.Write("\\\\"); break;
                    case '\n': w.Write("\\n"); break;
                    case '\r': w.Write("\\r"); break;
                    case '\t': w.Write("\\t"); break;
                    default: if (ch < ' ') w.Write("\\u" + ((int)ch).ToString("x4")); else w.Write(ch); break;
                }
            }
            w.Write('"');
        }
    }
}
