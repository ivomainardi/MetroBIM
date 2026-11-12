using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Autodesk.Revit.UI;
using MetroSP.Conferencia;
using MetroSP.Ui;
using DB = Autodesk.Revit.DB;

namespace MetroSP.Painel
{
    /// <summary>Ações que resolvem uma pendência, usadas pelo Painel MetroSP e pela janela da entrega. Contexto da API.</summary>
    internal static class AcoesPendencia
    {
        internal static void Dono(Window janela, Window dono, IntPtr donoRevit)
        {
            if (dono != null) janela.Owner = dono;
            else new WindowInteropHelper(janela).Owner = donoRevit;
        }

        /// <summary>Acrescenta a categoria no vínculo do parâmetro de projeto. Contexto da API.</summary>
        internal static bool VincularCategoria(DB.Document doc, string parametro, string categoria)
        {
            DB.Definition def = null;
            DB.ElementBinding vinculo = null;
            DB.DefinitionBindingMapIterator it = doc.ParameterBindings.ForwardIterator();
            while (it.MoveNext())
            {
                if (it.Key != null && string.Equals(it.Key.Name, parametro, StringComparison.OrdinalIgnoreCase))
                {
                    def = it.Key;
                    vinculo = it.Current as DB.ElementBinding;
                    break;
                }
            }
            if (def == null || vinculo == null)
            {
                TaskDialog.Show("MetroSP", $"\"{parametro}\" não é um parâmetro de projeto deste modelo, então não dá para vincular daqui.\n\n" +
                    "Se é um parâmetro interno do Revit, os elementos dessa categoria não têm esse nome: use \"Ajustar o .txt\" para apontar o campo " +
                    "para o parâmetro certo. Se é do MetroShared, crie em Parâmetros › Parâmetros de projeto. Se vem da família, carregue a família com ele.");
                return false;
            }
            DB.Category cat = doc.Settings.Categories.Cast<DB.Category>().FirstOrDefault(c => string.Equals(c.Name, categoria, StringComparison.OrdinalIgnoreCase));
            if (cat == null || !cat.AllowsBoundParameters)
            {
                TaskDialog.Show("MetroSP", $"A categoria {categoria} não aceita parâmetros de projeto.");
                return false;
            }
            if (vinculo.Categories.Contains(cat))
            {
                TaskDialog.Show("MetroSP", $"\"{parametro}\" já está vinculado a {categoria}. Os elementos sem ele devem vir de famílias que têm um parâmetro de família com o mesmo nome.");
                return false;
            }
            using (var t = new DB.Transaction(doc, $"MetroSP - Vincular {parametro} a {categoria}"))
            {
                t.Start();
                vinculo.Categories.Insert(cat);
                if (!doc.ParameterBindings.ReInsert(def, vinculo))
                {
                    t.RollBack();
                    TaskDialog.Show("MetroSP", $"O Revit não aceitou vincular \"{parametro}\" a {categoria}.");
                    return false;
                }
                t.Commit();
            }
            return true;
        }

        internal static bool EditarPropertySets(DB.Document doc, PacoteMetro pacote, Window dono, IntPtr donoRevit)
        {
            Ids.EditarPsetsWindow editor;
            try
            {
                var modelo = new Ids.ModeloIfc(doc);
                editor = new Ids.EditarPsetsWindow(pacote.PropertySets, modelo.Parametros, pacote.PropertySets, podeRestaurar: false, modelo: modelo);
            }
            catch (Exception ex) { TaskDialog.Show("MetroSP", "Não deu para abrir o .txt de property sets:\n" + ex.Message); return false; }
            Dono(editor, dono, donoRevit);
            return editor.ShowDialog() == true;
        }

        /// <summary>Escolher a classe IFC dos elementos da pendência e gravar no "Exportar para IFC como" (tipo, ou instância se já tiver valor). Contexto da API.</summary>
        internal static bool EscolherClasse(DB.Document doc, ResultadoConferencia res, Pendencia p, Window dono, IntPtr donoRevit, Action conferir)
        {
            var doPadrao = res.ClassesDoPadrao.Select(c => Ids.EsquemaIfc.ClasseComTipo(res.Esquema, c)).Where(c => c != null).Distinct().ToList();
            var lista = doPadrao.Concat(Ids.EsquemaIfc.ClassesComTipo(res.Esquema).Except(doPadrao)).ToList();
            var caixa = Ui.SeletorLista.Criar(lista, 320);
            var ok = new Button { Content = "Gravar", Style = Theme.PrimaryButton, IsDefault = true, Margin = new Thickness(8, 0, 0, 0) };
            var cancelar = new Button { Content = "Cancelar", IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
            var corpo = new StackPanel { Margin = new Thickness(16) };
            corpo.Children.Add(new TextBlock { Text = $"{p.Categoria}: {p.Elementos.Count} elemento(s) hoje em {p.Assunto}.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            corpo.Children.Add(Theme.Detalhe($"Primeiro as classes dos padrões ({doPadrao.Count}), depois todas as do {Ids.EsquemaIfc.Nome(res.Esquema)}. Digite parte do nome para filtrar."));
            var linha = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            linha.Children.Add(caixa);
            linha.Children.Add(ok);
            linha.Children.Add(cancelar);
            corpo.Children.Add(linha);
            var janela = new Window
            {
                Title = "MetroSP - Classe IFC", Content = corpo, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.CanResizeWithGrip,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brushes.White,
            };
            Theme.Estilos(janela);
            ok.Click += (s, e) => janela.DialogResult = true;
            Dono(janela, dono, donoRevit);
            if (janela.ShowDialog() != true) return false;
            string classe = Ids.EsquemaIfc.ClasseComTipo(res.Esquema, (caixa.SelectedItem as string ?? caixa.Text ?? "").Trim());
            if (classe == null)
            {
                TaskDialog.Show("MetroSP", "Escolha uma classe IFC da lista.");
                return false;
            }

            int tipos = 0, instancias = 0, falhas = 0;
            var feitos = new HashSet<DB.ElementId>();
            using (var t = new DB.Transaction(doc, "MetroSP - Definir classe IFC"))
            {
                t.Start();
                foreach (DB.ElementId id in p.Elementos)
                {
                    DB.Element e = doc.GetElement(id);
                    if (e == null) continue;
                    DB.Parameter inst = Compat.ExportarComo(e, false);
                    DB.Element tipo = doc.GetElement(e.GetTypeId());
                    DB.Parameter doTipo = Compat.ExportarComo(tipo, true);
                    bool instanciaTemValor = inst != null && !string.IsNullOrWhiteSpace(inst.AsString());
                    if (!instanciaTemValor && doTipo != null && !doTipo.IsReadOnly)
                    {
                        if (!feitos.Add(tipo.Id)) continue;
                        if (doTipo.Set(classe)) tipos++; else falhas++;
                    }
                    else if (inst != null && !inst.IsReadOnly)
                    {
                        if (inst.Set(classe)) instancias++; else falhas++;
                    }
                    else falhas++;
                }
                if (tipos + instancias > 0) t.Commit(); else t.RollBack();
            }
            conferir();
            if (falhas > 0)
                TaskDialog.Show("MetroSP", $"Classe gravada em {tipos} tipo(s) e {instancias} instância(s).\n{falhas} elemento(s) não aceitam o \"Exportar para IFC como\" (use Categorias IFC para a categoria inteira).");
            return tipos + instancias > 0;
        }

        /// <summary>
        /// Verde quando o Location do Revit está no Project Base Point e o IfcProject, o IfcSite e o IfcBuilding têm nome.
        /// Chamar no contexto da API.
        /// </summary>
        internal static (Sinal, string) SituacaoLocal(DB.Document doc)
        {
            Ids.LocalizacaoIfc l;
            try { l = Ids.LocalizacaoIfc.Calcular(doc); }
            catch (Exception ex) { return (Sinal.Amarelo, "Não foi possível ler a localização: " + ex.Message); }
            if (l.Erro != null) return (Sinal.Vermelho, "Não dá para localizar: " + l.Erro);
            var faltam = new List<string>();
            if (string.IsNullOrWhiteSpace(l.DadosAtuais.Projeto.Nome)) faltam.Add("nome do IfcProject");
            if (string.IsNullOrWhiteSpace(l.DadosAtuais.Terreno.Nome)) faltam.Add("nome do IfcSite");
            if (string.IsNullOrWhiteSpace(l.DadosAtuais.Edificio.Nome)) faltam.Add("nome do IfcBuilding");
            if (!l.LocationCorreto)
                return (Sinal.Amarelo, $"O Location do Revit está a {l.Diferenca:0.##} m do Project Base Point. Abra e confirme." +
                                       (faltam.Count > 0 ? " Falta também: " + string.Join(", ", faltam) + "." : ""));
            if (faltam.Count > 0) return (Sinal.Amarelo, "Location certo. Falta: " + string.Join(", ", faltam) + ".");
            return (Sinal.Verde, "Location no Project Base Point; IfcProject, IfcSite e IfcBuilding preenchidos.");
        }

    }
}
