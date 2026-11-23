using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MetroSP.Planilha;

namespace MetroSP.Classificacao
{
    /// <summary>Um item da classificação (equivalente a um IfcClassificationReference).</summary>
    public class ClassItem
    {
        public string Codigo { get; set; }
        /// <summary>Texto que vai depois dos dois-pontos: a coluna Título ou, sem ela, a Descrição.</summary>
        public string Descricao { get; set; }
        /// <summary>Coluna Descrição quando há coluna Título (vai para o parâmetro da descrição, se houver).</summary>
        public string DescricaoLonga { get; set; }
        public string CodigoPai { get; set; }
        /// <summary>Categorias do Revit em que o item vale (vazio = todas).</summary>
        public List<string> Categorias { get; } = new List<string>();
        public ClassItem Pai { get; set; }
        public List<ClassItem> Filhos { get; } = new List<ClassItem>();

        public string Caminho
        {
            get
            {
                var partes = new List<string>();
                for (ClassItem i = Pai; i != null; i = i.Pai) partes.Insert(0, i.Descricao);
                return string.Join(" › ", partes);
            }
        }

        public bool ValeParaCategoria(string categoria) =>
            Categorias.Count == 0 || Categorias.Any(c => string.Equals(c, categoria, StringComparison.CurrentCultureIgnoreCase));

        public override string ToString() => $"{Codigo}  {Descricao}";
    }

    /// <summary>
    /// Lista de classificação lida de uma planilha (.xlsx ou .csv), no espírito do IfcClassification:
    /// colunas Código, Descrição e, opcionais, Código pai e Categorias (o usuário indica a coluna de cada uma).
    /// Sem "Código pai", a hierarquia sai do próprio código (EST.02.01 é filho de EST.02).
    /// </summary>
    public class ClassificationList
    {
        public string Arquivo { get; private set; }
        public string Nome { get; set; }
        public string Edicao { get; set; }
        public List<ClassItem> Itens { get; } = new List<ClassItem>();
        public List<ClassItem> Raizes { get; } = new List<ClassItem>();
        public Dictionary<string, ClassItem> PorCodigo { get; } = new Dictionary<string, ClassItem>(StringComparer.OrdinalIgnoreCase);
        public List<string> Avisos { get; } = new List<string>();
        /// <summary>Linhas de dados com código (contando as repetidas).</summary>
        public int LinhasComCodigo { get; private set; }
        /// <summary>Código que mais se repete na planilha e quantas linhas ele ocupa.</summary>
        public string MaisRepetido { get; private set; }
        public int VezesMaisRepetido { get; private set; }

        /// <summary>
        /// true quando a coluna do código repete o mesmo valor em boa parte das linhas
        /// (ex.: "[LMB7]" em todas): é outra coluna (tipo, grupo, nome da lista), não o código do item.
        /// </summary>
        public bool CodigoRepetido => LinhasComCodigo >= 3 && Itens.Count * 2 <= LinhasComCodigo;

        // ---- preparado uma vez por lista, para não recalcular a cada elemento na verificação
        private List<(ClassItem Item, string Alfa, string Desc)> _preparados;
        private Dictionary<string, ClassItem> _porAlfa;

        /// <summary>Cada item com o código só com letras e números (maiúsculas) e a descrição normalizada.</summary>
        internal List<(ClassItem Item, string Alfa, string Desc)> Preparados
        {
            get
            {
                if (_preparados == null)
                    _preparados = Itens.Select(i => (i, ClassificationCore.SoAlfanumerico(i.Codigo), ClassificationCore.Normalizar(i.Descricao))).ToList();
                return _preparados;
            }
        }

        /// <summary>Código só com letras e números → primeiro item da lista com ele.</summary>
        internal Dictionary<string, ClassItem> PorAlfa
        {
            get
            {
                if (_porAlfa == null)
                {
                    _porAlfa = new Dictionary<string, ClassItem>(StringComparer.Ordinal);
                    foreach (var x in Preparados)
                        if (!_porAlfa.ContainsKey(x.Alfa)) _porAlfa[x.Alfa] = x.Item;
                }
                return _porAlfa;
            }
        }

        /// <summary>Resultados já calculados das buscas por código parecido e por texto (o mesmo valor se repete em muitos elementos).</summary>
        internal Dictionary<(string, string), ClassItem> CacheParecido { get; } = new Dictionary<(string, string), ClassItem>();
        internal Dictionary<string, ClassItem> CachePorTexto { get; } = new Dictionary<string, ClassItem>(StringComparer.Ordinal);

        /// <summary>Lê a planilha achando sozinho a linha dos títulos e as colunas (Código, Descrição, Código pai, Categorias).</summary>
        public static ClassificationList Ler(string caminho)
        {
            Tabela t = LeitorPlanilha.Ler(caminho, soPrimeiraAba: true);
            var mapa = new MapaColunas { Arquivo = caminho, Aba = t.NomesAbas[0] };
            var campos = SistemaClassificacao.Campos.Append(SistemaClassificacao.CampoEdicao).ToList();
            mapa.LinhaCabecalho = MapaColunas.AcharCabecalho(t, mapa.Aba, campos);
            mapa.Sugerir(t, campos);
            if (mapa.Coluna(SistemaClassificacao.CampoCodigo.Chave) == null) throw new InvalidDataException("Não encontrei a coluna \"Código\" na planilha.");
            if (mapa.Coluna(SistemaClassificacao.CampoDescricao.Chave) == null && mapa.Coluna(SistemaClassificacao.CampoTitulo.Chave) == null) throw new InvalidDataException("Não encontrei a coluna \"Descrição\" na planilha.");
            return De(t, mapa, Path.GetFileNameWithoutExtension(caminho));
        }

        /// <summary>Monta a lista com as colunas indicadas pelo usuário.</summary>
        public static ClassificationList De(Tabela t, MapaColunas mapa, string nome)
        {
            var lista = new ClassificationList { Arquivo = mapa.Arquivo, Nome = nome };
            bool comTitulo = mapa.Coluna(SistemaClassificacao.CampoTitulo.Chave) != null;
            var contagem = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (LinhaPlanilha l in mapa.Linhas(t))
            {
                string cod = ValorClassificacao.SemColchetes(l[SistemaClassificacao.CampoCodigo.Chave]);
                if (string.IsNullOrWhiteSpace(cod)) continue;
                lista.LinhasComCodigo++;
                contagem[cod] = contagem.TryGetValue(cod, out int n) ? n + 1 : 1;
                string desc = (l[SistemaClassificacao.CampoDescricao.Chave] ?? "").Trim();
                var item = new ClassItem
                {
                    Codigo = cod.Trim(),
                    Descricao = comTitulo ? (l[SistemaClassificacao.CampoTitulo.Chave] ?? "").Trim() : desc,
                    DescricaoLonga = comTitulo ? desc : "",
                    CodigoPai = l[SistemaClassificacao.CampoPai.Chave]?.Trim(),
                };
                if (lista.Edicao == null) lista.Edicao = l[SistemaClassificacao.CampoEdicao.Chave];
                // Categorias não são mais conferidas (1.0.10): perfis antigos com a coluna mapeada são ignorados.
                if (lista.PorCodigo.ContainsKey(item.Codigo))
                {
                    lista.Avisos.Add($"Código repetido na planilha: {item.Codigo} (linha {l.Numero}); ficou a primeira ocorrência.");
                    continue;
                }
                lista.PorCodigo[item.Codigo] = item;
                lista.Itens.Add(item);
            }
            if (contagem.Count > 0)
            {
                var mais = contagem.OrderByDescending(kv => kv.Value).First();
                lista.MaisRepetido = mais.Key;
                lista.VezesMaisRepetido = mais.Value;
            }
            if (lista.CodigoRepetido)
                lista.Avisos.Insert(0, $"A coluna do código repete \"{lista.MaisRepetido}\" em {lista.VezesMaisRepetido} de {lista.LinhasComCodigo} linhas: confira se é mesmo a coluna do código.");
            lista.MontarArvore();
            return lista;
        }

        private void MontarArvore()
        {
            foreach (ClassItem i in Itens)
            {
                ClassItem pai = null;
                if (!string.IsNullOrEmpty(i.CodigoPai)) PorCodigo.TryGetValue(i.CodigoPai, out pai);
                if (pai == null)
                {
                    // Maior código que é prefixo deste, seguido de separador (procura direto no dicionário,
                    // do separador mais à direita para a esquerda, em vez de comparar com a lista inteira).
                    for (int k = i.Codigo.Length - 1; k > 0 && pai == null; k--)
                        if (".- _/".IndexOf(i.Codigo[k]) >= 0) PorCodigo.TryGetValue(i.Codigo.Substring(0, k), out pai);
                }
                i.Pai = pai;
                if (pai != null) pai.Filhos.Add(i); else Raizes.Add(i);
            }
        }
    }
}
