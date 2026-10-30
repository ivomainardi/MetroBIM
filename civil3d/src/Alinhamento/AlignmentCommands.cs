using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(MetroSP.Alinhamento.AlignmentCommands))]

namespace MetroSP.Alinhamento
{
    public class AlignmentCommands
    {
        /// <summary>
        /// Exporta alinhamentos para IFC 4.3 com o perfil vertical aplicado: o eixo sai em 3D com as cotas
        /// do perfil, e a semântica horizontal + vertical vai completa (IfcAlignmentHorizontal/Vertical).
        /// </summary>
        [CommandMethod("MSP_IFCALINHAMENTO")]
        public void ExportAlignments()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;
            try
            {
                var cfg = Settings.Load();
                if (db.Insunits != UnitsValue.Meters)
                    ed.WriteMessage($"\nMetroSP: aviso - unidade do desenho é {db.Insunits}; o IFC é gravado em metros.");

                var ids = SelectAlignments(ed);
                if (ids == null || ids.Count == 0) return;

                var models = new List<AlignmentModel>();
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    foreach (var id in ids)
                    {
                        var al = (Alignment)tr.GetObject(id, OpenMode.ForRead);
                        var profile = ChooseProfile(ed, tr, al);
                        if (profile == null)
                            ed.WriteMessage($"\nMetroSP: {al.Name} sem perfil de projeto; vai só a geometria horizontal (sem eixo 3D).");

                        var model = CivilAlignmentReader.Read(al, profile, cfg.AlinhamentoPasso);
                        models.Add(model);
                        ed.WriteMessage($"\nMetroSP: {al.Name}: {model.Horizontal.Count - 1} segmento(s) horizontais, " +
                                        $"{Math.Max(0, model.Vertical.Count - 1)} vertical(is), {model.Axis3D.Count} ponto(s) no eixo 3D.");
                    }
                    tr.Commit();
                }

                var path = AskOutputPath(ed, doc.Name, ids.Count == 1 ? models[0].Name : "Alinhamentos");
                if (path == null) return;

                var info = new IfcExportInfo
                {
                    ProjectName = Path.GetFileNameWithoutExtension(doc.Name),
                    SourceFile = Path.GetFileName(doc.Name),
                    Author = Environment.UserName,
                    ProjectedCrs = cfg.SistemaCoordenadas,
                    Seed = db.FingerprintGuid
                };
                new IfcAlignmentWriter(info).Write(path, models);
                ed.WriteMessage($"\nMetroSP: IFC gravado em {path}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nMetroSP: ERRO - " + ex.Message);
            }
        }

        static List<ObjectId> SelectAlignments(Editor ed)
        {
            var pko = new PromptKeywordOptions("\nExportar alinhamentos [Selecionar/Todos] <Selecionar>: ");
            pko.Keywords.Add("Selecionar");
            pko.Keywords.Add("Todos");
            pko.Keywords.Default = "Selecionar";
            pko.AllowNone = true;
            var kr = ed.GetKeywords(pko);
            if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return null;

            if (kr.StringResult == "Todos")
                return CivilApplication.ActiveDocument.GetAlignmentIds().Cast<ObjectId>().ToList();

            var pso = new PromptSelectionOptions { MessageForAdding = "\nSelecione os alinhamentos: " };
            var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "AECC_ALIGNMENT") });
            var sr = ed.GetSelection(pso, filter);
            return sr.Status == PromptStatus.OK ? sr.Value.GetObjectIds().ToList() : null;
        }

        /// <summary>Perfil de projeto (layout). Se houver mais de um, pergunta qual.</summary>
        static Profile ChooseProfile(Editor ed, Transaction tr, Alignment al)
        {
            var candidates = al.GetProfileIds().Cast<ObjectId>()
                .Select(id => (Profile)tr.GetObject(id, OpenMode.ForRead))
                .Where(p => p.ProfileType == ProfileType.FG || p.ProfileType == ProfileType.File)
                .OrderBy(p => p.Name)
                .ToList();

            if (candidates.Count <= 1) return candidates.FirstOrDefault();

            ed.WriteMessage($"\nPerfis de {al.Name}:");
            for (int i = 0; i < candidates.Count; i++)
                ed.WriteMessage($"\n  {i + 1} - {candidates[i].Name}");

            var pio = new PromptIntegerOptions($"\nPerfil para {al.Name} <1>: ")
            {
                LowerLimit = 1,
                UpperLimit = candidates.Count,
                DefaultValue = 1,
                UseDefaultValue = true,
                AllowNone = true
            };
            var r = ed.GetInteger(pio);
            if (r.Status == PromptStatus.Cancel) throw new OperationCanceledException("Cancelado.");
            return candidates[(r.Status == PromptStatus.OK ? r.Value : 1) - 1];
        }

        static string AskOutputPath(Editor ed, string dwgPath, string name)
        {
            var dir = Path.GetDirectoryName(dwgPath);
            var baseName = Path.GetFileNameWithoutExtension(dwgPath) + "-" + Sanitize(name) + ".ifc";
            var opts = new PromptSaveFileOptions("\nSalvar IFC do alinhamento")
            {
                Filter = "IFC 4.3 (*.ifc)|*.ifc",
                InitialFileName = baseName,
                InitialDirectory = string.IsNullOrEmpty(dir) || !Directory.Exists(dir) ? null : dir
            };
            var r = ed.GetFileNameForSave(opts);
            return r.Status == PromptStatus.OK ? r.StringResult : null;
        }

        static string Sanitize(string s) =>
            string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    }
}
