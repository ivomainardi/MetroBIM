using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using MetroSP.Ifc;
using AcDocument = Autodesk.AutoCAD.ApplicationServices.Document;

namespace MetroSP.Terreno
{
    /// <summary>
    /// Lê as superfícies TIN do desenho que vão para o IFC (regra comum do FiltroIfc: layer ligado, não
    /// congelado e não marcado "Export": false). Vão para o IFC como IfcGeographicElement.TERRAIN.
    /// </summary>
    public static class CivilSurfaceReader
    {
        public static List<SurfaceModel> ReadAll(AcDocument doc)
        {
            var result = new List<SurfaceModel>();
            var ed = doc.Editor;
            var db = doc.Database;
            ObjectIdCollection ids;
            try { ids = CivilApplication.ActiveDocument.GetSurfaceIds(); }
            catch (Exception ex) { Log.Erro("lista de superfícies", ex); return result; }
            if (ids == null || ids.Count == 0) return result;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var filtro = new FiltroIfc(db, tr);
                var modelo = SymbolUtilityServices.GetBlockModelSpaceId(db);
                foreach (ObjectId id in ids)
                {
                    if (id.IsNull || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is TinSurface s)) continue;   // grid e volume ficam de fora
                    if (s.OwnerId != modelo) continue;
                    var fora = filtro.MotivoFora(s, tr);
                    if (fora != null)
                    {
                        ed.WriteMessage($"\nMetroSP: superfície {s.Name} fora do IFC ({fora}).");
                        continue;
                    }
                    try
                    {
                        var m = Read(s);
                        if (m.Triangles.Count == 0)
                        {
                            ed.WriteMessage($"\nMetroSP: superfície {s.Name} sem triângulos; fora do IFC.");
                            continue;
                        }
                        result.Add(m);
                        ed.WriteMessage($"\nMetroSP: superfície {m.Name} lida ({m.Points.Count} pontos, {m.Triangles.Count} triângulos).");
                    }
                    catch (Exception ex)
                    {
                        ed.WriteMessage($"\nMetroSP: aviso - não deu para ler a superfície {s.Name} ({ex.Message}); fica fora do IFC.");
                        Log.Erro($"superfície {s.Name}", ex);
                    }
                }
                tr.Commit();
            }
            return result;
        }

        static SurfaceModel Read(TinSurface s)
        {
            var m = new SurfaceModel { Name = s.Name, Description = s.Description, Handle = s.Handle.ToString(), Layer = s.Layer };
            // Pontos repetidos entre triângulos viram um só (chave ao milímetro).
            var index = new Dictionary<(long, long, long), int>();
            int Ponto(TinSurfaceVertex v)
            {
                var p = v.Location;
                var k = ((long)Math.Round(p.X * 1000), (long)Math.Round(p.Y * 1000), (long)Math.Round(p.Z * 1000));
                if (!index.TryGetValue(k, out var i))
                {
                    i = m.Points.Count;
                    index[k] = i;
                    m.Points.Add(new[] { p.X, p.Y, p.Z });
                }
                return i;
            }
            foreach (TinSurfaceTriangle t in s.GetTriangles(false))   // false: sem os triângulos escondidos (fora do contorno)
            {
                int a = Ponto(t.Vertex1), b = Ponto(t.Vertex2), c = Ponto(t.Vertex3);
                if (a == b || b == c || a == c) continue;
                m.Triangles.Add(new[] { a, b, c });
            }
            m.Compute();
            return m;
        }
    }
}
