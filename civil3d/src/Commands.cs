using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDocument = Autodesk.AutoCAD.ApplicationServices.Document;

[assembly: CommandClass(typeof(MetroSP.Commands))]

namespace MetroSP
{
    public class Commands
    {
        // Arquivos que o IFC Extension aceita com prefixo "[nome do DWG]-".
        static readonly string[] PrefixableConfigs = { "IfcInfraConfiguration.json", "IfcInfraExportMapping.json" };

        const string OriginLog = "MetroSP-IFC-origem.txt";

        // ------------------------------------------------------------------ comandos da ribbon

        [CommandMethod("MSP_IFCEXPORT")]
        public void IfcExport() => SyncAndRun(selected: false);

        [CommandMethod("MSP_IFCEXPORTSEL")]
        public void IfcExportSelected() => SyncAndRun(selected: true);

        [CommandMethod("MSP_IFCSYNC")]
        public void IfcSync() => SyncAndRun(selected: null);

        [CommandMethod("MSP_FORMALOGIN")]
        public void FormaLogin()
        {
            var ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
            try
            {
                var cfg = Settings.Load();
                cfg.Validate();
                ed.WriteMessage("\nMetroSP: conectando ao Forma (o navegador pode abrir para login)...");
                var log = new BufferedLog();
                var files = RunBlocking(ct => new FormaClient(cfg, log.Add).ListFolderAsync(ct), log, ed);
                ed.WriteMessage($"\nMetroSP: conectado ao Forma. {files.Count} arquivo(s) na pasta de configuração IFC.");
            }
            catch (System.Exception ex) { Fail(ed, ex); }
        }

        [CommandMethod("MSP_FORMALOGOUT")]
        public void FormaLogout()
        {
            FormaClient.Logout();
            AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nMetroSP: sessão do Forma encerrada neste computador.");
        }

        [CommandMethod("MSP_CONFIG")]
        public void OpenSettings()
        {
            var path = Settings.EnsureUserFile();
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nMetroSP: editando " + path);
        }

        [CommandMethod("MSP_IFCPASTA")]
        public void OpenConfigFolder()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var dir = DrawingFolder(doc) ?? ProgramDataDir;
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
        }

        [CommandMethod("MSP_SOBRE")]
        public void About()
        {
            var cfg = Settings.Load();
            AcApp.ShowAlertDialog(
                "MetroSP para Civil 3D\n\n" +
                "Exportação IFC 4.3 com configuração centralizada no Autodesk Forma.\n\n" +
                "Configuração: " + (cfg.LoadedFrom ?? "(não encontrada)") + "\n" +
                "Projeto: " + cfg.ProjectId + "\n" +
                "Pasta: " + cfg.FolderUrn);
        }

        // ------------------------------------------------------------------ fluxo principal

        /// <param name="selected">true = exportar seleção, false = exportar tudo, null = só sincronizar.</param>
        void SyncAndRun(bool? selected)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            try
            {
                var cfg = Settings.Load();
                cfg.Validate();

                var dwgDir = DrawingFolder(doc);
                if (dwgDir == null && cfg.Destino != "ProgramData")
                    throw new InvalidOperationException("Salve o desenho antes: os JSON de configuração são gravados na pasta do DWG.");

                ed.WriteMessage("\nMetroSP: buscando configuração IFC no Forma (o navegador pode abrir para login)...");
                var log = new BufferedLog();
                var written = RunBlocking(ct => SyncAsync(cfg, doc, dwgDir, log.Add, ct), log, ed);
                ed.WriteMessage($"\nMetroSP: {written.Count} arquivo(s) de configuração atualizados a partir do Forma.");

                if (selected == null) return;
                var cmd = selected.Value ? cfg.ExportSelectedCommand : cfg.ExportCommand;
                doc.SendStringToExecute("_." + cmd + " ", true, false, true);
            }
            catch (System.Exception ex) { Fail(ed, ex); }
        }

        static async Task<List<string>> SyncAsync(Settings cfg, AcDocument doc, string dwgDir, Action<string> log, CancellationToken ct)
        {
            var client = new FormaClient(cfg, log);
            var remote = await client.ListFolderAsync(ct);

            var wanted = cfg.Files != null && cfg.Files.Count > 0
                ? remote.Where(r => cfg.Files.Any(f => string.Equals(f, r.Name, StringComparison.OrdinalIgnoreCase))).ToList()
                : remote.Where(r => r.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                                    r.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)).ToList();

            var missing = (cfg.Files ?? new List<string>())
                .Where(f => !remote.Any(r => string.Equals(f, r.Name, StringComparison.OrdinalIgnoreCase)));
            foreach (var m in missing) log("aviso: \"" + m + "\" não existe na pasta do Forma.");

            if (wanted.Count == 0) throw new InvalidOperationException("Nenhum arquivo de configuração encontrado na pasta do Forma.");

            var dwgName = Path.GetFileNameWithoutExtension(doc.Name);
            var written = new List<string>();
            var origin = new StringBuilder();
            origin.AppendLine($"Configuração IFC baixada do Autodesk Forma em {DateTime.Now:yyyy-MM-dd HH:mm:ss} por {Environment.UserName}");
            origin.AppendLine($"Projeto: {cfg.ProjectId}  Pasta: {cfg.FolderUrn}");

            foreach (var f in wanted)
            {
                var data = await client.DownloadAsync(f, ct);
                if (f.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) EnsureValidJson(f.Name, data);

                var target = TargetPath(cfg, dwgDir, dwgName, f.Name);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (File.Exists(target) && !File.ReadAllBytes(target).SequenceEqual(data))
                    File.Copy(target, target + ".bak", overwrite: true);
                File.WriteAllBytes(target, data);

                written.Add(target);
                log($"{f.Name} v{f.VersionNumber} ({f.LastModifiedBy}, {f.LastModified}) -> {target}");
                origin.AppendLine($"{f.Name}\tv{f.VersionNumber}\t{f.LastModified}\t{f.LastModifiedBy}\t{f.VersionId}\t-> {target}");
            }

            File.WriteAllText(Path.Combine(dwgDir ?? ProgramDataDir, OriginLog), origin.ToString(), Encoding.UTF8);
            return written;
        }

        static string TargetPath(Settings cfg, string dwgDir, string dwgName, string fileName)
        {
            switch (cfg.Destino)
            {
                case "ProgramData":
                    return Path.Combine(ProgramDataDir, fileName);
                case "PastaDesenho":
                    return Path.Combine(dwgDir, fileName);
                default: // PorDesenho
                    var prefix = PrefixableConfigs.Any(p => string.Equals(p, fileName, StringComparison.OrdinalIgnoreCase));
                    return Path.Combine(dwgDir, prefix ? dwgName + "-" + fileName : fileName);
            }
        }

        static void EnsureValidJson(string name, byte[] data)
        {
            try
            {
                using var _ = JsonDocument.Parse(data, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"{name} no Forma não é um JSON válido (linha {ex.LineNumber + 1}): {ex.Message}");
            }
        }

        // ------------------------------------------------------------------ utilitários

        static string ProgramDataDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Autodesk", "Civil3D");

        static string DrawingFolder(AcDocument doc)
        {
            // Desenho nunca salvo: Database.Filename aponta para o template ou fica vazio.
            var saved = Convert.ToInt32(AcApp.GetSystemVariable("DWGTITLED")) == 1;
            return saved ? Path.GetDirectoryName(doc.Database.Filename) : null;
        }

        // A API é assíncrona; o comando roda no contexto do documento, então esperamos aqui.
        // O Editor só pode ser usado na thread principal: as mensagens ficam no buffer e saem no final.
        static T RunBlocking<T>(Func<CancellationToken, Task<T>> work, BufferedLog log, Editor ed)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            try { return Task.Run(() => work(cts.Token)).GetAwaiter().GetResult(); }
            finally { log.Flush(ed); }
        }

        class BufferedLog
        {
            readonly List<string> _lines = new List<string>();
            public void Add(string m) { lock (_lines) _lines.Add(m); }
            public void Flush(Editor ed)
            {
                lock (_lines)
                {
                    foreach (var l in _lines) ed.WriteMessage("\nMetroSP: " + l);
                    _lines.Clear();
                }
            }
        }

        static void Fail(Editor ed, System.Exception ex)
        {
            ed.WriteMessage("\nMetroSP: ERRO - " + ex.Message);
            Debug.WriteLine(ex);
        }
    }
}
