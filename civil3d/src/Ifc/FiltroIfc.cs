using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using MetroSP.Config;

namespace MetroSP.Ifc
{
    /// <summary>
    /// Separa os objetos do desenho que vão para o IFC (regra comum das ferramentas MetroSP). Ficam de fora:
    /// o que não está no espaço do modelo, layers desligados ou congelados, layers e blocos marcados com
    /// "Export": false no IfcInfraExportMapping.json ativo, e anotações (textos, cotas, hachuras, chamadas).
    /// </summary>
    public sealed class FiltroIfc
    {
        readonly List<Regex> _layersFora = new List<Regex>();
        readonly List<Regex> _blocosFora = new List<Regex>();
        readonly Dictionary<ObjectId, string> _layerFora = new Dictionary<ObjectId, string>();

        /// <summary>Classes que o exportador não leva como elemento.</summary>
        static readonly Type[] Anotacoes =
        {
            typeof(DBText), typeof(MText), typeof(Dimension), typeof(Leader), typeof(MLeader), typeof(Hatch),
            typeof(Table), typeof(Viewport), typeof(AttributeDefinition), typeof(Wipeout), typeof(RasterImage),
        };

        public FiltroIfc(Database db, Transaction tr)
        {
            try
            {
                JsonNode mapa = ConfigPackage.LoadJson(ConfigKind.ObjectMapping);
                Ler(mapa?["MapCADLayerName"] as JsonArray, _layersFora);
                Ler(mapa?["MapBlockName"] as JsonArray, _blocosFora);
            }
            catch { /* sem mapeamento: só as regras fixas */ }

            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId id in lt)
            {
                var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                string motivo = l.IsFrozen ? "Layer congelado" : l.IsOff ? "Layer desligado"
                    : _layersFora.Any(r => r.IsMatch(l.Name)) ? "Layer fora da exportação IFC" : null;
                if (motivo != null) _layerFora[id] = motivo;
            }
        }

        static void Ler(JsonArray regras, List<Regex> fora)
        {
            if (regras == null) return;
            foreach (JsonNode r in regras)
            {
                string nome = (string)r?["Name"];
                bool exporta = r?["Export"] == null || (bool)r["Export"];
                if (string.IsNullOrEmpty(nome) || exporta) continue;
                try { fora.Add(new Regex(nome, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)); } catch { /* regex inválida */ }
            }
        }

        /// <summary>Por que o objeto não vai para o IFC, ou null se vai.</summary>
        public string MotivoFora(Entity e, Transaction tr)
        {
            if (e == null) return "Não é objeto de desenho";
            Type t = e.GetType();
            if (Anotacoes.Any(a => a.IsAssignableFrom(t))) return "Anotação";
            if (_layerFora.TryGetValue(e.LayerId, out string m)) return m;
            if (e is BlockReference br && _blocosFora.Count > 0)
            {
                string nome = NomeBloco(br, tr);
                if (_blocosFora.Any(r => r.IsMatch(nome))) return "Bloco fora da exportação IFC";
            }
            return null;
        }

        public bool VaiParaIfc(Entity e, Transaction tr) => MotivoFora(e, tr) == null;

        public static string NomeBloco(BlockReference br, Transaction tr)
        {
            try
            {
                ObjectId def = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
                return ((BlockTableRecord)tr.GetObject(def, OpenMode.ForRead)).Name;
            }
            catch { return br.Name; }
        }

        /// <summary>Objetos do espaço do modelo que vão para o IFC (ou só os da seleção, se houver).</summary>
        public static List<ObjectId> Objetos(Database db, Transaction tr, IEnumerable<ObjectId> selecao = null)
        {
            var filtro = new FiltroIfc(db, tr);
            ObjectId modelo = SymbolUtilityServices.GetBlockModelSpaceId(db);
            IEnumerable<ObjectId> ids = selecao ?? ((BlockTableRecord)tr.GetObject(modelo, OpenMode.ForRead)).Cast<ObjectId>();
            var r = new List<ObjectId>();
            foreach (ObjectId id in ids)
            {
                if (id.IsNull || id.IsErased) continue;
                if (!(tr.GetObject(id, OpenMode.ForRead, false) is Entity e) || e.OwnerId != modelo) continue;
                if (filtro.VaiParaIfc(e, tr)) r.Add(id);
            }
            return r;
        }
    }
}
