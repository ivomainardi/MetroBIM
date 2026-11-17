using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.DatabaseServices;
using MetroSP.Classificacao;
using MetroSP.Config;
using MetroSP.Ifc;
using MetroSP.PropertySets;
using AcDocument = Autodesk.AutoCAD.ApplicationServices.Document;

namespace MetroSP.MetroData
{
    /// <summary>
    /// Escolhas do MetroDATA deste modelo (planilha, disciplina, etapa, classe e o objeto de cada layer ou bloco).
    /// Ficam gravadas no DWG (Propriedades do desenho, "MetroSP.MetroData"): quem abrir o modelo vê as mesmas.
    /// </summary>
    public class EscolhasMetroData
    {
        public const string Propriedade = "MetroSP.MetroData";

        public string Arquivo { get; set; }
        public string Disciplina { get; set; }
        public string Etapa { get; set; }
        /// <summary>Confere até esta classe (1 a 4); 0 = todas.</summary>
        public int Classe { get; set; }
        /// <summary>"Layer X" ou "Bloco Y" → objeto do MetroDATA (quando a classe IFC não basta).</summary>
        public Dictionary<string, string> Objetos { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static EscolhasMetroData Ler(AcDocument doc)
        {
            try
            {
                var e = doc.Database.SummaryInfo.CustomProperties;
                while (e.MoveNext())
                    if (string.Equals(e.Key as string, Propriedade, StringComparison.OrdinalIgnoreCase) && e.Value is string json && json.Trim().Length > 0)
                    {
                        var r = JsonSerializer.Deserialize<EscolhasMetroData>(json) ?? new EscolhasMetroData();
                        r.Objetos = new Dictionary<string, string>(r.Objetos ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                        return r;
                    }
            }
            catch { }
            return new EscolhasMetroData();
        }

        public void Gravar(AcDocument doc)
        {
            using (doc.LockDocument())
            {
                var db = doc.Database;
                var b = new DatabaseSummaryInfoBuilder(db.SummaryInfo);
                b.CustomPropertyTable[Propriedade] = JsonSerializer.Serialize(this);
                db.SummaryInfo = b.ToDatabaseSummaryInfo();
            }
        }
    }

    /// <summary>Um layer (ou bloco) com objetos que vão para o IFC, e o objeto do MetroDATA ligado a ele.</summary>
    public class GrupoModelo
    {
        public string Chave { get; set; }
        public string Tipo { get; set; }
        public string Nome { get; set; }
        public string ClasseIfc { get; set; }
        public List<ObjectId> Ids { get; } = new List<ObjectId>();
        /// <summary>Objeto do MetroDATA (escolhido ou achado pela classe IFC); null = sem objeto.</summary>
        public string Objeto { get; set; }
        public bool Escolhido { get; set; }
        public List<string> Candidatos { get; set; } = new List<string>();
    }

    /// <summary>Uma propriedade que o MetroDATA pede para um objeto, com quantos objetos do modelo têm ela preenchida.</summary>
    public class LinhaMetroData
    {
        public string Objeto { get; set; }
        public string Campo { get; set; }
        public int Classe { get; set; }
        public int Objetos { get; set; }
        public int? Preenchidos { get; set; }
        public int? Vazios { get; set; }
        /// <summary>Texto curto quando não dá para contar ("não sai no IFC", "calculado pelo exportador"...).</summary>
        public string Situacao { get; set; }
        public bool Pendente { get; set; }
        /// <summary>De onde o valor vem no modelo (property set), quando é property set.</summary>
        public string PsetModelo { get; set; }
        public string PropModelo { get; set; }
        public string PsetIfc { get; set; }
        public string PropIfc { get; set; }
        public string Layers { get; set; }
        public List<ObjectId> Ids { get; } = new List<ObjectId>();
        public List<ObjectId> IdsVazios { get; } = new List<ObjectId>();
        public bool SemObjeto { get; set; }
        public bool NaoSaiNoIfc { get; set; }

        public string ClasseTexto => Classe > 0 ? Classe.ToString() : "";
        public string ObjetosTexto => Objetos.ToString("N0");
        public string PreenchidosTexto => Preenchidos?.ToString("N0") ?? (Situacao ?? "—");
        public string VaziosTexto => Vazios?.ToString("N0") ?? "";
        public string ObjetoTexto => SemObjeto ? "sem objeto · " + Layers : Objeto;
        public string Dica =>
            SemObjeto ? $"Os objetos de {Layers} não têm objeto do MetroDATA: escolha no botão Objetos."
            : NaoSaiNoIfc ? $"{Campo} não está na Saída no IFC do perfil (passo 3): mapeie de qual propriedade do modelo o valor vem."
            : PsetModelo != null ? $"Lido de {PsetModelo} › {PropModelo}. Layers: {Layers}."
            : (Situacao ?? "") + (Layers != null ? $" Layers: {Layers}." : "");
    }

    /// <summary>Confere o modelo com a planilha do MetroDATA (passo 4 da Entrega).</summary>
    public static class ConferenciaMetroData
    {
        /// <summary>Layers e blocos com objetos que vão para o IFC, com a classe IFC do perfil e o objeto do MetroDATA.</summary>
        public static List<GrupoModelo> Grupos(Database db, Transaction tr, PlanilhaMetroData md, EscolhasMetroData esc)
        {
            JsonObject mapa = null;
            try { mapa = ConfigPackage.LoadMapping(); } catch { }
            List<JsonObject> Regras(string secao) => (mapa?[secao] as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();
            var layers = Regras("MapCADLayerName");
            var blocos = Regras("MapBlockName");
            string Classe(List<JsonObject> regras, string nome)
            {
                var r = regras.FirstOrDefault(x => Ui.ClassesIfcWindow.Casa((string)x["Name"], nome));
                return r == null ? null : ((string)r["IfcExportAs"] ?? "").Trim();
            }

            var grupos = new Dictionary<string, GrupoModelo>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId id in FiltroIfc.Objetos(db, tr))
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                string tipo, nome, classe;
                if (e is BlockReference br)
                {
                    nome = FiltroIfc.NomeBloco(br, tr);
                    classe = Classe(blocos, nome);
                    tipo = "Bloco";
                    if (string.IsNullOrEmpty(classe)) { tipo = "Layer"; nome = e.Layer; classe = Classe(layers, nome); }
                }
                else { tipo = "Layer"; nome = e.Layer; classe = Classe(layers, nome); }
                string chave = tipo + " " + nome;
                if (!grupos.TryGetValue(chave, out var g))
                    grupos[chave] = g = new GrupoModelo { Chave = chave, Tipo = tipo, Nome = nome, ClasseIfc = classe };
                g.Ids.Add(id);
            }
            foreach (var g in grupos.Values)
            {
                g.Candidatos = md == null ? new List<string>() : md.ObjetosDaClasse(esc.Disciplina, g.ClasseIfc);
                if (esc.Objetos.TryGetValue(g.Chave, out string o) && !string.IsNullOrWhiteSpace(o)) { g.Objeto = o; g.Escolhido = true; }
                else g.Objeto = g.Candidatos.Count == 1 ? g.Candidatos[0] : null;
            }
            return grupos.Values.OrderBy(g => g.Chave, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static List<LinhaMetroData> Analisar(AcDocument doc, PlanilhaMetroData md, EscolhasMetroData esc, out List<GrupoModelo> grupos)
        {
            var db = doc.Database;
            var saida = SaidaIfcPainel.LerMapeamento();
            var linhas = new List<LinhaMetroData>();
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                grupos = Grupos(db, tr, md, esc);
                var porChave = new Dictionary<string, LinhaMetroData>(StringComparer.OrdinalIgnoreCase);
                var layersDe = new Dictionary<LinhaMetroData, HashSet<string>>();
                foreach (GrupoModelo g in grupos)
                {
                    if (g.Objeto == null)
                    {
                        var s = new LinhaMetroData { SemObjeto = true, Objetos = g.Ids.Count, Layers = g.Tipo.ToLowerInvariant() + " " + g.Nome, Pendente = true, Campo = "—" };
                        s.Ids.AddRange(g.Ids);
                        linhas.Add(s);
                        continue;
                    }
                    foreach (PlanilhaMetroData.Linha x in md.Exigidas(esc.Disciplina, g.Objeto, esc.Etapa, esc.Classe))
                    {
                        string chave = g.Objeto + "\u0001" + x.Campo;
                        if (!porChave.TryGetValue(chave, out var l))
                        {
                            porChave[chave] = l = new LinhaMetroData { Objeto = g.Objeto, Campo = x.Campo, Classe = x.Classe, PsetIfc = x.Pset, PropIfc = x.NomeIfc };
                            layersDe[l] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            var m = saida.FirstOrDefault(r => Igual(r.Pset, x.Pset) && Igual(r.Prop, x.NomeIfc));
                            if (x.DoExportador) l.Situacao = "calculado pelo exportador";
                            else if (m.Pset == null || !m.Ativa) { l.Situacao = "não sai no IFC"; l.NaoSaiNoIfc = true; l.Pendente = true; }
                            else if (Igual(m.Source, "PropertySet")) { l.PsetModelo = m.Grupo; l.PropModelo = m.Nome; l.Preenchidos = 0; l.Vazios = 0; }
                            else l.Situacao = "lido de " + SaidaIfcPainel.RotuloDe(m.Source).ToLowerInvariant();
                            linhas.Add(l);
                        }
                        layersDe[l].Add(g.Nome);
                        l.Objetos += g.Ids.Count;
                        l.Ids.AddRange(g.Ids);
                        if (l.PsetModelo == null) continue;
                        foreach (ObjectId id in g.Ids)
                        {
                            string v = null;
                            try
                            {
                                var obj = tr.GetObject(id, OpenMode.ForRead);
                                v = ClassificacaoCore.Valor(ClassificacaoCore.Pset(obj, l.PsetModelo, tr), l.PropModelo, out _, out _);
                            }
                            catch { }
                            if (string.IsNullOrWhiteSpace(v)) { l.Vazios++; l.IdsVazios.Add(id); }
                            else l.Preenchidos++;
                        }
                        l.Pendente = l.Vazios > 0;
                    }
                }
                foreach (var kv in layersDe)
                    kv.Key.Layers = string.Join(", ", kv.Value.OrderBy(n => n).Take(4)) + (kv.Value.Count > 4 ? $" e mais {kv.Value.Count - 4}" : "");
                tr.Commit();
            }
            return linhas
                .OrderBy(l => l.SemObjeto ? 1 : 0)
                .ThenBy(l => l.Objeto, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(l => l.Classe == 0 ? 9 : l.Classe).ThenBy(l => l.Campo, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        static bool Igual(string a, string b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
