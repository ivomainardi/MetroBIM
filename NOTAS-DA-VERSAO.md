# Notas da versão: MetroSP

Cada versão do MetroSP sai num instalador só, `MetroSP-Setup-<versão>.exe`, com todos os plugins.
A versão de cada plugin aparece entre parênteses. As notas antigas de cada plugin ficam em
`revit/NOTAS-DA-VERSAO.md` e `civil3d/NOTAS-DA-VERSAO.md`.

## 1.0.3 (em preparação)

Revit 0.3.6 · Civil 3D 0.12.0

- Revit, Exportar IFC: novo campo **Fase** em Saída. Exportando o modelo inteiro, dá para escolher a fase (ou deixar o
  padrão do exportador, a última fase). Ao exportar, a fase escolhida fica gravada no setup (.json), em
  `ActivePhaseId`, e volta na próxima vez. Setup de uma classe instalada ou do Forma: grava a sua cópia, como em
  Gerenciar arquivos. Exportando só o visível numa vista, o campo mostra a fase da vista, que é a que vale.
- Revit, Categorias IFC: categoria marcada para não exportar não é mais erro. A classe e o tipo dela não ficam em
  vermelho nem impedem salvar, e o selo "usadas sem classe" conta só as que exportam (as não exportadas aparecem num
  selo neutro).
- Revit, Conferir entrega (etapa Classe IFC): elementos de categorias marcadas para não exportar ficam de fora, em vez
  de aparecer como proxy. Um elemento com "Exportar para IFC como" preenchido continua contando.
- Revit, classe IFC no tipo ou na instância: quando o elemento tem a classe no "Exportar para IFC como" (do tipo ou da
  instância), não é erro. Em Categorias IFC, a categoria sem classe na tabela só conta como "usada sem classe" se
  algum elemento dela não tiver a classe no tipo nem na instância. No Conferir entrega, quem sai como proxy por
  escolha no tipo ou na instância aparece só como informação, não como atenção.

## 1.0.2 (12/11/2026)

Revit 0.3.5 · Civil 3D 0.12.0

- Revit, Exportar IFC: o aviso "O Location do Revit não está no ponto do Project Base Point" agora mostra os números
  (latitude/longitude do Location, a calculada pelo Project Base Point e a distância entre elas). Quando não dá para
  conferir (Project Base Point fora da área do PTL, sem Location etc.), diz isso e o motivo, em vez de dizer que o
  Location está errado.
- Revit, Exportar IFC: nova opção **Sempre exportar os eixos (Grids)**, ligada por padrão e lembrada entre exportações.
  Mesmo exportando só o visível numa vista 3D, os eixos aparecem na vista durante a exportação (a vista volta como
  estava) e a categoria Grids sai como IfcGrid na tabela de categorias. O resumo diz quantos IfcGrid saíram no IFC e
  avisa se nenhum saiu.
- A tolerância da conferência passou de 1 cm para 10 cm: diferenças de arredondamento da latitude/longitude digitada
  no Revit não acusam mais. Na exportação, o IfcSite continua saindo com a latitude/longitude do Project Base Point.

## 1.0.1 (12/11/2026)

Revit 0.3.4 · Civil 3D 0.12.0

- Ícone do instalador com o símbolo do Metrô em branco sobre laranja, como no instalador anterior.

## 1.0.0 (12/11/2026)

Revit 0.3.4 · Civil 3D 0.12.0

- **Um instalador só para todos os plugins.** Ele mostra só os programas encontrados no computador: Revit 2022 a 2027,
  e Civil 3D 2025 a 2027. Instala só para o seu usuário, sem administrador.
- Quem tinha os instaladores antigos (só Civil 3D, só Revit 2022 a 2024, ou Revit + Civil 3D) não precisa desinstalar
  nada: o novo remove os antigos e fica um "MetroSP" só em Aplicativos instalados.
- Os três plugins passam a ficar num repositório só, público: [MetroBIM](https://github.com/ivomainardi/MetroBIM).
  As funções de cada plugin são as mesmas das versões Revit 0.3.4 e Civil 3D 0.12.0.
