using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using MetroSP.Alinhamento;
using MetroSP.Config;
using MetroSP.Ifc;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDocument = Autodesk.AutoCAD.ApplicationServices.Document;

[assembly: CommandClass(typeof(MetroSP.Commands))]

namespace MetroSP
{
    public class Commands
    {
        const string OriginLog = "MetroSP-IFC-origem.txt";

        /// <summary>Exportação em andamento: o pós-processamento roda quando o comando da Autodesk termina.</summary>
        class PendingExport
        {
            public AcDocument Doc;
            public string Command;
            public DateTime StartUtc;
            public string DwgDir;
            public List<AlignmentModel> Alignments;
        }

        static PendingExport _pending;

        // ------------------------------------------------------------------ comandos da ribbon

        [CommandMethod("MSP_IFCEXPORT")]
        public void IfcExport() => PrepareAndRun(selected: false);

        [CommandMethod("MSP_IFCEXPORTSEL")]
        public void IfcExportSelected() => PrepareAndRun(selected: true);

        [CommandMethod("MSP_IFCSYNC")]
        public void IfcSync() => PrepareAndRun(selected: null);

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

        /// <summary>Janela de configurações IFC: pacote, mapeamento de classes, property sets, projeto/terreno/edificações, alinhamento.</summary>
        [CommandMethod("MSP_IFCCONFIG")]
        public void OpenIfcConfig()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            try
            {
                AcApp.ShowModalWindow(new Ui.ConfigWindow(DrawingFolder(doc)));
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage("\nMetroSP: ERRO - " + ex.Message);
            }
        }

        /// <summary>Janela Projeto IFC: IfcProject, IfcSite, instalações e Pset MetroSP_Projeto (a aba Projeto do Configurar IFC).</summary>
        [CommandMethod("MSP_IFCPROJETO")]
        public void OpenIfcProject()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            try
            {
                AcApp.ShowModalWindow(new Ui.ConfigWindow(DrawingFolder(doc), somenteProjeto: true));
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage("\nMetroSP: ERRO - " + ex.Message);
            }
        }

        /// <summary>Versão instalada da IFC 4.3 Extension da Autodesk e atalho para as atualizações.</summary>
        [CommandMethod("MSP_IFCEXTENSAO")]
        public void AutodeskExtension()
        {
            try { var cfg = Settings.Load(); Ui.ExtensionWindow.Show(cfg, cfg.ExportCommand, blocking: false);; }
            catch (System.Exception ex) { Fail(AcApp.DocumentManager.MdiActiveDocument.Editor, ex); }
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
            var version = typeof(Commands).Assembly.GetName().Version;
            var w = new System.Windows.Window
            {
                Title = "Sobre o MetroSP",
                Width = 520,
                SizeToContent = System.Windows.SizeToContent.Height,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner
            };

            var body = new System.Windows.Controls.StackPanel();
            body.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "Exportação IFC 4.3 do Civil 3D com o motor da Autodesk e pós-processamento MetroSP: " +
                       "alinhamento 3D com as cotas do perfil, IfcSite, IfcBuilding e configuração padronizada.",
                TextWrapping = System.Windows.TextWrapping.Wrap
            });
            body.Children.Add(Ui.Theme.Secao("Versão"));
            var chips = new System.Windows.Controls.WrapPanel();
            chips.Children.Add(Ui.Theme.Chip($"Versão {version.Major}.{version.Minor}.{version.Build}", escuro: true));
            chips.Children.Add(Ui.Theme.Chip("Civil 3D 2025 · 2026 · 2027", destaque: true));
            chips.Children.Add(Ui.Theme.Chip(cfg.UsesForma ? "Configuração do Forma" : "Configuração local"));
            body.Children.Add(chips);
            body.Children.Add(Ui.Theme.Secao("Arquivos"));
            body.Children.Add(Ui.Theme.Detalhe("Configuração: " + (cfg.LoadedFrom ?? "(não encontrada)")));
            body.Children.Add(Ui.Theme.Detalhe("Pacote IFC ativo: " + ConfigPackage.ActiveDir));
            body.Children.Add(Ui.Theme.Detalhe("Exportador da Autodesk: " + AutodeskIfcExtension.Describe(cfg.ExportCommand)));
            if (cfg.UsesForma)
            {
                body.Children.Add(Ui.Theme.Detalhe("Projeto Forma: " + cfg.ProjectId));
                body.Children.Add(Ui.Theme.Detalhe("Pasta Forma: " + cfg.FolderUrn));
            }

            var close = new System.Windows.Controls.Button { Content = "Fechar", Style = Ui.Theme.PrimaryButton, IsDefault = true, IsCancel = true };
            close.Click += (s, e) => w.Close();
            Ui.Theme.Apply(w, "MetroSP para Civil 3D", "Ação BIM · Metrô SP", body, Ui.Theme.Buttons(close));
            AcApp.ShowModalWindow(AcApp.MainWindow.Handle, w, false);
        }

        /// <summary>
        /// Pós-processa um IFC exportado pela Autodesk: eixo 3D dos alinhamentos, vertical, projeto/terreno/edificações.
        /// Chamado sozinho no fim do Exportar IFC, ou à mão escolhendo o arquivo.
        /// </summary>
        [CommandMethod("MSP_IFCPOSPROC")]
        public void PostProcess()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var pending = _pending != null && _pending.Doc == doc ? _pending : null;
            _pending = null;
            try
            {
                var metro = ConfigPackage.LoadMetroSp();
                string ifc = null;
                if (pending != null)
                {
                    ifc = FindExportedIfc(pending);
                    if (ifc == null) ed.WriteMessage("\nMetroSP: não achei o IFC recém-exportado na pasta do desenho; escolha o arquivo.");
                }
                if (ifc == null)
                {
                    var opts = new PromptOpenFileOptions("\nIFC exportado pelo Civil 3D")
                    {
                        Filter = "IFC (*.ifc)|*.ifc",
                        InitialDirectory = DrawingFolder(doc)
                    };
                    var r = ed.GetFileNameForOpen(opts);
                    if (r.Status != PromptStatus.OK) return;
                    ifc = r.StringResult;
                }

                var alignments = pending?.Alignments;
                if (alignments == null && metro.Alinhamento.AplicarNoIfc)
                    alignments = AlignmentCommands.ReadAll(doc, metro.Alinhamento, interactive: true);

                ed.WriteMessage($"\nMetroSP: pós-processando {Path.GetFileName(ifc)}...");
                IfcPostProcessor.ProcessFile(ifc, metro.Alinhamento.AplicarNoIfc ? alignments : null,
                    metro.Estrutura, metro.PropriedadesProjeto, m => ed.WriteMessage("\nMetroSP: " + m),
                    Path.GetFileName(doc.Name));
                ed.WriteMessage($"\nMetroSP: IFC pronto: {ifc}");
            }
            catch (System.Exception ex) { Fail(ed, ex); }
        }

        // ------------------------------------------------------------------ fluxo principal

        /// <param name="selected">true = exportar seleção, false = exportar tudo, null = só preparar a configuração.</param>
        void PrepareAndRun(bool? selected)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            try
            {
                var cfg = Settings.Load();
                if (selected != null)
                {
                    // Sem a extensão IFC 4.3 da Autodesk não há o que exportar: avisa antes de qualquer trabalho.
                    var command = selected.Value ? cfg.ExportSelectedCommand : cfg.ExportCommand;
                    if (!AutodeskIfcExtension.CommandDefined(command) && AutodeskIfcExtension.FindInstalled().Count == 0)
                    {
                        ed.WriteMessage($"\nMetroSP: {command} não existe neste Civil 3D. Instale a IFC 4.3 Extension da Autodesk.");
                        Ui.ExtensionWindow.Show(cfg, command, blocking: true);
                        return;
                    }
                }

                var dwgDir = DrawingFolder(doc);
                if (dwgDir == null && cfg.Destino != "ProgramData")
                    throw new InvalidOperationException("Salve o desenho antes: os JSON de configuração são gravados na pasta do DWG.");

                if (cfg.UsesForma)
                {
                    cfg.Validate();
                    ed.WriteMessage("\nMetroSP: buscando configuração IFC no Forma (o navegador pode abrir para login)...");
                    var log = new BufferedLog();
                    var n = RunBlocking(ct => SyncFromFormaAsync(cfg, log.Add, ct), log, ed);
                    ed.WriteMessage($"\nMetroSP: {n} arquivo(s) atualizados a partir do Forma.");
                }

                var written = ConfigPackage.ApplyToDrawing(dwgDir, Path.GetFileNameWithoutExtension(doc.Name), cfg.Destino, ProgramDataDir);
                ed.WriteMessage($"\nMetroSP: configuração IFC aplicada ({written.Count} arquivo(s)).");
                if (selected == null) return;

                var metro = ConfigPackage.LoadMetroSp();
                List<AlignmentModel> alignments = null;
                if (metro.Alinhamento.AplicarNoIfc)
                {
                    // Lê agora (pode perguntar o perfil); o IFC só existe depois do comando da Autodesk.
                    alignments = AlignmentCommands.ReadAll(doc, metro.Alinhamento, interactive: true);
                }

                var cmd = selected.Value ? cfg.ExportSelectedCommand : cfg.ExportCommand;
                _pending = new PendingExport
                {
                    Doc = doc,
                    Command = cmd,
                    StartUtc = DateTime.UtcNow,
                    DwgDir = dwgDir,
                    Alignments = alignments
                };
                doc.CommandEnded += OnCommandEnded;
                doc.CommandCancelled += OnCommandAborted;
                doc.CommandFailed += OnCommandAborted;
                doc.SendStringToExecute("_." + cmd + " ", true, false, true);
            }
            catch (System.Exception ex) { Fail(ed, ex); }
        }

        static void OnCommandEnded(object sender, CommandEventArgs e)
        {
            var doc = (AcDocument)sender;
            if (_pending == null || _pending.Doc != doc ||
                !string.Equals(e.GlobalCommandName, _pending.Command, StringComparison.OrdinalIgnoreCase)) return;
            Unhook(doc);
            doc.SendStringToExecute("_MSP_IFCPOSPROC ", true, false, true);
        }

        static void OnCommandAborted(object sender, CommandEventArgs e)
        {
            var doc = (AcDocument)sender;
            if (_pending == null || !string.Equals(e.GlobalCommandName, _pending.Command, StringComparison.OrdinalIgnoreCase)) return;
            Unhook(doc);
            _pending = null;
            doc.Editor.WriteMessage("\nMetroSP: exportação cancelada; IFC não pós-processado.");
        }

        static void Unhook(AcDocument doc)
        {
            doc.CommandEnded -= OnCommandEnded;
            doc.CommandCancelled -= OnCommandAborted;
            doc.CommandFailed -= OnCommandAborted;
        }

        /// <summary>IFC gravado depois do início da exportação, na pasta do desenho ou em Documentos.</summary>
        static string FindExportedIfc(PendingExport p)
        {
            var dirs = new[] { p.DwgDir, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) }
                .Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d));
            return dirs
                .SelectMany(d => Directory.EnumerateFiles(d, "*.ifc"))
                .Where(f => !f.EndsWith(".autodesk.ifc", StringComparison.OrdinalIgnoreCase))
                .Select(f => new FileInfo(f))
                .Where(f => f.LastWriteTimeUtc >= p.StartUtc.AddSeconds(-2))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => f.FullName)
                .FirstOrDefault();
        }

        /// <summary>Baixa do Forma para o pacote ativo os arquivos de configuração reconhecidos.</summary>
        static async Task<int> SyncFromFormaAsync(Settings cfg, Action<string> log, CancellationToken ct)
        {
            var client = new FormaClient(cfg, log);
            var remote = await client.ListFolderAsync(ct);

            var wanted = cfg.Files != null && cfg.Files.Count > 0
                ? remote.Where(r => cfg.Files.Any(f => string.Equals(f, r.Name, StringComparison.OrdinalIgnoreCase))).ToList()
                : remote.Where(r => r.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                                    r.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var m in (cfg.Files ?? new List<string>()).Where(f => !remote.Any(r => string.Equals(f, r.Name, StringComparison.OrdinalIgnoreCase))))
                log("aviso: \"" + m + "\" não existe na pasta do Forma.");
            if (wanted.Count == 0) throw new InvalidOperationException("Nenhum arquivo de configuração encontrado na pasta do Forma.");

            var origin = new StringBuilder();
            origin.AppendLine($"Configuração IFC baixada do Autodesk Forma em {DateTime.Now:yyyy-MM-dd HH:mm:ss} por {Environment.UserName}");
            origin.AppendLine($"Projeto: {cfg.ProjectId}  Pasta: {cfg.FolderUrn}");

            int n = 0;
            foreach (var f in wanted)
            {
                var kind = ConfigPackage.FileNames.FirstOrDefault(kv => string.Equals(kv.Value, f.Name, StringComparison.OrdinalIgnoreCase)).Key;
                if (kind == ConfigKind.Unknown)
                {
                    log($"aviso: {f.Name} não é um arquivo de configuração conhecido; ignorado.");
                    continue;
                }
                var data = await client.DownloadAsync(f, ct);
                ConfigPackage.Write(kind, data);
                n++;
                log($"{f.Name} v{f.VersionNumber} ({f.LastModifiedBy}, {f.LastModified})");
                origin.AppendLine($"{f.Name}\tv{f.VersionNumber}\t{f.LastModified}\t{f.LastModifiedBy}\t{f.VersionId}");
            }
            File.WriteAllText(Path.Combine(ConfigPackage.ActiveDir, OriginLog), origin.ToString(), Encoding.UTF8);
            return n;
        }

        // ------------------------------------------------------------------ utilitários

        internal static string ProgramDataDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Autodesk", "Civil3D");

        internal static string DrawingFolder(AcDocument doc)
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
            if (ex is OperationCanceledException) { ed.WriteMessage("\nMetroSP: " + ex.Message); return; }
            var log = Log.Erro("comando", ex);
            ed.WriteMessage("\nMetroSP: ERRO - " + ex.Message + (log != null ? $" (detalhes em {log})" : ""));
        }
    }
}
