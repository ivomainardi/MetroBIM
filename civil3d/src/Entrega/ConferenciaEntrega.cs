using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.DatabaseServices;
using MetroSP.Alinhamento;
using MetroSP.Classificacao;
using MetroSP.Config;
using MetroSP.Confronto;
using MetroSP.Escrita;
using MetroSP.PropertySets;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDocument = Autodesk.AutoCAD.ApplicationServices.Document;

namespace MetroSP.Entrega
{
    public enum Sinal { Verde, Amarelo, Vermelho, Cinza }

    /// <summary>O que a pendência pede: cada tipo tem um botão na janela (ver EntregaWindow.Acoes).</summary>
    public enum Acao { Nenhuma, Comando, Exportador, SalvarDesenho, CriarPset, AnexarPset, UsarPerfil, AbrirEscrita, PreencherProjeto }

    public class Pendencia
    {
        public Sinal Sinal { get; set; } = Sinal.Amarelo;
        public string Titulo { get; set; }
        public string Texto { get; set; }
        public Acao Acao { get; set; }
        /// <summary>Dado da ação: comando, nome do property set, handle do alinhamento...</summary>
        public string Alvo { get; set; }
        public string Valor { get; set; }
        public object Dados { get; set; }
        public List<ObjectId> Objetos { get; set; } = new List<ObjectId>();
    }

    public class EtapaEntrega
    {
        public int Numero { get; set; }
        public string Nome { get; set; }
        public string Dica { get; set; }
        public string Resumo { get; set; }
        public List<Pendencia> Pendencias { get; } = new List<Pendencia>();
        public List<Pendencia> Abertas => Pendencias.Where(p => p.Sinal == Sinal.Vermelho || p.Sinal == Sinal.Amarelo)
            .OrderBy(p => p.Sinal == Sinal.Vermelho ? 0 : 1).ThenByDescending(p => p.Objetos.Count).ToList();
        public Sinal Sinal => Pendencias.Any(p => p.Sinal == Sinal.Vermelho) ? Sinal.Vermelho
            : Pendencias.Any(p => p.Sinal == Sinal.Amarelo) ? Sinal.Amarelo : Sinal.Verde;
    }

    public class ResultadoEntrega
    {
        public string Desenho { get; set; }
        public DateTime Quando { get; set; } = DateTime.Now;
        public List<EtapaEntrega> Etapas { get; } = new List<EtapaEntrega>();
        public List<AlignmentCommands.SituacaoPerfil> Perfis { get; set; } = new List<AlignmentCommands.SituacaoPerfil>();
        public int Vermelhas => Etapas.Count(e => e.Numero < UltimaEtapa && e.Sinal == Sinal.Vermelho);
        public int Amarelas => Etapas.Count(e => e.Numero < UltimaEtapa && e.Sinal == Sinal.Amarelo);
        public EtapaEntrega Etapa(int n) => Etapas.FirstOrDefault(e => e.Numero == n);
        public const int UltimaEtapa = 7;
    }

    /// <summary>
    /// Confere o desenho ativo nas 7 etapas da entrega para o Metrô. Só lê: nada é gravado aqui.
    /// Considera só os objetos que vão para o IFC (FiltroIfc).
    /// </summary>
    public static class ConferenciaEntrega
    {
        public static readonly (int Numero, string Nome, string Dica)[] Etapas =
        {
            (1, "Exportador", "IFC 4.3 Extension da Autodesk instalada e configuração IFC (padrão MetroSP ou Forma) em dia."),
            (2, "Property sets", "Property sets que o mapeamento IFC pede: montados no desenho e anexados aos objetos que vão para o IFC."),
            (3, "Classe IFC", "Classe IFC de cada tipo de objeto, dentro do esquema escolhido (IFC 4.3)."),
            (4, "Dados", "Erros de escrita (espaços, acentos, caracteres estranhos) e confronto com a planilha do padrão."),
            (5, "Classificação", "Códigos de classificação dos objetos conferidos com a planilha do padrão."),
            (6, "Alinhamentos", "Perfil de cada alinhamento que vai para o IFC (eixo 3D com as cotas)."),
            (7, "Local e exportar", "IfcProject, IfcSite e instalações (IfcRailway, IfcBuilding), e a exportação."),
        };

        /// <summary>Última conferência de cada desenho, para o Painel MetroSP.</summary>
        static readonly Dictionary<string, ResultadoEntrega> _ultimas = new Dictionary<string, ResultadoEntrega>(StringComparer.OrdinalIgnoreCase);
        public static event Action<ResultadoEntrega> Conferido;

        public static ResultadoEntrega Ultima(AcDocument doc) =>
            doc != null && _ultimas.TryGetValue(doc.Name ?? "", out var r) ? r : null;

        public static ResultadoEntrega Executar(AcDocument doc)
        {
            var r = new ResultadoEntrega { Desenho = Path.GetFileNameWithoutExtension(doc.Name) };
            foreach (var (n, nome, dica) in Etapas) r.Etapas.Add(new EtapaEntrega { Numero = n, Nome = nome, Dica = dica });

            Rodar(r, 1, () => Exportador(doc, r.Etapa(1)));
            Rodar(r, 2, () => PropertySets(doc, r.Etapa(2)));
            Rodar(r, 3, () => ClasseIfc(r.Etapa(3)));
            Rodar(r, 4, () => Dados(doc, r.Etapa(4)));
            Rodar(r, 5, () => Classificacao(doc, r.Etapa(5)));
            Rodar(r, 6, () => Alinhamentos(doc, r));
            Rodar(r, 7, () => Local(r.Etapa(7)));

            _ultimas[doc.Name ?? ""] = r;
            try { Conferido?.Invoke(r); } catch { }
            return r;
        }

        /// <summary>Uma etapa com erro não pode parar a conferência das outras.</summary>
        static void Rodar(ResultadoEntrega r, int n, Action conferir)
        {
            try { conferir(); }
            catch (Exception ex)
            {
                Log.Erro("conferência da etapa " + n, ex);
                r.Etapa(n).Pendencias.Add(new Pendencia { Sinal = Sinal.Amarelo, Titulo = "Não deu para conferir esta etapa", Texto = ex.Message });
            }
        }

        // ------------------------------------------------------------------ 1 Exportador

        static void Exportador(AcDocument doc, EtapaEntrega e)
        {
            var cfg = Settings.Load();
            if (Commands.DrawingFolder(doc) == null && cfg.Destino != "ProgramData")
                e.Pendencias.Add(new Pendencia
                {
                    Sinal = Sinal.Vermelho, Titulo = "Desenho não salvo",
                    Texto = "A configuração IFC é gravada na pasta do DWG.", Acao = Acao.SalvarDesenho,
                });

            bool ok = AutodeskIfcExtension.CommandDefined(cfg.ExportCommand) || AutodeskIfcExtension.FindInstalled().Count > 0;
            if (!ok)
                e.Pendencias.Add(new Pendencia
                {
                    Sinal = Sinal.Vermelho, Titulo = "IFC 4.3 Extension não instalada",
                    Texto = "Sem ela o Civil 3D não exporta IFC 4.3.", Acao = Acao.Exportador,
                });

            ConfigPackage.EnsureActive();
            var presentes = ConfigPackage.List().Select(x => x.Kind).ToHashSet();
            var faltam = new[] { ConfigKind.AutodeskConfig, ConfigKind.ObjectMapping }.Where(k => !presentes.Contains(k)).ToList();
            if (faltam.Count > 0)
                e.Pendencias.Add(new Pendencia
                {
                    Sinal = Sinal.Vermelho, Titulo = "Configuração IFC incompleta",
                    Texto = "Falta " + string.Join(" e ", faltam.Select(k => ConfigPackage.FileNames[k])) + ".",
                    Acao = Acao.Comando, Alvo = "MSP_IFCCONFIG", Valor = "Configurar IFC",
                });

            if (cfg.UsesForma)
            {
                string origem = Path.Combine(ConfigPackage.ActiveDir, "MetroSP-IFC-origem.txt");
                double dias = File.Exists(origem) ? (DateTime.Now - File.GetLastWriteTime(origem)).TotalDays : double.MaxValue;
                if (dias > 7)
                    e.Pendencias.Add(new Pendencia
                    {
                        Sinal = Sinal.Amarelo,
                        Titulo = dias == double.MaxValue ? "Configuração do Forma nunca baixada" : $"Configuração do Forma baixada há {Math.Floor(dias)} dias",
                        Texto = "A exportação baixa de novo, mas vale conferir antes.",
                        Acao = Acao.Comando, Alvo = "MSP_IFCSYNC", Valor = "Sincronizar",
                    });
            }
            e.Resumo = ok ? "Exportador da Autodesk: " + AutodeskIfcExtension.Describe(cfg.ExportCommand) : null;
        }

        // ------------------------------------------------------------------ 2 Property sets

        static void PropertySets(AcDocument doc, EtapaEntrega e)
        {
            var db = doc.Database;
            var esperados = PsetsCore.DoMapeamentoIfc()
                .GroupBy(p => p.Nome, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            if (esperados.Count == 0)
            {
                e.Resumo = "O mapeamento IFC não pede property sets.";
                return;
            }
            var noDesenho = PsetsCore.LerDesenho(db).Select(p => p.Nome).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var p in esperados.Where(p => !noDesenho.Contains(p.Nome)))
                e.Pendencias.Add(new Pendencia
                {
                    Sinal = Sinal.Amarelo, Titulo = p.Nome + " · não está no desenho",
                    Texto = $"O mapeamento IFC pede {p.Propriedades.Count} propriedade(s) dele.",
                    Acao = Acao.CriarPset, Alvo = p.Nome, Dados = p,
                });

            var escolhidos = esperados.Select(p => p.Nome).Where(noDesenho.Contains).ToList();
            if (escolhidos.Count == 0) return;
            var objetos = PsetsCore.Objetos(db, null);
            PsetsCore.CalcularFaltas(db, objetos, escolhidos);
            foreach (string pset in escolhidos)
            {
                var sem = objetos.Where(o => (o.Faltam ?? "").Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries)
                    .Contains(pset, StringComparer.OrdinalIgnoreCase)).ToList();
                if (sem.Count == 0) continue;
                e.Pendencias.Add(new Pendencia
                {
                    Sinal = Sinal.Vermelho, Titulo = $"{pset} · falta em {sem.Count} objeto(s)",
                    Texto = Tipos(sem.Select(o => o.Tipo)),
                    Acao = Acao.AnexarPset, Alvo = pset, Dados = sem,
                    Objetos = sem.Select(o => o.Id).ToList(),
                });
            }
            e.Resumo = $"{objetos.Count} objeto(s) no IFC, {escolhidos.Count} property set(s) conferidos.";
        }

        static string Tipos(IEnumerable<string> tipos)
        {
            var g = tipos.GroupBy(t => t ?? "").OrderByDescending(x => x.Count()).ToList();
            string s = string.Join(", ", g.Take(4).Select(x => $"{x.Key} ({x.Count()})"));
            return g.Count > 4 ? s + $" e mais {g.Count - 4} tipo(s)" : s;
        }

        // ------------------------------------------------------------------ 3 Classe IFC

        static void ClasseIfc(EtapaEntrega e)
        {
            var mapping = ConfigPackage.LoadJson(ConfigKind.ObjectMapping) as JsonObject ?? new JsonObject();
            var config = ConfigPackage.LoadJson(ConfigKind.AutodeskConfig);
            string versao = (string)(config?["Export"]?["IfcVersion"]) ?? "IFC4X3_ADD2";
            var invalidas = Ui.ConfigWindow.ClassesInvalidas(mapping, versao);
            foreach (string linha in invalidas)
            {
                int i = linha.IndexOf(" → ", StringComparison.Ordinal);
                e.Pendencias.Add(new Pendencia
                {
                    Sinal = Sinal.Vermelho,
                    Titulo = i > 0 ? linha.Substring(0, i) : linha,
                    Texto = (i > 0 ? "Classe " + linha.Substring(i + 3) + " " : "Classe ") + "fora do " + Ifc.IfcCatalog.Label(versao) + ".",
                });
            }
            e.Resumo = "Esquema " + Ifc.IfcCatalog.Label(versao) + ".";
        }

        // ------------------------------------------------------------------ 4 Dados

        static void Dados(AcDocument doc, EtapaEntrega e)
        {
            var fonte = new FonteCivil3D(doc, null);
            var padrao = PadroesEscrita.Listar().First();
            var corretor = new CorretorEscrita(padrao);
            var ignorados = new HashSet<string>(padrao.Ignorados ?? new List<string>());
            var vistos = new HashSet<string>();
            var porCampo = new Dictionary<string, int>();
            var objetos = fonte.Objetos();
            foreach (CampoConfronto c in fonte.Campos().Where(c => c.Texto))
                foreach (ObjetoConfronto o in objetos)
                {
                    ValorConfronto v = fonte.Ler(o, c);
                    if (!v.Existe || string.IsNullOrEmpty(v.Texto)) continue;
                    string dono = v.Dono != null ? "t:" + v.Dono : !v.Editavel ? "v:" + v.Texto : "o:" + o.Id;
                    if (!vistos.Add(dono + "|" + c.Chave)) continue;
                    if (ignorados.Contains(dono + "|" + c.Chave + "|" + v.Texto)) continue;
                    if (!corretor.Analisar(v.Texto).TemProblema) continue;
                    porCampo[c.Rotulo] = porCampo.TryGetValue(c.Rotulo, out int n) ? n + 1 : 1;
                }
            foreach (var kv in porCampo.OrderByDescending(x => x.Value))
                e.Pendencias.Add(new Pendencia
                {
                    Sinal = Sinal.Amarelo, Titulo = $"{kv.Key} · {kv.Value} valor(es) com erro de escrita",
                    Texto = "Espaços, acentos ou caracteres estranhos.", Acao = Acao.AbrirEscrita,
                });
            e.Resumo = $"Escrita conferida em {objetos.Count} objeto(s) (padrão \"{padrao.Nome}\").";
        }

        // ------------------------------------------------------------------ 5 Classificação

        static void Classificacao(AcDocument doc, EtapaEntrega e)
        {
            var padroes = PadraoClassificacao.Listar();
            var padrao = padroes.FirstOrDefault(p => p.Nome == VerificarClassificacaoWindow._ultimoPadrao) ?? padroes.FirstOrDefault();
            if (padrao == null)
            {
                e.Pendencias.Add(new Pendencia { Sinal = Sinal.Amarelo, Titulo = "Nenhum padrão de classificação", Texto = "Escolha a planilha de classificação do Metrô na ferramenta ao lado." });
                return;
            }
            var cache = new Dictionary<string, Planilha.Tabela>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in padrao.Sistemas) s.Carregar(cache);
            var sistemas = padrao.Sistemas.Where(s => s.Lista != null).ToList();
            foreach (var s in padrao.Sistemas.Where(s => s.Lista == null))
                e.Pendencias.Add(new Pendencia { Sinal = Sinal.Amarelo, Titulo = s.Nome + " · " + (s.Erro ?? "sem planilha"), Texto = "Indique a planilha no padrão \"" + padrao.Nome + "\"." });
            if (sistemas.Count == 0) return;

            var db = doc.Database;
            var linhas = new List<LinhaVerificacao>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ids = Ifc.FiltroIfc.Objetos(db, tr);
                foreach (var s in sistemas) linhas.AddRange(ClassificacaoCore.Verificar(db, tr, ids, s));
                tr.Commit();
            }
            foreach (var g in linhas.Where(l => l.Situacao != ClassificacaoCore.OK).GroupBy(l => l.Situacao)
                         .OrderBy(g => Array.IndexOf(ClassificacaoCore.Ordem, g.Key)))
                e.Pendencias.Add(new Pendencia
                {
                    Sinal = g.Key == ClassificacaoCore.DescricaoDiferente ? Sinal.Amarelo : Sinal.Vermelho,
                    Titulo = $"{g.Key} · {g.Count()} objeto(s)",
                    Texto = Tipos(g.Select(l => l.Tipo)),
                    Objetos = g.Select(l => l.Id).Distinct().ToList(),
                });
            e.Resumo = $"Padrão \"{padrao.Nome}\": {linhas.Count} verificação(ões).";
        }

        // ------------------------------------------------------------------ 6 Alinhamentos

        static void Alinhamentos(AcDocument doc, ResultadoEntrega r)
        {
            var e = r.Etapa(6);
            var metro = ConfigPackage.LoadMetroSp();
            r.Perfis = AlignmentCommands.Situacao(doc, metro.Alinhamento);
            if (r.Perfis.Count == 0) { e.Resumo = "Desenho sem alinhamento."; return; }
            if (!metro.Alinhamento.AplicarNoIfc)
                e.Pendencias.Add(new Pendencia
                {
                    Sinal = Sinal.Amarelo, Titulo = "Eixo 3D desligado",
                    Texto = "O IFC sai só com o alinhamento que a Autodesk grava.", Acao = Acao.Comando, Alvo = "MSP_IFCCONFIG", Valor = "Configurar IFC",
                });
            foreach (var s in r.Perfis.Where(p => p.Origem == "Sugerido"))
                e.Pendencias.Add(new Pendencia
                {
                    Sinal = Sinal.Amarelo, Titulo = $"{s.Alinhamento} · {s.Perfis.Count} perfis",
                    Texto = $"Sugerido: {s.Escolhido}. A exportação vai perguntar.",
                    Acao = Acao.UsarPerfil, Alvo = s.Handle, Valor = s.Escolhido, Objetos = { s.Id },
                });
            foreach (var s in r.Perfis.Where(p => p.Origem == "Nenhum"))
                e.Pendencias.Add(new Pendencia
                {
                    Sinal = Sinal.Amarelo, Titulo = s.Alinhamento + " · sem perfil de projeto",
                    Texto = "Vai só a planta, sem as cotas.", Objetos = { s.Id },
                });
            e.Resumo = $"{r.Perfis.Count} alinhamento(s).";
        }

        // ------------------------------------------------------------------ 7 Local e exportar

        static void Local(EtapaEntrega e)
        {
            var est = ConfigPackage.LoadMetroSp().Estrutura;
            if (string.IsNullOrWhiteSpace(est.Projeto?.Nome))
                e.Pendencias.Add(new Pendencia { Sinal = Sinal.Vermelho, Titulo = "IfcProject sem nome", Texto = "O nome do projeto é obrigatório.", Acao = Acao.PreencherProjeto });
            if (string.IsNullOrWhiteSpace(est.Terreno?.Nome))
                e.Pendencias.Add(new Pendencia { Sinal = Sinal.Vermelho, Titulo = "IfcSite sem nome", Texto = "O nome do terreno é obrigatório.", Acao = Acao.PreencherProjeto });
            if (!est.AllFacilities().Any(f => !string.IsNullOrWhiteSpace(f.Nome)))
                e.Pendencias.Add(new Pendencia { Sinal = Sinal.Amarelo, Titulo = "Nenhuma instalação", Texto = "Inclua a linha (IfcRailway) e as estações (IfcBuilding).", Acao = Acao.PreencherProjeto });
            e.Resumo = string.IsNullOrWhiteSpace(est.Projeto?.Nome) ? null : $"{est.Projeto.Nome} · {est.Terreno?.Nome}";
        }
    }
}
