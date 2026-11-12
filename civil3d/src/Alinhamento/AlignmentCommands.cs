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
                    ProjectedCrs = metro.Estrutura?.CoordenadasPtl == false ? metro.Estrutura.SistemaCoordenadas : null,   // PTL: sem EPSG, como no Revit
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
            if (ids.Count == 0)
            {
                // Pavimentação, interferências etc.: o IFC sai sem IfcAlignment e o resto do pós-processamento segue.
                doc.Editor.WriteMessage("\nMetroSP: desenho sem alinhamento; o IFC vai com projeto, terreno, instalações e os objetos do desenho.");
                return new List<AlignmentModel>();
            }
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
                var alignments = ids.Select(id => (Alignment)tr.GetObject(id, OpenMode.ForRead)).ToList();
                var chosen = ChooseProfiles(doc, tr, alignments, opts.PerfisPreferidos, interactive);

                int withoutProfile = 0, points = 0;
                foreach (var al in alignments)
                {
                    chosen.TryGetValue(al.Handle.ToString(), out var profile);
                    if (profile == null)
                    {
                        withoutProfile++;
                        ed.WriteMessage($"\nMetroSP: {al.Name} não tem perfil de projeto; vai só com a geometria em planta.");
                    }
                    // Um alinhamento ou perfil com problema não pode parar a exportação.
                    AlignmentModel model = null;
                    try { model = CivilAlignmentReader.Read(al, profile, opts.Passo); }
                    catch (System.Exception ex) when (profile != null)
                    {
                        ed.WriteMessage($"\nMetroSP: aviso - {al.Name}: não deu para ler o perfil {profile.Name} ({ex.Message}); vai só com a geometria em planta.");
                        Log.Erro($"perfil {profile.Name} do alinhamento {al.Name}", ex);
                        profile = null;
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage($"\nMetroSP: aviso - {al.Name}: não deu para ler o alinhamento ({ex.Message}); fica como a Autodesk exportar.");
                        Log.Erro($"alinhamento {al.Name}", ex);
                        continue;
                    }
                    if (model == null)
                    {
                        try { model = CivilAlignmentReader.Read(al, null, opts.Passo); }
                        catch (System.Exception ex)
                        {
                            ed.WriteMessage($"\nMetroSP: aviso - {al.Name}: não deu para ler o alinhamento ({ex.Message}); fica como a Autodesk exportar.");
                            Log.Erro($"alinhamento {al.Name}", ex);
                            continue;
                        }
                    }
                    if (model.Lacunas > 0)
                        ed.WriteMessage($"\nMetroSP: aviso - {al.Name}: o perfil {profile?.Name} tem trecho sem cota ({model.Lacunas} estaca(s) puladas no eixo 3D).");
                    points += model.Axis3D.Count;
                    models.Add(model);
                }
                tr.Commit();
                ed.WriteMessage($"\nMetroSP: {models.Count} alinhamento(s) lidos" +
                                (withoutProfile > 0 ? $", {withoutProfile} sem perfil" : "") + $" ({points} pontos no eixo 3D).");
            }
            return models;
        }

        /// <summary>
        /// Perfil de cada alinhamento (layout ou importado). Com mais de um: a escolha guardada para este desenho,
        /// depois a lista de preferidos; o que sobrar vai numa janela só (ou, sem interação, a sugestão).
        /// </summary>
        static Dictionary<string, Profile> ChooseProfiles(AcDocument doc, Transaction tr, List<Alignment> alignments,
                                                          List<string> preferred, bool interactive)
        {
            var result = new Dictionary<string, Profile>();
            var saved = ProfileMemory.Load(doc.Name);
            var ask = new List<(Alignment Al, List<Profile> Candidates)>();

            foreach (var al in alignments)
            {
                var key = al.Handle.ToString();
                var candidates = new List<Profile>();
                foreach (ObjectId id in al.GetProfileIds())
                {
                    try
                    {
                        var p = (Profile)tr.GetObject(id, OpenMode.ForRead);
                        if (p.ProfileType == ProfileType.FG || p.ProfileType == ProfileType.File) candidates.Add(p);
                    }
                    catch (System.Exception ex)
                    {
                        doc.Editor.WriteMessage($"\nMetroSP: aviso - {al.Name}: um perfil não pôde ser lido ({ex.Message}); ignorado.");
                        Log.Erro($"perfil do alinhamento {al.Name}", ex);
                    }
                }
                candidates = candidates.OrderBy(p => p.Name).ToList();

                if (candidates.Count <= 1) { result[key] = candidates.FirstOrDefault(); continue; }

                var hit = saved.TryGetValue(key, out var savedName) ? candidates.FirstOrDefault(p => p.Name == savedName) : null;
                foreach (var pattern in (preferred ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    if (hit != null) break;
                    hit = candidates.FirstOrDefault(p => Regex.IsMatch(p.Name, pattern, RegexOptions.IgnoreCase));
                }
                if (hit != null) { result[key] = hit; continue; }

                result[key] = Suggest(al, candidates);
                ask.Add((al, candidates));
            }

            if (ask.Count == 0 || !interactive) return result;

            var rows = ask.Select(a => new Ui.ProfileWindow.Row
            {
                Alignment = a.Al.Name,
                Profiles = a.Candidates.Select(p => p.Name).ToList(),
                Chosen = result[a.Al.Handle.ToString()].Name
            }).ToList();
            if (!Ui.ProfileWindow.Ask(rows, out var remember))
                throw new OperationCanceledException("Exportação cancelada.");

            for (int i = 0; i < ask.Count; i++)
            {
                var key = ask[i].Al.Handle.ToString();
                result[key] = ask[i].Candidates.First(p => p.Name == rows[i].Chosen);
                if (remember) saved[key] = rows[i].Chosen;
            }
            if (remember) ProfileMemory.Save(doc.Name, saved);
            return result;
        }

        /// <summary>Perfil de cada alinhamento, sem perguntar nada: base da etapa Alinhamentos da Entrega passo a passo.</summary>
        public class SituacaoPerfil
        {
            public string Handle { get; set; }
            public ObjectId Id { get; set; }
            public string Alinhamento { get; set; }
            public List<string> Perfis { get; set; } = new List<string>();
            public string Escolhido { get; set; }
            /// <summary>Único, Guardado, Preferido, Sugerido (a exportação perguntaria) ou Nenhum.</summary>
            public string Origem { get; set; }
            public string Estacas { get; set; }
            public bool Pendente => Origem == "Sugerido" || Origem == "Nenhum";
        }

        public static List<SituacaoPerfil> Situacao(AcDocument doc, AlignmentOptions opts)
        {
            var r = new List<SituacaoPerfil>();
            var saved = ProfileMemory.Load(doc.Name);
            var preferred = opts?.PerfisPreferidos ?? new List<string>();
            using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (ObjectId id in CivilApplication.ActiveDocument.GetAlignmentIds())
                {
                    Alignment al;
                    try { al = (Alignment)tr.GetObject(id, OpenMode.ForRead); } catch { continue; }
                    var s = new SituacaoPerfil { Id = id, Handle = al.Handle.ToString(), Alinhamento = al.Name };
                    try { s.Estacas = $"{al.StartingStation:0.000} a {al.EndingStation:0.000}"; } catch { }
                    var candidates = new List<Profile>();
                    foreach (ObjectId pid in al.GetProfileIds())
                    {
                        try
                        {
                            var p = (Profile)tr.GetObject(pid, OpenMode.ForRead);
                            if (p.ProfileType == ProfileType.FG || p.ProfileType == ProfileType.File) candidates.Add(p);
                        }
                        catch { }
                    }
                    candidates = candidates.OrderBy(p => p.Name).ToList();
                    s.Perfis = candidates.Select(p => p.Name).ToList();
                    if (candidates.Count == 0) s.Origem = "Nenhum";
                    else if (candidates.Count == 1) { s.Origem = "Único"; s.Escolhido = candidates[0].Name; }
                    else
                    {
                        Profile hit = saved.TryGetValue(s.Handle, out var savedName) ? candidates.FirstOrDefault(p => p.Name == savedName) : null;
                        if (hit != null) s.Origem = "Guardado";
                        foreach (var pattern in preferred.Where(x => !string.IsNullOrWhiteSpace(x)))
                        {
                            if (hit != null) break;
                            try { hit = candidates.FirstOrDefault(p => Regex.IsMatch(p.Name, pattern, RegexOptions.IgnoreCase)); } catch { }
                            if (hit != null) s.Origem = "Preferido";
                        }
                        if (hit == null) { hit = Suggest(al, candidates); s.Origem = "Sugerido"; }
                        s.Escolhido = hit.Name;
                    }
                    r.Add(s);
                }
                tr.Commit();
            }
            return r.OrderBy(x => x.Alinhamento, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>Guarda a escolha de perfil deste desenho (a exportação não pergunta mais por esses alinhamentos).</summary>
        public static void GuardarPerfis(AcDocument doc, IEnumerable<(string Handle, string Perfil)> escolhas)
        {
            var saved = ProfileMemory.Load(doc.Name);
            foreach (var (h, p) in escolhas)
                if (!string.IsNullOrEmpty(h) && !string.IsNullOrEmpty(p)) saved[h] = p;
            ProfileMemory.Save(doc.Name, saved);
        }

        /// <summary>Sugestão: o perfil com o nome do alinhamento e que não seja cópia; senão o primeiro que não seja cópia.</summary>
        static Profile Suggest(Alignment al, List<Profile> candidates)
        {
            bool IsCopy(Profile p) => Regex.IsMatch(p.Name, @"\[Copy|\(\d+\)|c[oó]pia", RegexOptions.IgnoreCase);
            return candidates.FirstOrDefault(p => !IsCopy(p) && p.Name.IndexOf(al.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                   ?? candidates.FirstOrDefault(p => !IsCopy(p))
                   ?? candidates[0];
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

    /// <summary>Perfis escolhidos por desenho (%APPDATA%\MetroSP\perfis.json): DWG → handle do alinhamento → perfil.</summary>
    static class ProfileMemory
    {
        static string FilePath => Path.Combine(Settings.UserDir, "perfis.json");

        static Dictionary<string, Dictionary<string, string>> All()
        {
            try
            {
                return File.Exists(FilePath)
                    ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(FilePath))
                      ?? new Dictionary<string, Dictionary<string, string>>()
                    : new Dictionary<string, Dictionary<string, string>>();
            }
            catch { return new Dictionary<string, Dictionary<string, string>>(); }
        }

        public static Dictionary<string, string> Load(string dwg) =>
            All().TryGetValue(Key(dwg), out var d) ? d : new Dictionary<string, string>();

        public static void Save(string dwg, Dictionary<string, string> choices)
        {
            var all = All();
            all[Key(dwg)] = choices;
            Directory.CreateDirectory(Settings.UserDir);
            File.WriteAllText(FilePath, System.Text.Json.JsonSerializer.Serialize(all, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }

        static string Key(string dwg) => (dwg ?? "").Trim().ToLowerInvariant();
    }
}
