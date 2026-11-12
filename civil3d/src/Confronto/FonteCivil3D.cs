using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DataType = Autodesk.Aec.PropertyData.DataType;
using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using MetroSP.Ifc;
using MetroSP.Ui;
using AcDocument = Autodesk.AutoCAD.ApplicationServices.Document;
using CivilEntity = Autodesk.Civil.DatabaseServices.Entity;

namespace MetroSP.Confronto
{
    /// <summary>
    /// Confrontar com planilha no Civil 3D: objetos do espaço do modelo que vão para o IFC (FiltroIfc), com as
    /// propriedades dos property sets (que viram Psets no IFC), o nome, a descrição e o layer.
    /// Os valores são lidos uma vez por escopo, numa transação só.
    /// </summary>
    public class FonteCivil3D : IFonteConfronto
    {
        private const string Handle = "@handle", Nome = "@nome", Descricao = "@descricao", Layer = "@layer", Classe = "@classe";
        private const string Todos = "Todos os tipos";

        private class Valor
        {
            public string Texto = "";
            public bool Editavel;
            public string Motivo;
            public ObjectId Pset = ObjectId.Null;
            public int Prop = -1;
            public DataType Tipo = DataType.Text;
        }

        private class Dados
        {
            public ObjectId Id;
            public readonly Dictionary<string, Valor> Valores = new Dictionary<string, Valor>();
        }

        private readonly AcDocument _doc;
        private readonly ObjectId[] _selecao;
        private readonly ComboBox _escopo;
        private readonly ComboBox _tipo;
        private List<ObjetoConfronto> _todos;
        private Dictionary<string, CampoConfronto> _campos = new Dictionary<string, CampoConfronto>();
        private int _fora;
        private bool _montando;

        public FonteCivil3D(AcDocument doc, ObjectId[] selecao)
        {
            _doc = doc;
            _selecao = selecao != null && selecao.Length > 0 ? selecao : null;
            _escopo = new ComboBox { MinWidth = 200, Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(4, 3, 4, 3) };
            if (_selecao != null) _escopo.Items.Add($"Seleção atual ({_selecao.Length})");
            _escopo.Items.Add("Todo o desenho");
            _escopo.SelectedIndex = 0;
            _tipo = new ComboBox { MinWidth = 200, Padding = new Thickness(4, 3, 4, 3), ToolTip = "Tipo de objeto a considerar." };
            var p = new WrapPanel();
            p.Children.Add(Theme.Rotulo("Escopo"));
            p.Children.Add(_escopo);
            p.Children.Add(_tipo);
            PainelEscopo = p;
            _escopo.SelectionChanged += (s, e) => { _todos = null; EscopoMudou?.Invoke(); };
            _tipo.SelectionChanged += (s, e) => { if (!_montando) EscopoMudou?.Invoke(); };
        }

        public string Programa => "Civil3D";
        public string NomeDocumento => System.IO.Path.GetFileNameWithoutExtension(_doc.Name);
        public string Termo => "objeto";
        public FrameworkElement PainelEscopo { get; }
        public event Action EscopoMudou;

        // ------------------------------------------------------------------ leitura

        private void Carregar()
        {
            if (_todos != null) return;
            _todos = new List<ObjetoConfronto>();
            _campos = new Dictionary<string, CampoConfronto>
            {
                [Handle] = new CampoConfronto { Chave = Handle, Nome = "Handle", Grupo = "Desenho" },
                [Nome] = new CampoConfronto { Chave = Nome, Nome = "Nome", Grupo = "Desenho", Editavel = true, Texto = true },
                [Descricao] = new CampoConfronto { Chave = Descricao, Nome = "Descrição", Grupo = "Desenho", Editavel = true, Texto = true },
                [Layer] = new CampoConfronto { Chave = Layer, Nome = "Layer", Grupo = "Desenho", Editavel = true },
                [Classe] = new CampoConfronto { Chave = Classe, Nome = "Tipo de objeto", Grupo = "Desenho" },
            };
            Database db = _doc.Database;
            bool soSelecao = (_escopo.SelectedItem as string ?? "").StartsWith("Seleção");
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                ObjectId modelo = SymbolUtilityServices.GetBlockModelSpaceId(db);
                List<ObjectId> todos = soSelecao ? _selecao.ToList()
                    : ((BlockTableRecord)tr.GetObject(modelo, OpenMode.ForRead)).Cast<ObjectId>().ToList();
                List<ObjectId> ids = FiltroIfc.Objetos(db, tr, todos);
                _fora = todos.Count - ids.Count;
                foreach (ObjectId id in ids)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                    var d = new Dados { Id = id };
                    string classe = NomeClasse(e);
                    d.Valores[Handle] = new Valor { Texto = e.Handle.ToString(), Motivo = "o handle é do AutoCAD" };
                    d.Valores[Classe] = new Valor { Texto = classe, Motivo = "tipo do objeto" };
                    d.Valores[Layer] = new Valor { Texto = e.Layer, Editavel = true };
                    string nome = null;
                    if (e is CivilEntity ce)
                    {
                        try { nome = ce.Name; d.Valores[Nome] = new Valor { Texto = nome ?? "", Editavel = true }; } catch { /* sem nome */ }
                        try { d.Valores[Descricao] = new Valor { Texto = ce.Description ?? "", Editavel = true }; } catch { /* sem descrição */ }
                    }
                    else if (e is BlockReference br)
                    {
                        nome = FiltroIfc.NomeBloco(br, tr);
                        d.Valores[Nome] = new Valor { Texto = nome, Motivo = "nome do bloco (renomear no BLOCK)" };
                    }
                    LerPropertySets(e, tr, d);
                    _todos.Add(new ObjetoConfronto
                    {
                        Ref = d,
                        Id = e.Handle.ToString(),
                        Nome = string.IsNullOrEmpty(nome) ? $"{classe} {e.Handle}" : nome,
                        Categoria = classe,
                    });
                }
                tr.Commit();
            }

            object atual = _tipo.SelectedItem;
            _montando = true;
            _tipo.Items.Clear();
            _tipo.Items.Add(Todos);
            foreach (var g in _todos.GroupBy(o => o.Categoria).OrderBy(g => g.Key)) _tipo.Items.Add(g.Key);
            _tipo.SelectedItem = atual != null && _tipo.Items.Contains(atual) ? atual : Todos;
            _montando = false;
        }

        private void LerPropertySets(Entity e, Transaction tr, Dados d)
        {
            ObjectIdCollection sets;
            try { sets = PropertyDataServices.GetPropertySets(e); }
            catch { return; } // objeto sem suporte a property sets
            foreach (ObjectId psId in sets)
            {
                try
                {
                    var ps = (PropertySet)tr.GetObject(psId, OpenMode.ForRead);
                    var psd = (PropertySetDefinition)tr.GetObject(ps.PropertySetDefinition, OpenMode.ForRead);
                    foreach (PropertyDefinition pd in psd.Definitions)
                    {
                        string chave = "ps:" + psd.Name + "|" + pd.Name;
                        int prop = ps.PropertyNameToId(pd.Name);
                        object v = null;
                        try { v = ps.GetAt(prop); } catch { /* sem valor */ }
                        bool formula = pd is PropertyDefinitionFormula;
                        bool editavel = !pd.Automatic && !formula && !pd.IsReadOnly && pd.DataType != DataType.Graphic;
                        d.Valores[chave] = new Valor
                        {
                            Texto = Texto(v),
                            Editavel = editavel,
                            Motivo = editavel ? null : formula ? "fórmula do property set" : pd.Automatic ? "propriedade automática (vem do objeto)" : "somente leitura",
                            Pset = psId,
                            Prop = prop,
                            Tipo = pd.DataType,
                        };
                        if (!_campos.TryGetValue(chave, out CampoConfronto c))
                            _campos[chave] = c = new CampoConfronto { Chave = chave, Nome = pd.Name, Grupo = psd.Name, Detalhe = pd.DataType.ToString(), Texto = pd.DataType == DataType.Text };
                        if (editavel) c.Editavel = true;
                    }
                }
                catch { /* property set ilegível */ }
            }
        }

        private static string Texto(object v)
        {
            switch (v)
            {
                case null: return "";
                case bool b: return b ? "Sim" : "Não";
                case double d: return d.ToString("0.##########", CultureInfo.CurrentCulture);
                case float f: return ((double)f).ToString("0.##########", CultureInfo.CurrentCulture);
                default: return Convert.ToString(v, CultureInfo.CurrentCulture) ?? "";
            }
        }

        private static readonly Dictionary<string, string> Classes = new Dictionary<string, string>
        {
            ["Alignment"] = "Alinhamento", ["Profile"] = "Perfil", ["Corridor"] = "Corredor", ["TinSurface"] = "Superfície TIN",
            ["GridSurface"] = "Superfície de grade", ["FeatureLine"] = "Linha característica", ["Pipe"] = "Tubo", ["Structure"] = "Estrutura",
            ["PressurePipe"] = "Tubo de pressão", ["PressureFitting"] = "Conexão de pressão", ["PressureAppurtenance"] = "Acessório de pressão",
            ["CogoPoint"] = "Ponto COGO", ["Parcel"] = "Lote", ["Solid3d"] = "Sólido 3D", ["BlockReference"] = "Bloco",
            ["Polyline3d"] = "Polilinha 3D", ["Polyline"] = "Polilinha", ["Line"] = "Linha", ["SubassemblyBase"] = "Subconjunto",
            ["Assembly"] = "Conjunto", ["Catchment"] = "Bacia", ["SurfaceMesh"] = "Malha", ["PolyFaceMesh"] = "Malha", ["SubDMesh"] = "Malha",
        };

        private static string NomeClasse(Entity e)
        {
            string n = e.GetType().Name;
            if (e is BlockReference) n = "BlockReference";
            return Classes.TryGetValue(n, out string pt) ? pt : n;
        }

        private static Dados D(ObjetoConfronto o) => (Dados)o.Ref;

        public IList<ObjetoConfronto> Objetos()
        {
            Carregar();
            string tipo = _tipo.SelectedItem as string;
            return tipo == null || tipo == Todos ? _todos.ToList() : _todos.Where(o => o.Categoria == tipo).ToList();
        }

        public IList<CampoConfronto> Campos()
        {
            Carregar();
            var usados = new HashSet<string>(Objetos().SelectMany(o => D(o).Valores.Keys));
            return _campos.Values.Where(c => c.Grupo == "Desenho" || usados.Contains(c.Chave))
                .OrderBy(c => c.Grupo == "Desenho" ? 0 : 1).ThenBy(c => c.Grupo).ThenBy(c => c.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>Quantos objetos do escopo ficaram de fora por não irem para o IFC.</summary>
        public int ForaDoIfc { get { Carregar(); return _fora; } }

        public ValorConfronto Ler(ObjetoConfronto o, CampoConfronto c)
        {
            if (!D(o).Valores.TryGetValue(c.Chave, out Valor v))
                return new ValorConfronto { Motivo = c.Chave.StartsWith("ps:") ? $"Anexar o property set \"{c.Grupo}\" a este objeto" : "Este tipo de objeto não tem este dado" };
            return new ValorConfronto { Existe = true, Texto = v.Texto, Editavel = v.Editavel, Motivo = v.Motivo };
        }

        // ------------------------------------------------------------------ comparação e gravação

        private static bool SimNao(string s, out bool b)
        {
            string t = (s ?? "").Trim().ToLowerInvariant();
            b = new[] { "sim", "s", "yes", "y", "1", "true", "verdadeiro", "x" }.Contains(t);
            return b || new[] { "não", "nao", "n", "no", "0", "false", "falso" }.Contains(t);
        }

        public bool Iguais(ObjetoConfronto o, CampoConfronto c, string modelo, string planilha)
        {
            if (!D(o).Valores.TryGetValue(c.Chave, out Valor v)) return false;
            switch (v.Tipo)
            {
                case DataType.Real:
                case DataType.Integer:
                case DataType.AutoIncrement:
                    return Confrontador.Numero(modelo, out double a) && Confrontador.Numero(planilha, out double b) && Math.Abs(a - b) <= 1e-6 * Math.Max(1, Math.Abs(a));
                case DataType.TrueFalse:
                    return SimNao(modelo, out bool x) && SimNao(planilha, out bool y) && x == y;
                default:
                    return false;
            }
        }

        public bool Preparar(ObjetoConfronto o, CampoConfronto c, string planilha, out object valor, out string motivo)
        {
            valor = null;
            motivo = null;
            string t = (planilha ?? "").Trim();
            if (!D(o).Valores.TryGetValue(c.Chave, out Valor v)) { motivo = "o objeto não tem este dado"; return false; }
            if (c.Chave == Layer)
            {
                if (t.Length == 0) { motivo = "layer vazio"; return false; }
                using (Transaction tr = _doc.Database.TransactionManager.StartOpenCloseTransaction())
                {
                    var lt = (LayerTable)tr.GetObject(_doc.Database.LayerTableId, OpenMode.ForRead);
                    if (!lt.Has(t)) { motivo = $"o layer \"{t}\" não existe no desenho"; return false; }
                }
                valor = t;
                return true;
            }
            if (c.Chave == Nome && t.Length == 0) { motivo = "nome vazio"; return false; }
            switch (v.Tipo)
            {
                case DataType.Integer:
                    if (Confrontador.Numero(t, out double n) && Math.Abs(n - Math.Round(n)) < 1e-9) { valor = (int)Math.Round(n); return true; }
                    motivo = "não é um número inteiro";
                    return false;
                case DataType.Real:
                    if (Confrontador.Numero(t, out double r)) { valor = r; return true; }
                    motivo = "não é um número";
                    return false;
                case DataType.TrueFalse:
                    if (SimNao(t, out bool b)) { valor = b; return true; }
                    motivo = "não é Sim/Não";
                    return false;
                default:
                    valor = planilha ?? "";
                    return true;
            }
        }

        public (int Ok, int Falhas) Gravar(IList<Gravacao> gravacoes)
        {
            int ok = 0, falhas = 0;
            using (_doc.LockDocument())
            using (Transaction tr = _doc.Database.TransactionManager.StartTransaction())
            {
                foreach (Gravacao g in gravacoes)
                {
                    Dados d = D(g.Objeto);
                    try
                    {
                        Valor v = d.Valores[g.Campo.Chave];
                        if (v.Pset != ObjectId.Null)
                        {
                            var ps = (PropertySet)tr.GetObject(v.Pset, OpenMode.ForWrite);
                            switch (g.Valor)
                            {
                                case int i: ps.SetAt(v.Prop, i); break;
                                case double x: ps.SetAt(v.Prop, x); break;
                                case bool b: ps.SetAt(v.Prop, b); break;
                                default: ps.SetAt(v.Prop, Convert.ToString(g.Valor, CultureInfo.CurrentCulture)); break;
                            }
                        }
                        else
                        {
                            var e = (Entity)tr.GetObject(d.Id, OpenMode.ForWrite);
                            if (g.Campo.Chave == Layer) e.Layer = (string)g.Valor;
                            else if (g.Campo.Chave == Nome) ((CivilEntity)e).Name = (string)g.Valor;
                            else if (g.Campo.Chave == Descricao) ((CivilEntity)e).Description = (string)g.Valor;
                            else throw new InvalidOperationException("campo não editável");
                        }
                        v.Texto = g.Valor is bool bb ? (bb ? "Sim" : "Não") : Texto(g.Valor);
                        g.Ok = true;
                        ok++;
                    }
                    catch { falhas++; } // objeto bloqueado, nome repetido, layer travado...
                }
                tr.Commit();
            }
            return (ok, falhas);
        }

        public void Selecionar(IEnumerable<ObjetoConfronto> objetos, bool mostrar)
        {
            ObjectId[] ids = objetos.Select(o => D(o).Id).Where(id => !id.IsErased).Distinct().ToArray();
            Editor ed = _doc.Editor;
            using (_doc.LockDocument())
            {
                ed.SetImpliedSelection(ids);
                if (mostrar && ids.Length > 0) Zoom(ed, ids);
                ed.UpdateScreen();
            }
        }

        /// <summary>Aproxima a vista dos objetos (vista em planta).</summary>
        private void Zoom(Editor ed, ObjectId[] ids)
        {
            var ext = new Extents3d();
            bool tem = false;
            using (Transaction tr = _doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (ObjectId id in ids)
                {
                    try
                    {
                        Extents3d e = ((Entity)tr.GetObject(id, OpenMode.ForRead)).GeometricExtents;
                        if (tem) ext.AddExtents(e); else { ext = e; tem = true; }
                    }
                    catch { /* sem extensão */ }
                }
                tr.Commit();
            }
            if (!tem) return;
            using (ViewTableRecord vista = ed.GetCurrentView())
            {
                double w = Math.Max(ext.MaxPoint.X - ext.MinPoint.X, 1) * 1.3;
                double h = Math.Max(ext.MaxPoint.Y - ext.MinPoint.Y, 1) * 1.3;
                double razao = vista.Width / Math.Max(vista.Height, 1e-9);
                if (w / h > razao) h = w / razao; else w = h * razao;
                vista.CenterPoint = new Point2d((ext.MinPoint.X + ext.MaxPoint.X) / 2, (ext.MinPoint.Y + ext.MaxPoint.Y) / 2);
                vista.Width = w;
                vista.Height = h;
                vista.ViewDirection = Vector3d.ZAxis;
                vista.Target = Point3d.Origin;
                ed.SetCurrentView(vista);
            }
        }
    }
}
