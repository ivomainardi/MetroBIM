# Notas da versão: MetroSP

Cada versão do MetroSP sai num instalador só, `MetroSP-Setup-<versão>.exe`, com todos os plugins.
A versão de cada plugin aparece entre parênteses. As notas antigas de cada plugin ficam em
`revit/NOTAS-DA-VERSAO.md` e `civil3d/NOTAS-DA-VERSAO.md`.

## 1.0.4 (em preparação)

Revit 0.3.7 · Civil 3D 0.12.0

- Revit, **Exportar IFC** com a interface reorganizada: **Saída** traz os elementos (modelo inteiro ou vista 3D) e o
  arquivo, e o novo grupo **Extras** reúne Localização, IfcProject, IfcSite, IfcBuilding, Coordenadas (EPSG em
  branco), Fase e Eixos, cada um com o rótulo à esquerda e a explicação na dica.
  - O botão Projeto e Local saiu. No lugar, **Gravar localização** grava no Location a latitude/longitude do Project
    Base Point com um clique, e os nomes do **IfcProject, IfcSite e IfcBuilding** são preenchidos ali mesmo e gravados
    nos campos do Revit ao exportar. O link **Mais dados...** abre os demais campos (nome longo, descrição, endereço,
    Pset MetroSP_Projeto).
  - Location fora do Project Base Point ao exportar: "Sim" grava a localização e exporta.
- Revit, **arquivo de coordenação** (.coord-metrosp): o coordenador BIM grava tudo o que ajustou para a exportação
  IFC num arquivo só e envia ao projetista; o Exportar IFC do projetista passa a usar tudo o que veio nele.
  - O que vai no arquivo: o setup do exportador (inteiro) e a fase, **pelo nome** (o número interno da fase muda de
    um modelo para outro), as tabelas de Categorias IFC (IFC4 e IFC 4.3), as classes IFC dos tipos de família
    ("Exportar para IFC como", tipo predefinido e "Exportar para IFC", por família e tipo), os property sets e o
    mapeamento (.txt) e os dados de Projeto e Local. Por escolha, também as classes preenchidas em cada elemento,
    que só valem no mesmo modelo.
  - **Perfis**: um arquivo leva vários perfis com nome (ex.: "Entrega Metrô · IFC 4.3", "Compatibilização · IFC 4"),
    cada um com a sua classe, setup, categorias e property sets, e um perfil padrão.
  - **Entrega passo a passo**: no rodapé, **Exportar alterações...** (grava tudo como está agora, atualizando o
    perfil escolhido e mantendo os outros) e **Abrir alterações...** (escolhe o arquivo e o perfil). A faixa preta
    mostra a revisão e o perfil em uso, com troca de perfil.
  - **Fica salvo com o arquivo**: o arquivo de coordenação e o perfil escolhido ficam guardados no próprio modelo
    (.rvt). Quem abre o modelo de novo continua no mesmo perfil, mesmo sem o .coord-metrosp por perto.
  - **Exportar IFC**: faixa laranja com a revisão, o autor e o perfil, e os links perfil, trocar e não usar. A classe
    do perfil é escolhida sozinha e o setup, os .txt e a tabela de categorias vêm do arquivo (selo "da
    coordenação"). Um arquivo que o projetista salvar depois de receber a coordenação (mais novo) vale sobre o dela.
    As classes por família entram só no IFC, na hora de exportar, e o modelo não muda (a não ser que o setup grave o
    GUID IFC, que já mantém as alterações da exportação, ou que o projetista marque "Gravar já no modelo" ao abrir).
    O Exportar IFC também avisa quando há um .coord-metrosp na pasta do modelo ou na pasta do Forma, ou uma revisão
    mais nova que a em uso.
  - Ribbon: o painel "Configurar padrões" passa a se chamar **Coordenação BIM** e ganha o botão **Arquivo de
    coordenação** (a mesma janela do Exportar alterações).

## 1.0.3 (13/11/2026)

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
- Revit, Exportar IFC: o status de fase de cada elemento, na fase exportada, vai para o IFC em dois lugares: no
  **Status** do Pset_&lt;Classe&gt;Common (NEW, EXISTING, DEMOLISH, TEMPORARY, nas classes que têm Status no IFC: paredes,
  lajes, vigas, pilares, portas, janelas, escadas, rampas, coberturas, guarda-corpos, revestimentos, fundações e
  proxies) e no **Estado** do Pset Metro (Novo, Existente, A demolir, Temporário), nas classes que têm o Pset Metro
  no .txt de property sets. Só IFC4 e IFC 4.3. O resumo da exportação diz quantos elementos receberam cada um.

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
