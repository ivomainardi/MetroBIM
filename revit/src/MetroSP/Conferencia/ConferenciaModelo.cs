using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using MetroSP.Ids;

namespace MetroSP.Conferencia
{
    /// <summary>Semáforo: verde = ok, amarelo = atenção (não impede), vermelho = pendência, cinza = não conferido.</summary>
    public enum Sinal { Verde, Amarelo, Vermelho, Cinza }

    /// <summary>Ferramenta que ajuda a corrigir a pendência.</summary>
    public enum Correcao
    {
        Nenhuma,
        EditarPacote,
        ParametrosDeProjeto,
        ListarCompartilhados,
        AplicarClassificacao,
        VerificarClassificacao,
        DetalhesIds,
        EditarEmMassa,
        CategoriasIfc,
        EditarPropertySets,
    }

    /// <summary>Uma linha de pendência (ou de informação) de uma etapa.</summary>
    public class Pendencia
    {
        public Sinal Sinal { get; set; }
        public string Categoria { get; set; }
        /// <summary>Do que se trata: arquivo, parâmetro, campo (Pset.Propriedade), situação da classificação.</summary>
        public string Assunto { get; set; }
        public string Problema { get; set; }
        public string OQueFazer { get; set; }
        public List<ElementId> Elementos { get; set; } = new List<ElementId>();
        public Correcao Correcao { get; set; }
        /// <summary>Parâmetro do Revit a preencher (etapa 4), para abrir o Editar em Massa já nele.</summary>
        public string Parametro { get; set; }
        /// <summary>Campos do .txt (Pset.Propriedade) que leem o parâmetro, para mostrar de onde vem a exigência.</summary>
        public string Campo { get; set; }
        /// <summary>Arquivo que faz a exigência (ex.: o .txt de property sets dos padrões).</summary>
        public string Origem { get; set; }

        public int QtdElementos => Elementos.Count;
        public string Situacao =>
            Sinal == Sinal.Vermelho ? "Pendente" : Sinal == Sinal.Amarelo ? "Atenção" : Sinal == Sinal.Verde ? "OK" : "Info";
    }

    public class EtapaConferencia
    {
        public int Numero { get; set; }
        public string Titulo { get; set; }
        /// <summary>O que a etapa confere, em uma frase.</summary>
        public string Descricao { get; set; }
        public string Resumo { get; set; }
        public Sinal Sinal { get; set; } = Sinal.Verde;
        public List<Pendencia> Pendencias { get; } = new List<Pendencia>();
        /// <summary>Ferramenta principal para corrigir o que a etapa aponta.</summary>
        public Correcao Correcao { get; set; }

        /// <summary>Quantidade a mostrar no cartão (elementos com problema, ou itens se a etapa não é por elemento).</summary>
        public int Contagem { get; set; }

        public void CalcularSinal()
        {
            if (Sinal == Sinal.Cinza) return;
            if (Pendencias.Any(p => p.Sinal == Sinal.Vermelho)) Sinal = Sinal.Vermelho;
            else if (Pendencias.Any(p => p.Sinal == Sinal.Amarelo)) Sinal = Sinal.Amarelo;
            else Sinal = Sinal.Verde;
        }
    }

    /// <summary>Elemento com problema numa célula da matriz.</summary>
    public class ElementoPendente
    {
        public ElementId Id { get; set; }
        /// <summary>"Família: Tipo" (ou o nome do tipo).</summary>
        public string Tipo { get; set; }
        public string Problema { get; set; }
        public string Valor { get; set; }
    }

    /// <summary>Uma célula da matriz categoria × campo exigido.</summary>
    public class CelulaMatriz
    {
        public string Categoria { get; set; }
        public string Campo { get; set; }
        public string Parametro { get; set; }
        public bool NoTipo { get; set; }
        /// <summary>Restrição de valor do IDS (null = qualquer valor preenchido).</summary>
        public string ValorExigido { get; set; }
        /// <summary>Valores fixos aceitos pelo IDS (valor único ou enumeração), para escolher ao corrigir.</summary>
        public List<string> Opcoes { get; } = new List<string>();
        public int Total { get; set; }
        public int Ok { get; set; }
        /// <summary>Não dá para conferir aqui (ex.: Pset padrão calculado pelo exportador).</summary>
        public string NaoConferido { get; set; }
        /// <summary>Nenhum mapeamento no .txt gera este campo.</summary>
        public bool SemMapeamento { get; set; }
        public List<ElementoPendente> Pendentes { get; } = new List<ElementoPendente>();

        public double Percentual => Total == 0 ? 1 : (double)Ok / Total;
        public bool TemPendencia => NaoConferido == null && (SemMapeamento || Ok < Total);

        public Sinal Sinal =>
            NaoConferido != null ? Sinal.Cinza
            : SemMapeamento ? Sinal.Vermelho
            : Ok == Total ? Sinal.Verde
            : Percentual >= 0.8 ? Sinal.Amarelo
            : Sinal.Vermelho;

        public string Texto =>
            NaoConferido != null ? "—"
            : SemMapeamento ? "sem mapa"
            : Pendentes.Count > 0 && Pendentes.All(p => p.Problema == ConferenciaEntrega.ProblemaSemParametro) ? "sem parâm."
            : Ok == Total ? "100%"
            : $"{Math.Floor(Percentual * 100):0}%";
    }

    public class ResultadoConferencia
    {
        public DateTime Quando { get; set; } = DateTime.Now;
        public string Documento { get; set; }
        public string Escopo { get; set; }
        /// <summary>Elementos que vão para o IFC numa classe que os padrões conferem (os que entram nas etapas 4 e 5).</summary>
        public int TotalElementos { get; set; }
        /// <summary>Elementos que vão para o IFC numa classe sem nenhum campo dos padrões (só aparecem na etapa 3).</summary>
        public int ForaDoPadrao { get; set; }
        /// <summary>Classes IFC que os padrões conferem, para escolher ao corrigir a classe na etapa 3.</summary>
        public List<string> ClassesDoPadrao { get; } = new List<string>();
        /// <summary>Esquema do setup do pacote (CategoriasIfc.Ifc4 ou Ifc4x3), para listar as classes ao corrigir.</summary>
        public string Esquema { get; set; } = CategoriasIfc.Ifc4x3;
        public List<EtapaConferencia> Etapas { get; } = new List<EtapaConferencia>();

        /// <summary>Linhas da matriz: categoria → classe IFC (para mostrar) e quantidade de elementos.</summary>
        public List<(string Categoria, string Classe, int Elementos)> Categorias { get; } = new List<(string, string, int)>();
        /// <summary>Colunas da matriz: Pset.Propriedade.</summary>
        public List<string> Campos { get; } = new List<string>();
        public Dictionary<(string Categoria, string Campo), CelulaMatriz> Matriz { get; } = new Dictionary<(string, string), CelulaMatriz>();

        /// <summary>Resultado completo do IDS (para abrir a janela de detalhes), se o pacote tem IDS.</summary>
        public IdsDocument Ids { get; set; }
        public List<CheckResult> ResultadosIds { get; set; }

        /// <summary>Quanto das verificações por elemento passou (0 a 1).</summary>
        public double Progresso { get; set; }

        public int Vermelhas => Etapas.Count(e => e.Sinal == Sinal.Vermelho);
        public int Amarelas => Etapas.Count(e => e.Sinal == Sinal.Amarelo);
        public bool ProntoParaExportar => Vermelhas == 0;
    }
}
