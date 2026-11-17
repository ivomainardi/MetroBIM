// Aviso de versão nova do MetroSP no Navisworks: ao abrir o Navisworks, consulta (no máximo uma vez por dia, em
// segundo plano) se saiu versão nova do instalador único e pergunta se quer baixar. Quieto sem internet ou sem o
// instalador. A consulta é a mesma do Revit e do Civil 3D (comum/VersaoNova.cs).
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Autodesk.Navisworks.Api.Plugins;
using MetroSP.Comum;

namespace IrisExport
{
    // EventWatcherPlugin: o Navisworks carrega ao abrir, sem botão na Ribbon.
    [Plugin("MetroSPAvisoVersao", "IRIS", DisplayName = "MetroSP: aviso de versão nova")]
    public class AvisoVersao : EventWatcherPlugin
    {
        public override void OnLoaded()
        {
            try
            {
                var interfaceGrafica = SynchronizationContext.Current;
                Task.Run(async () =>
                {
                    var n = await VersaoNova.ProcurarAsync().ConfigureAwait(false);
                    if (!VersaoNova.TemNova(n)) return;
                    if (interfaceGrafica != null) interfaceGrafica.Post(_ => Perguntar(n), null);
                    else Perguntar(n);
                });
            }
            catch { }
        }

        public override void OnUnloading() { }

        static void Perguntar(VersaoNova.Novidade n)
        {
            try
            {
                var texto = $"Saiu a versão {n.Nova} do MetroSP (instalada: {n.Instalada}).\n\n" +
                            "Quer baixar o instalador agora? Feche o Navisworks antes de rodar o instalador.\n\n" +
                            "Sim: baixa o instalador.   Não: avisa de novo amanhã.   Cancelar: não avisa mais desta versão.";
                var r = MessageBox.Show(texto, "MetroSP - Versão nova", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);
                if (r == DialogResult.Yes) VersaoNova.Abrir(n.Instalador);
                else if (r == DialogResult.Cancel) VersaoNova.Ignorar(n.Nova);
            }
            catch { }
        }
    }
}
