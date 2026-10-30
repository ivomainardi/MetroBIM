using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using MetroSP.Config;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDocument = Autodesk.AutoCAD.ApplicationServices.Document;

[assembly: CommandClass(typeof(MetroSP.Alinhamento.AlignmentCommands))]

namespace MetroSP.Alinhamento
{
    public class AlignmentCommands
    {
        /// <summary>
        /// Exporta alinhamentos para um IFC 4.3 separado, com o perfil vertical aplicado: eixo 3D com as cotas
        /// do perfil e semântica horizontal + vertical completa (IfcAlignmentHorizontal/Vertical).
        /// O fluxo normal (Exportar IFC) já aplica isso dentro do IFC da Autodesk; este comando serve para
        /// gerar só os alinhamentos.
        /// </summary>
        [CommandMethod("MSP_IFCALINHAMENTO")]
        public void ExportAlignments()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;
            try
            {
                var metro = ConfigPackage.LoadMetroSp();
                var ids = SelectAlignments(ed);
                if (ids == null || ids.Count == 0) return;

                var models = Read(doc, ids, metro.Alinhamento, interactive: true);
                var path = AskOutputPath(ed, doc.Name, ids.Count == 1 ? models[0].Name : "Alinhamentos");
                if (path == null) return;

                var info = new IfcExportInfo
                {
                    ProjectName = Path.GetFileNameWithoutExtension(doc.Name),
                    SourceFile = Path.GetFileName(doc.Name),
                    Author = Environment.UserName,
                    ProjectedCrs = metro.Estrutura?.SistemaCoordenadas,
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

        /// <summary>Todos os alinhamentos do desenho, cada um com o perfil escolhido.</summary>
        public static List<AlignmentModel> ReadAll(AcDocument doc, AlignmentOptions opts, bool interactive)
        {
            var ids = CivilApplication.ActiveDocument.GetAlignmentIds().Cast<ObjectId>().ToList();
            return Read(doc, ids, opts, interactive);
        }

        static List<AlignmentModel> Read(AcDocument doc, List<ObjectId> ids, AlignmentOptions opts, bool interactive)
        {
            var ed = doc.Editor;
            var db = doc.Database;
            if (db.Insunits != UnitsValue.Meters)
                ed.WriteMessage($"\nMetroSP: aviso - unidade do desenho é {db.Insunits}; o IFC é gravado em metros.");

            var models = new List<AlignmentModel>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in ids)
                {
                    var al = (Alignment)tr.GetObject(id, OpenMode.ForRead);
                    var profile = ChooseProfile(ed, tr, al, opts.PerfisPreferidos, interactive);
                    if (profile == null)
                        ed.WriteMessage($"\nMetroSP: {al.Name} sem perfil de projeto; fica só com a geometria horizontal.");

                    var model = CivilAlignmentReader.Read(al, profile, opts.Passo);
                    models.Add(model);
                    ed.WriteMessage($"\nMetroSP: {al.Name}: {Math.Max(0, model.Horizontal.Count - 1)} segmento(s) horizontais, " +
                                    $"{Math.Max(0, model.Vertical.Count - 1)} vertical(is), {model.Axis3D.Count} ponto(s) no eixo 3D" +
                                    (profile != null ? $", perfil {profile.Name}." : "."));
                }
                tr.Commit();
            }
            return models;
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

        /// <summary>
        /// Perfil de projeto (layout ou importado). Com mais de um: o primeiro que casar com a lista de
        /// preferidos; se nenhum casar, pergunta (ou, sem interação, usa o primeiro em ordem alfabética).
        /// </summary>
        static Profile ChooseProfile(Editor ed, Transaction tr, Alignment al, List<string> preferred, bool interactive)
        {
            var candidates = al.GetProfileIds().Cast<ObjectId>()
                .Select(id => (Profile)tr.GetObject(id, OpenMode.ForRead))
                .Where(p => p.ProfileType == ProfileType.FG || p.ProfileType == ProfileType.File)
                .OrderBy(p => p.Name)
                .ToList();

            if (candidates.Count <= 1) return candidates.FirstOrDefault();

            foreach (var pattern in preferred ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(pattern)) continue;
                var hit = candidates.FirstOrDefault(p => Regex.IsMatch(p.Name, pattern, RegexOptions.IgnoreCase));
                if (hit != null) return hit;
            }

            if (!interactive) return candidates[0];

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
