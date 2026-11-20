using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using MetroSP.Ifc;

namespace MetroSP.Ids
{
    /// <summary>
    /// Parafusos, chumbadores, conectores, chapas e soldas das ligações metálicas como elementos próprios no IFC4/4.3.
    /// <para>
    /// O exportador da Autodesk só separa essas peças no IFC 2x3 (com "Incluir elementos de aço", pelo módulo do
    /// Advance Steel). No IFC4 a ligação sai como um corpo só e os elementos de fabricação avulsos saem sem geometria.
    /// Aqui, dentro da transação da exportação (que é desfeita no fim), cada peça vira uma forma direta temporária com a
    /// geometria dela e "Exportar para IFC como" a classe certa (IfcMechanicalFastener.BOLT etc.). O exportador cuida da
    /// posição e da representação; depois o <see cref="FixadoresIfc"/> grava os dados e agrupa as peças na ligação.
    /// </para>
    /// </summary>
    internal sealed class AcoIfc
    {
        /// <summary>Forma temporária (ElementId) → dados da peça, para o pós-processamento.</summary>
        public Dictionary<long, PecaAco> Pecas { get; } = new Dictionary<long, PecaAco>();
        /// <summary>Ligações e elementos avulsos encontrados.</summary>
        public int Origens { get; private set; }
        /// <summary>O que não deu certo (para o resumo), ou vazio.</summary>
        public List<string> Avisos { get; } = new List<string>();

        private readonly List<ElementId> _criadas = new List<ElementId>();
        /// <summary>Forma temporária → origem, sólidos e classe, para recriar como Modelo genérico se a vista não a mostrar.</summary>
        private readonly Dictionary<ElementId, (Element Origem, List<Solid> Solidos, string Classe)> _formas =
            new Dictionary<ElementId, (Element, List<Solid>, string)>();
        private readonly List<(Element Elemento, int Valor)> _exportarOriginal = new List<(Element, int)>();
        private View _vista;
        /// <summary>Vista 3D usada para ler a geometria (as ligações detalhadas só mostram as peças numa vista).</summary>
        private View3D _vista3d;
        private int _diagnosticos;
        private readonly List<ElementId> _categoriasMostradas = new List<ElementId>();

        private static readonly Dictionary<BuiltInCategory, (string Peca, string Classe, bool Parafuso, bool Separar)> Categorias =
            new Dictionary<BuiltInCategory, (string, string, bool, bool)>
            {
                { BuiltInCategory.OST_StructConnectionBolts, ("Parafuso", "IfcMechanicalFastener.BOLT", true, true) },
                { BuiltInCategory.OST_StructConnectionAnchors, ("Chumbador", "IfcMechanicalFastener.ANCHORBOLT", true, true) },
                { BuiltInCategory.OST_StructConnectionShearStuds, ("Conector de cisalhamento", "IfcMechanicalFastener.SHEARCONNECTOR", false, true) },
                { BuiltInCategory.OST_StructConnectionPlates, ("Chapa", "IfcPlate", false, false) },
                { BuiltInCategory.OST_StructConnectionWelds, ("Solda", "IfcFastener.WELD", false, false) },
                { BuiltInCategory.OST_StructConnectionProfiles, ("Perfil", "IfcMember", false, false) },
            };

        /// <summary>Valores do "Exportar para IFC" (IFCExportElement: 0 pelo tipo, 1 sim, 2 não).</summary>
        private const int ExportarSim = 1, ExportarNao = 2;

        /// <summary>Parâmetro "Exportar para IFC" da instância. No Revit 2022 ele ainda não é parâmetro interno: fica sem.</summary>
        private static Parameter ExportarParaIfc(Element e) =>
#if REVIT_ANTES_2023
            null;
#else
            e.get_Parameter(BuiltInParameter.IFC_EXPORT_ELEMENT);
#endif

        /// <summary>true se o modelo tem ligações metálicas ou elementos de fabricação de aço (consulta rápida).</summary>
        public static bool TemAco(Document doc)
        {
            if (new FilteredElementCollector(doc).OfClass(typeof(StructuralConnectionHandler)).FirstElementId() != null) return true;
            return new FilteredElementCollector(doc).WherePasses(new ElementMulticategoryFilter(Categorias.Keys.ToList()))
                .WhereElementIsNotElementType().FirstElementId() != null;
        }

        /// <summary>
        /// Cria as formas temporárias. Chamar dentro da transação da exportação, antes do exportador.
        /// vista: vista que filtra a exportação (ou InvalidElementId). fase: fase exportada.
        /// </summary>
        public static AcoIfc Preparar(Document doc, ElementId vistaId, ElementId fase)
        {
            var r = new AcoIfc();
            bool porVista = vistaId != null && vistaId != ElementId.InvalidElementId && doc.GetElement(vistaId) is View;
            if (porVista) r._vista = (View)doc.GetElement(vistaId);
            r._vista3d = Vista3D(doc, vistaId);
            FilteredElementCollector Coletor() => porVista ? new FilteredElementCollector(doc, vistaId) : new FilteredElementCollector(doc);

            var origens = new List<Element>();
            origens.AddRange(Coletor().OfClass(typeof(StructuralConnectionHandler)).ToElements());
            origens.AddRange(Coletor().WherePasses(new ElementMulticategoryFilter(Categorias.Keys.ToList()))
                .WhereElementIsNotElementType().ToElements().Where(e => !(e is StructuralConnectionHandler)));
            int semGeometria = 0, falhas = 0;
            foreach (Element origem in origens)
            {
                if (FiltroIfc.MotivoFora(doc, origem, fase) is string motivo && motivo != "Sem geometria 3D") continue;
                r.Origens++;
                try
                {
                    int n = r.Separar(doc, origem);
                    if (n == 0) semGeometria++;
                }
                catch (Exception ex)
                {
                    falhas++;
                    if (falhas <= 3) r.Avisos.Add($"Ligações metálicas: não deu para separar o elemento {Compat.Numero(origem.Id)} ({ex.Message}).");
                }
            }
            if (semGeometria > 0)
                r.Avisos.Add($"Ligações metálicas: {semGeometria} ligação(ões) ou peça(s) sem geometria legível pela API; saíram como o exportador do Revit faz.");
            if (r._vista != null) r.MostrarCategorias(doc);
            if (r._criadas.Count > 0) r.ConferirNaVista(doc);
            return r;
        }

        /// <summary>Separa as peças de uma ligação (ou de um elemento de fabricação avulso). Devolve quantas formas criou.</summary>
        private int Separar(Document doc, Element origem)
        {
            bool ehLigacao = origem is StructuralConnectionHandler;
            string ligacao = ehLigacao ? doc.GetElement(origem.GetTypeId())?.Name : null;

            // Cada subelemento é uma peça (ou um grupo de parafusos). Um elemento de fabricação avulso pode não ter
            // subelementos: aí ele próprio é a peça.
            var itens = new List<(string Chave, Category Categoria, List<Solid> Geo, Subelement Sub)>();
            IList<Subelement> subs = null;
            try { subs = origem.GetSubelements(); } catch { /* elemento sem subelementos */ }
            if (subs != null)
                foreach (Subelement s in subs)
                {
                    GeometryObject g = null;
                    if (_vista3d != null) try { g = s.GetGeometryObject(_vista3d); } catch { }
                    if (Solidos(g).Count == 0) try { g = s.GetGeometryObject(null); } catch { }
                    itens.Add((s.UniqueId, s.Category, Solidos(g), s));
                }
            if (!ehLigacao && itens.All(i => i.Categoria == null || !Categorias.ContainsKey(i.Categoria.BuiltInCategory)))
            {
                itens.Clear();
                itens.Add((origem.UniqueId, origem.Category, Solidos(Geometria(origem, _vista3d)), null));
            }
            // Ligação cujos subelementos não devolvem geometria: as peças vêm do corpo da ligação, separadas pela categoria
            // (subcategoria) de cada sólido ou, sem ela, pela forma (chapa fina, parafuso pequeno).
            if (ehLigacao && itens.All(i => i.Geo.Count == 0))
            {
                List<Solid> todos = Solidos(Geometria(origem, _vista3d));
                var subPorCategoria = itens.Where(i => i.Categoria != null).GroupBy(i => i.Categoria.BuiltInCategory)
                    .ToDictionary(g => g.Key, g => g.First().Sub);
                itens.Clear();
                foreach (var g in ClassificarVolumes(doc, todos).GroupBy(x => x.Categoria, x => x.Volume))
                {
                    Category cat = Category.GetCategory(doc, g.Key);
                    subPorCategoria.TryGetValue(g.Key, out Subelement sub);
                    itens.Add((origem.UniqueId + "|" + ((int)g.Key).ToString(CultureInfo.InvariantCulture), cat, g.ToList(), sub));
                }
            }
            if (itens.All(i => i.Geo.Count == 0) && _diagnosticos < 3)
            {
                _diagnosticos++;
                Avisos.Add("Ligações metálicas, diagnóstico: " + Diagnostico(doc, origem, subs));
            }

            int criadas = 0, previstas = 0, separadas = 0;
            var novas = new List<PecaAco>();
            foreach (var item in itens)
            {
                if (item.Categoria == null || !Categorias.TryGetValue(item.Categoria.BuiltInCategory, out var info)) continue;
                List<Solid> solidos = item.Geo;
                if (solidos.Count == 0) continue;
                previstas++;

                Dados dados = item.Sub != null ? LerDados(doc, item.Sub) : LerDados(doc, origem);
                // Grupo de parafusos: cada conjunto de sólidos que se tocam (parafuso, porca, arruelas) é um parafuso.
                List<List<Solid>> pecas = info.Separar ? Agrupar(solidos) : new List<List<Solid>> { solidos };
                int antes = criadas;
                for (int i = 0; i < pecas.Count; i++)
                {
                    DirectShape ds = CriarForma(doc, origem, item.Categoria, pecas[i], info.Classe);
                    if (ds == null) continue;
                    var p = new PecaAco
                    {
                        HostId = Compat.Numero(origem.Id),
                        Chave = item.Chave + "|" + i.ToString(CultureInfo.InvariantCulture),
                        Peca = info.Peca,
                        Ligacao = ligacao,
                        Norma = dados.Norma,
                        Classe = dados.Classe,
                        Diametro = dados.Diametro,
                        Comprimento = dados.Comprimento,
                        EhParafuso = info.Parafuso,
                        Tipo = dados.Tipo,
                        Propriedades = dados.Propriedades,
                    };
                    p.Nome = NomePeca(p);
                    Pecas[Compat.Numero(ds.Id)] = p;
                    novas.Add(p);
                    criadas++;
                }
                if (criadas == antes + pecas.Count) separadas++;
            }
            if (criadas == 0) return 0;

            if (ehLigacao)
            {
                // A ligação continua no IFC (como conjunto) e as peças ficam dentro dela. Se tudo foi separado,
                // o corpo único dela sai, para a geometria não ficar duplicada.
                bool tudo = separadas == previstas;
                foreach (PecaAco p in novas) p.TirarCorpoDoHost = tudo;
            }
            else
            {
                // Elemento de fabricação avulso: a forma temporária o substitui; o original não é exportado.
                Parameter exp = ExportarParaIfc(origem);
                if (exp != null && !exp.IsReadOnly)
                {
                    _exportarOriginal.Add((origem, exp.AsInteger()));
                    exp.Set(ExportarNao);
                }
            }
            return criadas;
        }

        private DirectShape CriarForma(Document doc, Element origem, Category categoria, List<Solid> solidos, string classe, bool generico = false)
        {
            ElementId cat = (generico ? new[] { new ElementId(BuiltInCategory.OST_GenericModel) } : new[]
            {
                categoria?.Id, categoria?.Parent?.Id, origem.Category?.Id,
                new ElementId(BuiltInCategory.OST_StructConnections), new ElementId(BuiltInCategory.OST_GenericModel),
            }).FirstOrDefault(c => c != null && DirectShape.IsValidCategoryId(c, doc));
            if (cat == null) return null;

            var formas = solidos.Cast<GeometryObject>().ToList();
            DirectShape ds = DirectShape.CreateElement(doc, cat);
            ds.ApplicationId = "MetroSP";
            ds.ApplicationDataId = origem.UniqueId;
            try { ds.SetShape(formas); }
            catch
            {
                doc.Delete(ds.Id);
                return null;
            }
            _criadas.Add(ds.Id);
            _formas[ds.Id] = (origem, solidos, classe);

            // Mesmo subprojeto (workset) da origem: a forma nova cai no subprojeto ativo, que pode estar oculto na vista.
            if (doc.IsWorkshared)
                try
                {
                    Parameter ws = ds.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                    if (ws != null && !ws.IsReadOnly && origem.WorksetId != WorksetId.InvalidWorksetId) ws.Set(origem.WorksetId.IntegerValue);
                }
                catch { }

            // Mesma fase do elemento de origem, para sair na fase exportada como ele.
            try { ds.CreatedPhaseId = origem.CreatedPhaseId; } catch { }
            try { if (origem.DemolishedPhaseId != ElementId.InvalidElementId) ds.DemolishedPhaseId = origem.DemolishedPhaseId; } catch { }

            Parameter como = Compat.ExportarComo(ds, false);
            if (como != null && !como.IsReadOnly) como.Set(classe);
            Parameter exp = ExportarParaIfc(ds);
            if (exp != null && !exp.IsReadOnly) exp.Set(ExportarSim);
            return ds;
        }

        /// <summary>Na exportação por vista, mostra as categorias das formas temporárias se a vista as esconde.</summary>
        private void MostrarCategorias(Document doc)
        {
            foreach (ElementId cat in _criadas.Select(id => doc.GetElement(id)?.Category?.Id).Where(c => c != null).Distinct().ToList())
            {
                try
                {
                    if (_vista.CanCategoryBeHidden(cat) && _vista.GetCategoryHidden(cat))
                    {
                        _vista.SetCategoryHidden(cat, false);
                        _categoriasMostradas.Add(cat);
                    }
                }
                catch (Exception ex) { Avisos.Add($"Ligações metálicas: a vista \"{_vista.Name}\" não deixou mostrar as peças ({ex.Message})."); }
            }
        }

        /// <summary>
        /// Na exportação por vista, o exportador só leva o que a vista mostra. Confere se as formas aparecem na vista;
        /// as que não aparecem são recriadas como Modelo genérico (a classe IFC vem do "Exportar para IFC como") e,
        /// se ainda assim ficarem de fora, o motivo vai para o registro.
        /// </summary>
        private void ConferirNaVista(Document doc)
        {
            try { doc.Regenerate(); } catch { }
            if (_vista == null) return;
            List<ElementId> fora = ForaDaVista(doc);
            if (fora.Count == 0) return;

            string motivo = MotivoFora(doc, doc.GetElement(fora[0]));
            int recriadas = 0;
            foreach (ElementId id in fora)
            {
                if (!_formas.TryGetValue(id, out var f) || !Pecas.TryGetValue(Compat.Numero(id), out PecaAco p)) continue;
                DirectShape novo = CriarForma(doc, f.Origem, null, f.Solidos, f.Classe, generico: true);
                if (novo == null) continue;
                Pecas.Remove(Compat.Numero(id));
                Pecas[Compat.Numero(novo.Id)] = p;
                _formas.Remove(id);
                _criadas.Remove(id);
                try { doc.Delete(id); } catch { }
                recriadas++;
            }
            if (recriadas > 0)
            {
                MostrarCategorias(doc);
                try { doc.Regenerate(); } catch { }
            }
            List<ElementId> ainda = ForaDaVista(doc);
            string antes = $"Ligações metálicas: {fora.Count} peça(s) não apareciam na vista \"{_vista.Name}\" ({motivo})";
            if (ainda.Count == 0)
                Avisos.Add(antes + $"; foram recriadas como Modelo genérico e entram no IFC com a classe certa.");
            else
                Avisos.Add(antes + $"; {ainda.Count} continuam fora ({MotivoFora(doc, doc.GetElement(ainda[0]))}) e não vão sair no IFC.");
        }

        private List<ElementId> ForaDaVista(Document doc)
        {
            var vivas = _criadas.Where(id => doc.GetElement(id) != null).ToList();
            if (vivas.Count == 0) return vivas;
            HashSet<ElementId> visiveis;
            try
            {
                visiveis = new HashSet<ElementId>(new FilteredElementCollector(doc, _vista.Id)
                    .WherePasses(new ElementIdSetFilter(vivas)).ToElementIds());
            }
            catch { return new List<ElementId>(); }
            return vivas.Where(id => !visiveis.Contains(id)).ToList();
        }

        /// <summary>Por que a forma não aparece na vista da exportação (para o registro).</summary>
        private string MotivoFora(Document doc, Element e)
        {
            if (e == null) return "forma apagada";
            var r = new List<string>();
            try
            {
                Category c = e.Category;
                r.Add("categoria " + (c?.Name ?? "?"));
                if (c != null && c.get_AllowsVisibilityControl(_vista) && !c.get_Visible(_vista)) r.Add("categoria oculta na vista");
                if (e.IsHidden(_vista)) r.Add("elemento oculto na vista");
                if (_vista.IsInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate)) r.Add("vista com ocultar/isolar temporário ativo");
                if (doc.IsWorkshared && !_vista.IsWorksetVisible(e.WorksetId)) r.Add("subprojeto oculto na vista");
                if (e.DesignOption != null) r.Add("na opção de projeto " + e.DesignOption.Name);
                ElementId fase = _vista.get_Parameter(BuiltInParameter.VIEW_PHASE)?.AsElementId();
                if (fase != null && fase != ElementId.InvalidElementId) r.Add("fase " + e.GetPhaseStatus(fase));
                if (_vista is View3D v3 && v3.IsSectionBoxActive)
                {
                    BoundingBoxXYZ caixa = v3.GetSectionBox(), bb = e.get_BoundingBox(null);
                    if (bb != null)
                    {
                        XYZ centro = caixa.Transform.Inverse.OfPoint((bb.Min + bb.Max) / 2);
                        bool dentro = centro.X >= caixa.Min.X && centro.X <= caixa.Max.X && centro.Y >= caixa.Min.Y && centro.Y <= caixa.Max.Y
                            && centro.Z >= caixa.Min.Z && centro.Z <= caixa.Max.Z;
                        if (!dentro) r.Add("fora da caixa de corte");
                    }
                }
                if (_vista.GetFilters().Count > 0) r.Add($"{_vista.GetFilters().Count} filtro(s) na vista");
            }
            catch (Exception ex) { r.Add(ex.Message); }
            return string.Join(", ", r);
        }

        /// <summary>
        /// Desfaz o que foi mudado no modelo, ainda dentro da transação: apaga as formas temporárias, devolve
        /// "Exportar para IFC" dos originais e a vista. Necessário quando a transação é gravada (setup que grava o GUID).
        /// </summary>
        public void Restaurar(Document doc)
        {
            foreach (ElementId c in _categoriasMostradas)
                try { _vista.SetCategoryHidden(c, true); } catch { }
            foreach (var (e, valor) in _exportarOriginal)
                try { ExportarParaIfc(e)?.Set(valor); } catch { }
            try { if (_criadas.Count > 0) doc.Delete(_criadas); } catch { }
        }

        // ------------------------------------------------------------------ geometria

        /// <summary>Vista 3D para ler a geometria: a da exportação, se for 3D, senão a primeira vista 3D comum do modelo.</summary>
        internal static View3D Vista3D(Document doc, ElementId vistaId)
        {
            if (vistaId != null && vistaId != ElementId.InvalidElementId && doc.GetElement(vistaId) is View3D v && !v.IsTemplate) return v;
            return new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                .Where(x => !x.IsTemplate).OrderBy(x => x.Name == "{3D}" ? 0 : 1).FirstOrDefault();
        }

        /// <summary>
        /// Geometria do elemento: primeiro como a vista 3D mostra (ligações detalhadas, perfis com recortes), depois sem
        /// vista no nível Fino e, por último, incluindo os objetos invisíveis.
        /// </summary>
        internal static GeometryElement Geometria(Element e, View3D vista)
        {
            var opcoes = new List<Options>();
            if (vista != null) opcoes.Add(new Options { View = vista });
            opcoes.Add(new Options { DetailLevel = ViewDetailLevel.Fine });
            opcoes.Add(new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = true });
            GeometryElement primeira = null;
            foreach (Options o in opcoes)
            {
                GeometryElement ge = null;
                try { ge = e.get_Geometry(o); } catch { }
                if (ge == null) continue;
                primeira = primeira ?? ge;
                if (Solidos(ge).Count > 0) return ge;
            }
            return primeira;
        }

        /// <summary>Categoria do sólido pelo estilo gráfico (subcategoria da ligação), ou pela forma quando não tem.</summary>
        private static BuiltInCategory CategoriaDoSolido(Document doc, Solid s)
        {
            try
            {
                if (doc.GetElement(s.GraphicsStyleId) is GraphicsStyle gs && gs.GraphicsStyleCategory is Category c)
                    for (Category x = c; x != null; x = x.Parent)
                        if (Categorias.ContainsKey(x.BuiltInCategory)) return x.BuiltInCategory;
            }
            catch { }
            return CategoriaPelaForma(s);
        }

        private static BuiltInCategory CategoriaPelaForma(Solid s)
        {
            BoundingBoxXYZ bb = s.GetBoundingBox();
            double[] d = { bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y, bb.Max.Z - bb.Min.Z };
            Array.Sort(d);
            double m = 0.3048;
            if (d[2] * m < 0.30 && d[0] * m > 0.004 && d[1] < d[0] * 2.5) return BuiltInCategory.OST_StructConnectionBolts;
            return BuiltInCategory.OST_StructConnectionPlates;
        }

        /// <summary>
        /// Separa o corpo da ligação em volumes e dá a categoria de cada um. Arruela e porca são discos e prismas
        /// pequenos que a forma sozinha confunde com chapa: um volume pequeno (até 10 cm) que toca um parafuso é parte
        /// dele, para o parafuso sair inteiro (cabeça, corpo, arruelas e porca) e as chapas só com as chapas.
        /// </summary>
        private static List<(BuiltInCategory Categoria, Solid Volume)> ClassificarVolumes(Document doc, List<Solid> solidos)
        {
            var vols = new List<(BuiltInCategory Categoria, Solid Volume)>();
            foreach (Solid s in solidos)
            {
                BuiltInCategory cat = CategoriaDoSolido(doc, s);
                List<Solid> partes;
                try { partes = SolidUtils.SplitVolumes(s).Where(v => v.Volume > 1e-9).ToList(); }
                catch { partes = new List<Solid>(); }
                if (partes.Count == 0) partes.Add(s);
                foreach (Solid v in partes)
                    vols.Add((partes.Count > 1 && cat == BuiltInCategory.OST_StructConnectionPlates ? CategoriaPelaForma(v) : cat, v));
            }
            var caixas = vols.Select(v => Caixa(v.Volume)).ToList();
            bool Pequeno(int i)
            {
                var c = caixas[i];
                return Math.Max(c.Max.X - c.Min.X, Math.Max(c.Max.Y - c.Min.Y, c.Max.Z - c.Min.Z)) * 0.3048 <= 0.10;
            }
            const double folga = 0.5 / 304.8;
            bool mudou = true;
            while (mudou)
            {
                mudou = false;
                for (int i = 0; i < vols.Count; i++)
                {
                    if (vols[i].Categoria != BuiltInCategory.OST_StructConnectionPlates || !Pequeno(i)) continue;
                    for (int j = 0; j < vols.Count; j++)
                        if (vols[j].Categoria == BuiltInCategory.OST_StructConnectionBolts && Tocam(caixas[i], caixas[j], folga))
                        {
                            vols[i] = (BuiltInCategory.OST_StructConnectionBolts, vols[i].Volume);
                            mudou = true;
                            break;
                        }
                }
            }
            return vols;
        }

        /// <summary>O que a API devolve para a ligação, para o registro (quando nenhuma peça saiu).</summary>
        private string Diagnostico(Document doc, Element origem, IList<Subelement> subs)
        {
            var sb = new StringBuilder();
            sb.Append($"elemento {Compat.Numero(origem.Id)} ({origem.GetType().Name}, {origem.Category?.Name ?? "sem categoria"})");
            sb.Append($"; subelementos: {subs?.Count ?? 0}");
            if (subs != null && subs.Count > 0)
                sb.Append(" [" + string.Join(", ", subs.Take(6).Select(x =>
                {
                    string tipo = "erro";
                    try { tipo = x.GetGeometryObject(_vista3d)?.GetType().Name ?? "sem geometria"; } catch (Exception ex) { tipo = ex.GetType().Name; }
                    return (x.Category?.Name ?? "?") + ": " + tipo;
                })) + "]");
            void Conta(string nome, Options o)
            {
                try
                {
                    GeometryElement ge = origem.get_Geometry(o);
                    if (ge == null) { sb.Append($"; {nome}: nada"); return; }
                    var tipos = ge.Cast<GeometryObject>().GroupBy(x => x.GetType().Name).Select(g => $"{g.Count()} {g.Key}");
                    sb.Append($"; {nome}: {string.Join(", ", tipos)} ({Solidos(ge).Count} sólido(s))");
                }
                catch (Exception ex) { sb.Append($"; {nome}: {ex.GetType().Name}"); }
            }
            if (_vista3d != null) Conta("na vista " + _vista3d.Name, new Options { View = _vista3d });
            Conta("Fino", new Options { DetailLevel = ViewDetailLevel.Fine });
            Conta("com invisíveis", new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = true });
            return sb.ToString();
        }

        internal static List<Solid> Solidos(GeometryObject g)
        {
            var r = new List<Solid>();
            Juntar(g, r, 0);
            return r;
        }

        private static void Juntar(GeometryObject g, List<Solid> r, int nivel)
        {
            switch (g)
            {
                case Solid s when s.Volume > 1e-9: r.Add(s); break;
                case GeometryElement ge:
                    foreach (GeometryObject o in ge) Juntar(o, r, nivel + 1);
                    break;
                case GeometryInstance gi when nivel < 8:
                    Juntar(gi.GetInstanceGeometry(), r, nivel + 1);
                    break;
            }
        }

        /// <summary>
        /// Separa os volumes e junta os que se tocam (parafuso + porca + arruelas), para cada parafuso de um grupo
        /// virar um elemento. A ordem segue a posição, para a numeração das peças (e o GlobalId) não mudar à toa.
        /// </summary>
        private static List<List<Solid>> Agrupar(List<Solid> solidos)
        {
            var volumes = new List<Solid>();
            foreach (Solid s in solidos)
            {
                try { volumes.AddRange(SolidUtils.SplitVolumes(s).Where(v => v.Volume > 1e-9)); }
                catch { volumes.Add(s); }
            }
            var caixas = volumes.Select(Caixa).ToList();
            int[] pai = Enumerable.Range(0, volumes.Count).ToArray();
            int Raiz(int i) { while (pai[i] != i) i = pai[i] = pai[pai[i]]; return i; }
            const double folga = 0.5 / 304.8;   // 0,5 mm
            for (int i = 0; i < volumes.Count; i++)
                for (int j = i + 1; j < volumes.Count; j++)
                    if (Tocam(caixas[i], caixas[j], folga)) pai[Raiz(i)] = Raiz(j);
            return Enumerable.Range(0, volumes.Count).GroupBy(Raiz)
                .Select(g => g.ToList())
                .OrderBy(l => Math.Round(l.Min(i => caixas[i].Min.Z), 3))
                .ThenBy(l => Math.Round(l.Min(i => caixas[i].Min.X), 3))
                .ThenBy(l => Math.Round(l.Min(i => caixas[i].Min.Y), 3))
                .Select(l => l.Select(i => volumes[i]).ToList()).ToList();
        }

        private static (XYZ Min, XYZ Max) Caixa(Solid s)
        {
            BoundingBoxXYZ b = s.GetBoundingBox();
            Transform t = b.Transform;
            double[] x = new double[8], y = new double[8], z = new double[8];
            int k = 0;
            foreach (double a in new[] { b.Min.X, b.Max.X })
                foreach (double c in new[] { b.Min.Y, b.Max.Y })
                    foreach (double d in new[] { b.Min.Z, b.Max.Z })
                    {
                        XYZ p = t.OfPoint(new XYZ(a, c, d));
                        x[k] = p.X; y[k] = p.Y; z[k] = p.Z; k++;
                    }
            return (new XYZ(x.Min(), y.Min(), z.Min()), new XYZ(x.Max(), y.Max(), z.Max()));
        }

        private static bool Tocam((XYZ Min, XYZ Max) a, (XYZ Min, XYZ Max) b, double f) =>
            a.Min.X <= b.Max.X + f && b.Min.X <= a.Max.X + f &&
            a.Min.Y <= b.Max.Y + f && b.Min.Y <= a.Max.Y + f &&
            a.Min.Z <= b.Max.Z + f && b.Min.Z <= a.Max.Z + f;

        // ------------------------------------------------------------------ dados

        private class Dados
        {
            public string Norma, Classe, Tipo;
            public double? Diametro, Comprimento;   // metros
            public Dictionary<string, string> Propriedades = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Parâmetros do subelemento (grupo de parafusos, chapa...) e do tipo dele.</summary>
        private static Dados LerDados(Document doc, Subelement s)
        {
            var d = new Dados();
            try
            {
                foreach (ElementId pid in s.GetAllParameters())
                {
                    string nome = NomeParametro(doc, pid);
                    if (string.IsNullOrWhiteSpace(nome)) continue;
                    ParameterValue v = null;
                    try { v = s.GetParameterValue(pid); } catch { }
                    if (v == null) continue;
                    double? pes = (v as DoubleParameterValue)?.Value;
                    string texto = TextoValor(doc, v, nome);
                    Guardar(d, nome, texto, pes);
                }
            }
            catch { /* subelemento sem parâmetros */ }
            if (s.TypeId != null && s.TypeId != ElementId.InvalidElementId && doc.GetElement(s.TypeId) is Element tipo)
            {
                d.Tipo = tipo.Name;
                LerParametros(d, tipo);
            }
            return d;
        }

        private static Dados LerDados(Document doc, Element e)
        {
            var d = new Dados();
            LerParametros(d, e);
            if (doc.GetElement(e.GetTypeId()) is Element tipo)
            {
                d.Tipo = tipo.Name;
                LerParametros(d, tipo);
            }
            return d;
        }

        private static void LerParametros(Dados d, Element e)
        {
            foreach (Parameter p in e.Parameters)
            {
                if (p?.Definition == null || !p.HasValue) continue;
                string nome = p.Definition.Name;
                if (Ignorar(nome)) continue;
                double? pes = null;
                if (p.StorageType == StorageType.Double)
                {
                    try { if (p.Definition.GetDataType() == SpecTypeId.Length) pes = p.AsDouble(); } catch { }
                }
                string texto = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
                if (p.StorageType == StorageType.ElementId && string.IsNullOrEmpty(texto))
                    texto = e.Document.GetElement(p.AsElementId())?.Name;
                Guardar(d, nome, texto, pes);
            }
        }

        private static void Guardar(Dados d, string nome, string texto, double? pes)
        {
            if (string.IsNullOrWhiteSpace(texto) || Ignorar(nome)) return;
            string n = SemAcento(nome).ToLowerInvariant();
            bool furo = n.Contains("hole") || n.Contains("furo") || n.Contains("thread") || n.Contains("rosca") || n.Contains("grip") || n.Contains("aperto");
            if (!furo && d.Diametro == null && pes > 0 && (n.Contains("diameter") || n.Contains("diametro"))) d.Diametro = pes * 0.3048;
            else if (!furo && d.Comprimento == null && pes > 0 && (n.Contains("length") || n.Contains("comprimento"))) d.Comprimento = pes * 0.3048;
            if (d.Norma == null && (n.Contains("standard") || n.Contains("norma"))) d.Norma = texto.Trim();
            if (d.Classe == null && (n.Contains("grade") || n.Contains("classe"))) d.Classe = texto.Trim();
            if (!d.Propriedades.ContainsKey(nome)) d.Propriedades[nome] = texto.Trim();
        }

        private static readonly string[] Ignorados =
        {
            "IfcGUID", "IfcExportAs", "IfcExportType", "IFC Predefined Type", "Export to IFC", "Export to IFC As", "Type IfcGUID",
            "Type Image", "Image", "URL", "Keynote", "Phase Created", "Phase Demolished", "Design Option", "Workset", "Edited by",
            "Exportar para IFC", "Exportar para IFC como", "Tipo predefinido IFC", "Fase de criação", "Fase de demolição",
            "Imagem do tipo", "Imagem", "Opção de projeto", "Subprojeto", "Editado por",
        };

        private static bool Ignorar(string nome) =>
            string.IsNullOrWhiteSpace(nome) || Ignorados.Any(i => string.Equals(i, nome, StringComparison.OrdinalIgnoreCase)) ||
            nome.StartsWith("Ifc", StringComparison.OrdinalIgnoreCase);

        private static string NomeParametro(Document doc, ElementId pid)
        {
            long n = Compat.Numero(pid);
            if (n < 0)
            {
                try { return LabelUtils.GetLabelFor((BuiltInParameter)(int)n); } catch { return null; }
            }
            return (doc.GetElement(pid) as ParameterElement)?.GetDefinition()?.Name;
        }

        /// <summary>Valor do parâmetro do subelemento como texto. Comprimentos (pelo nome) em mm.</summary>
        private static string TextoValor(Document doc, ParameterValue v, string nome)
        {
            switch (v)
            {
                case StringParameterValue s: return s.Value;
                case IntegerParameterValue i: return i.Value.ToString(CultureInfo.InvariantCulture);
                case ElementIdParameterValue e: return e.Value == ElementId.InvalidElementId ? null : doc.GetElement(e.Value)?.Name;
                case DoubleParameterValue d:
                    string n = SemAcento(nome).ToLowerInvariant();
                    bool comprimento = new[] { "diameter", "diametro", "length", "comprimento", "thickness", "espessura", "width", "largura", "height", "altura", "size", "tamanho" }.Any(n.Contains);
                    return comprimento
                        ? Math.Round(d.Value * 304.8, 1).ToString("0.#", CultureInfo.GetCultureInfo("pt-BR")) + " mm"
                        : Math.Round(d.Value, 4).ToString("0.####", CultureInfo.GetCultureInfo("pt-BR"));
                default: return null;
            }
        }

        private static string NomePeca(PecaAco p)
        {
            var sb = new StringBuilder(p.Peca);
            if (p.Diametro > 0) sb.Append(" Ø").Append(Mm(p.Diametro.Value));
            if (p.Comprimento > 0 && p.EhParafuso) sb.Append(" x ").Append(Mm(p.Comprimento.Value));
            if (!string.IsNullOrWhiteSpace(p.Classe) && p.EhParafuso) sb.Append(" - ").Append(p.Classe.Trim());
            return sb.ToString();
        }

        private static string Mm(double metros) => Math.Round(metros * 1000, 1).ToString("0.#", CultureInfo.GetCultureInfo("pt-BR"));

        private static string SemAcento(string s)
        {
            string n = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(n.Length);
            foreach (char c in n)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString();
        }
    }
}
