using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using Corridor = Autodesk.Civil.DatabaseServices.Corridor;
using Baseline = Autodesk.Civil.DatabaseServices.Baseline;

namespace MetroSP.Ifc
{
    /// <summary>
    /// Um nome que existe no desenho (layer, bloco ou baseline de corredor), com quantos objetos do espaço do
    /// modelo o usam. A aba Classes IFC mostra estes nomes junto com as regras do padrão.
    /// </summary>
    public sealed class ItemDesenho
    {
        public string Nome { get; set; }
        public int Objetos { get; set; }
        /// <summary>Tipos de objeto (ex.: "Sólido 3D 12, Corredor 1").</summary>
        public string Tipos { get; set; } = "";
        /// <summary>Desligado / congelado (só layers).</summary>
        public string Situacao { get; set; } = "";

    }

    /// <summary>
    /// Lê do desenho ativo os nomes que as regras de Classes IFC usam: layers, blocos e baselines de corredor.
    /// Conta só objetos do espaço do modelo que não são anotação (textos, cotas, hachuras etc. não vão para o IFC).
    /// </summary>
    public static class ConteudoDesenho
    {
        public const string Layers = "MapCADLayerName";
        public const string Blocos = "MapBlockName";
        public const string Baselines = "MapBaselineName";

        /// <summary>Nome do item no singular/plural para a interface.</summary>
        public static (string Um, string Varios) Palavra(string secao) =>
            secao == Layers ? ("layer", "layers") : secao == Blocos ? ("bloco", "blocos") : secao == Baselines ? ("baseline", "baselines") : ("nome", "nomes");

        /// <summary>Seções que o plugin sabe ler do desenho.</summary>
        public static bool Le(string secao) => secao == Layers || secao == Blocos || secao == Baselines;

        /// <summary>Layers, blocos e baselines do desenho ativo. Vazio se não houver desenho aberto.</summary>
        public static Dictionary<string, List<ItemDesenho>> Ler()
        {
            var r = new Dictionary<string, List<ItemDesenho>>
            {
                [Layers] = new List<ItemDesenho>(),
                [Blocos] = new List<ItemDesenho>(),
                [Baselines] = new List<ItemDesenho>(),
            };
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return r;
            Database db = doc.Database;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var layers = new Dictionary<ObjectId, ItemDesenho>();
                var tiposLayer = new Dictionary<ObjectId, Dictionary<string, int>>();
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId id in lt)
                {
                    var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (l.IsDependent) continue;   // layers de referência externa (xref|layer)
                    layers[id] = new ItemDesenho
                    {
                        Nome = l.Name,
                        Situacao = l.IsFrozen ? "Congelado" : l.IsOff ? "Desligado" : "",
                    };
                    tiposLayer[id] = new Dictionary<string, int>();
                }

                var blocos = new Dictionary<string, ItemDesenho>(StringComparer.OrdinalIgnoreCase);
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in bt)
                {
                    var b = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (b.IsLayout || b.IsAnonymous || b.IsFromExternalReference || b.IsDependent || b.Name.StartsWith("*")) continue;
                    blocos[b.Name] = new ItemDesenho { Nome = b.Name };
                }

                ObjectId modelo = SymbolUtilityServices.GetBlockModelSpaceId(db);
                foreach (ObjectId id in (BlockTableRecord)tr.GetObject(modelo, OpenMode.ForRead))
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead, false) is Entity e)) continue;
                    if (FiltroIfc.EhAnotacao(e)) continue;
                    if (layers.TryGetValue(e.LayerId, out var item))
                    {
                        item.Objetos++;
                        var t = tiposLayer[e.LayerId];
                        string tipo = Tipo(e);
                        t[tipo] = t.TryGetValue(tipo, out int n) ? n + 1 : 1;
                    }
                    if (e is BlockReference br)
                    {
                        string nome = FiltroIfc.NomeBloco(br, tr);
                        if (!blocos.TryGetValue(nome, out var b)) blocos[nome] = b = new ItemDesenho { Nome = nome };
                        b.Objetos++;
                    }
                }
                foreach (var kv in layers)
                    kv.Value.Tipos = string.Join(", ", tiposLayer[kv.Key].OrderByDescending(x => x.Value).Select(x => $"{x.Key} {x.Value}"));
                foreach (var b in blocos.Values) if (b.Objetos > 0) b.Tipos = "Bloco " + b.Objetos;

                r[Layers] = layers.Values.OrderBy(i => i.Nome, StringComparer.OrdinalIgnoreCase).ToList();
                r[Blocos] = blocos.Values.OrderBy(i => i.Nome, StringComparer.OrdinalIgnoreCase).ToList();

                try
                {
                    var bl = new Dictionary<string, ItemDesenho>(StringComparer.OrdinalIgnoreCase);
                    foreach (ObjectId cid in CivilApplication.ActiveDocument.CorridorCollection)
                    {
                        if (!(tr.GetObject(cid, OpenMode.ForRead) is Corridor c)) continue;
                        foreach (Baseline b in c.Baselines)
                        {
                            if (!bl.TryGetValue(b.Name, out var it)) bl[b.Name] = it = new ItemDesenho { Nome = b.Name };
                            it.Objetos++;
                            it.Tipos = string.IsNullOrEmpty(it.Tipos) ? "Corredor " + c.Name : it.Tipos + ", " + c.Name;
                        }
                    }
                    r[Baselines] = bl.Values.OrderBy(i => i.Nome, StringComparer.OrdinalIgnoreCase).ToList();
                }
                catch { /* desenho sem Civil 3D ou corredor com erro: segue sem baselines */ }

                tr.Commit();
            }
            return r;
        }

        static readonly Dictionary<string, string> Nomes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Solid3d"] = "Sólido 3D", ["SubDMesh"] = "Malha", ["PolyFaceMesh"] = "Malha", ["PolygonMesh"] = "Malha",
            ["Surface"] = "Superfície", ["BlockReference"] = "Bloco", ["Corridor"] = "Corredor", ["Alignment"] = "Alinhamento",
            ["Profile"] = "Perfil", ["FeatureLine"] = "Feature line", ["TinSurface"] = "Superfície TIN", ["GridSurface"] = "Superfície grid",
            ["Pipe"] = "Tubo", ["Structure"] = "Estrutura", ["PressurePipe"] = "Tubo pressão", ["Polyline"] = "Polilinha",
            ["Polyline3d"] = "Polilinha 3D", ["Polyline2d"] = "Polilinha 2D", ["Line"] = "Linha", ["Arc"] = "Arco", ["Circle"] = "Círculo",
            ["Region"] = "Região", ["Body"] = "Corpo", ["CogoPoint"] = "Ponto COGO", ["Point"] = "Ponto",
        };

        static string Tipo(Entity e)
        {
            string n = e.GetType().Name;
            return Nomes.TryGetValue(n, out string pt) ? pt : n;
        }
    }
}
