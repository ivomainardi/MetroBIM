# Notas da versão: MetroSP

Cada versão do MetroSP sai num instalador só, `MetroSP-Setup-<versão>.exe`, com todos os plugins.
A versão de cada plugin aparece entre parênteses. As notas antigas de cada plugin ficam em
`revit/NOTAS-DA-VERSAO.md` e `civil3d/NOTAS-DA-VERSAO.md`.

## Próxima versão (em preparação)

- Revit e Civil 3D, **janelas no monitor do software**: todas as janelas do MetroSP, inclusive as de progresso
  (carregando), abrem no mesmo monitor do Revit ou do Civil 3D, centralizadas sobre ele. Uma janela que lembra a
  posição só volta lá se for nesse monitor, e nenhuma fica maior que a tela onde abre.
- Revit, **Verificar requisitos (.ids)** de volta, no lugar certo: no passo **Exportar** da **Entrega passo a passo**, o
  cartão **Requisitos (.ids)** confere o modelo com cada regra do .ids dos padrões antes de exportar (classe IFC,
  property sets e valores, classificação e material), só com os elementos que vão para o IFC. O resultado abre numa
  tabela com filtro em cada coluna; duplo clique seleciona os elementos e **Exportar CSV...** salva a lista. O cartão
  só aparece quando os padrões têm um .ids (passo 1).
- Revit, **Entrega passo a passo › Exportar** mais simples:
  - Erros e avisos só nos cartões **O que falta no passo 6**, cada um com o botão que resolve (Gravar localização,
    Editar property sets, Perfis...). Os selos de dentro da ferramenta saíram.
  - O cartão **Requisitos (.ids)** mostra o resultado da última verificação ("3 regras com falha").
  - Um botão **Exportar IFC** só, no rodapé (no lugar de "Salvar e conferir"). Ao sair do passo, as escolhas ficam
    guardadas como antes.
  - Com um perfil, os arquivos (setup, property sets e mapeamento) viram um resumo ao lado dele; os caminhos ficam na
    dica. Em "Outros arquivos" a lista volta.
  - **Fase** numa linha própria, logo abaixo de Elementos. **Extras** passa a se chamar **Projeto e local**, e EPSG em
    branco e Eixos sempre ficam numa linha de opções (também no Exportar IFC da Ribbon).
  - Perfil e Elementos são listas digitáveis.
- Revit, **Exportar IFC com as peças das ligações metálicas**: parafusos, chumbadores, conectores de cisalhamento,
  chapas e soldas das ligações (Steel Connections) e dos elementos de fabricação de aço saem no IFC4 e no IFC 4.3 como
  elementos próprios: **IfcMechanicalFastener** (BOLT, ANCHORBOLT, SHEARCONNECTOR), **IfcPlate** e **IfcFastener**
  (WELD). Cada parafuso de um grupo vira um elemento, com diâmetro e comprimento nominais, o
  Pset_MechanicalFastenerBolt e o Pset **MetroSP_LigacaoMetalica** (peça, ligação, norma, classe, diâmetro,
  comprimento e os demais parâmetros do Revit). As peças ficam agrupadas dentro da ligação (IfcElementAssembly) e
  têm GlobalId fixo entre exportações. Antes só o IFC 2x3 separava essas peças: no IFC4 a ligação saía como um corpo
  só e os parafusos avulsos não saíam. É automático, sem opção nova na tela; o resumo da exportação diz quantas peças
  saíram.

## 1.0.9 (17/11/2026)

Revit 0.3.12 · Civil 3D 0.12.5

- Revit, **Entrega passo a passo › Classes IFC** sem falsos proxies:
  - **Balaústres, corrimãos (Top Rails, Handrails), suportes e terminações** de guarda-corpo e **linhas de grade de
    parede cortina** (Curtain Wall Grids) não aparecem mais como proxy. O exportador IFC da Autodesk não cria um
    elemento para eles: a geometria vai dentro do IfcRailing ou do IfcCurtainWall. Vale para todas as ferramentas
    (ficam de fora como os demais elementos que não vão para o IFC). Se alguém escolher uma classe para eles no
    "Exportar para IFC como", continuam contando.
  - Elementos com a classe definida no tipo ou na instância ("Exportar para IFC como") estão certos, mesmo que a
    categoria não tenha classe na tabela: não aparecem mais como pendência. O resumo do passo mostra quantos são.
- Revit, **Categorias IFC › Copiar entre IFC schemas**: o menu agora separa **só as linhas selecionadas** e a
  **tabela inteira**, nos dois sentidos (IFC4 → IFC 4.3 e IFC 4.3 → IFC4).
- Revit e Civil 3D, **Exportar IFC mais rápido em modelos grandes**: a etapa em que o MetroSP completa o IFC depois do
  exportador (classificação, status de fase, dados do projeto) ficava muito mais lenta conforme o modelo crescia. Num IFC
  de teste com 20 mil paredes em fases diferentes ela levava 2 min 30 s e agora leva menos de 2 s.
- Revit, **Exportar IFC**:
  - O IFC é gravado e completado numa pasta temporária do computador e só no fim vai para a pasta escolhida. Numa pasta
    do Desktop Connector ou da rede o arquivo é enviado uma vez só, e não a cada etapa.
  - A janela de andamento fica aberta também enquanto o IFC é completado e conferido.
  - O resumo e o registro (.log.txt) mostram quanto tempo levou cada parte: preparação, exportador do Revit e o
    complemento do MetroSP.
  - A conferência de elementos fora da fase exportada ficou mais leve.
- Revit e Civil 3D, **Entrega passo a passo**: abre sempre no passo 1. Antes abria na primeira etapa com pendência
  (ou no último passo, com tudo certo). Quem abre por um atalho (clique numa etapa do Painel MetroSP ou
  **Passo a passo...** do Exportar IFC) continua indo direto para a etapa escolhida.
- Revit, **Entrega passo a passo** com nova sequência: **1 Padrões**, **2 Classes IFC**, **3 Parâmetros compartilhados**,
  **4 MetroDATA**, **5 Classificação** e **6 Exportar**. Classes IFC vem antes dos parâmetros porque o que cada elemento
  precisa ter depende da classe dele.
- Revit, **Entrega › Padrões** (antes Arquivos): além do perfil de exportação, mostra os padrões em uso numa tabela, com o
  arquivo, o que ele traz (por exemplo "12 property sets, 140 propriedades") e o passo que o usa, e um resumo do modelo
  (elementos nos padrões, fora dos padrões, classes IFC e esquema).
- Revit, **Entrega passo a passo › Parâmetros compartilhados** mais fácil de ler:
  - Os erros do .txt de property sets aparecem uma vez só, no painel **O que falta** à esquerda. A caixa rosa repetida
    em cima da tabela saiu (na ferramenta **Parâmetros** aberta sozinha, ela continua, com uma linha por erro).
  - Textos dos erros curtos, por exemplo "2093 elemento(s) sem o parâmetro "Length"." A explicação completa fica na
    dica (passe o mouse no cartão).
  - A tabela de parâmetros de projeto ganha a altura toda. A coluna **Problema** só aparece quando há problema, e as
    colunas não ficam mais espremidas: com pouco espaço, a tabela rola para o lado. Cada título tem uma dica.
- Revit, **Perfil de exportação** no lugar de "Padrão": a configuração que se escolhe para exportar (setup, property sets
  e mapeamento) passa a se chamar **perfil**, no Exportar IFC, na Entrega passo a passo, no arquivo de coordenação
  (Exportar alterações, Abrir alterações e a faixa) e na janela dos perfis. **Padrões do Metrô** (.ids) e a janela
  **Padrões** da Ribbon continuam com o mesmo nome.
- Revit, janela **Perfis de exportação IFC** (Exportar IFC › Perfis... e Entrega › Editar perfis...): agora se escolhe
  o perfil numa lista à esquerda e, à direita, aparecem juntos as **Configurações** (setup .json), os **Property sets** e
  o **Mapeamento** dele. Trocar de perfil troca as três abas. Cada perfil da lista mostra o que tem (configurações, psets
  e mapa) e avisa em laranja quando falta parâmetro no modelo ou o setup não é IFC4/4.3. Novos botões **Duplicar...** e
  **Renomear...**. A divisão entre a lista e o detalhe pode ser arrastada.
- Revit, **Exportar IFC**: quatro opções passam a ser fixas, seja qual for o valor no setup .json: **Psets do IFC padrão**,
  **Psets de material** e **quantidades base** sempre saem; **Psets internos do Revit** nunca saem. Na janela dos perfis
  essas caixas mostram o que está no .json, travadas, com 🔒 "sempre exporta" ou "nunca exporta" (em laranja quando o
  arquivo diz o contrário).
- Revit, **Aplicar classificação abre na hora**: a janela não varre mais os elementos da vista antes de aparecer.
  - As categorias de **Selecionar no Revit por categoria** só são contadas quando você abre a lista de categorias
    (trocar entre Vista atual e Todo o modelo também não conta mais na hora).
  - A planilha é lida depois que a janela aparece, sem travar a tela, e só a primeira aba do .xlsx.
    Reabrindo a janela, a planilha não é lida de novo enquanto o arquivo não mudar (**Recarregar** sempre lê).
  - A árvore só desenha os itens que aparecem na tela (planilhas grandes).

## 1.0.8 (16/11/2026)

Revit 0.3.11 · Civil 3D 0.12.4

- Revit e Civil 3D, **aviso de versão nova**: ao abrir o programa, o MetroSP confere (no máximo uma vez por dia, em
  segundo plano) se já saiu uma versão nova do instalador. Se saiu, mostra a versão, o que mudou e o botão
  **Baixar instalador**. **Agora não** avisa de novo no dia seguinte e **Não avisar desta** só volta na próxima versão.
  Sem internet ou com a rede bloqueando o GitHub, não aparece nada. O aviso começa a valer a partir desta versão.
  O Navisworks (1.0.1) ganha o mesmo aviso quando entrar no instalador.
- Revit e Civil 3D, **Sobre**: botão **Procurar versão nova**, para conferir na hora.
- Instalador, **programa aberto**: com o Revit, o Civil 3D (ou AutoCAD) ou o Navisworks aberto, o instalador não dá
  mais erro na cópia da MetroSP.dll. Antes de copiar, ele mostra quais programas estão abertos (no Revit, com o ano) e
  pede para fechar, com **Tentar de novo** ou **Cancelar**. Só conta o programa onde o MetroSP vai ser instalado: com o
  Revit 2024 aberto e só o 2026 marcado, a instalação segue. Cancelando, nada é alterado.
- O desinstalador faz o mesmo aviso antes de apagar os arquivos.

## 1.0.7 (16/11/2026)

Revit 0.3.10 · Civil 3D 0.12.3

- Revit e Civil 3D, **Ribbon mais enxuta**, igual nos dois: três painéis, **Entrega**, **Ferramentas** e **Coordenação**.
  Nenhuma ferramenta saiu; as menos usadas passaram para a setinha ▾ do botão.
  - Entrega: **Entrega passo a passo** (na setinha, o Painel MetroSP ao lado do modelo) e **Exportar IFC**.
  - Revit, Ferramentas: **Classificação ▾** (Aplicar e Verificar), **Dados ▾** (Planilha de dados, Confrontar planilha,
    Corrigir escrita, Editar em massa, Filtrar e colorir e Copiar valores) e **Parâmetros ▾** (com Renomear famílias).
  - Civil 3D, Ferramentas: **Classificação** e **Dados ▾** (Property sets, Confrontar planilha e Corrigir escrita).
  - Coordenação: **Padrões ▾**. No Revit, com Arquivo de coordenação e Categorias IFC; no Civil 3D, com Configurar IFC,
    Exportador e Forma.
  - Civil 3D, **Projeto e Local** saiu da frente e fica na setinha do Exportar IFC.
  - O Sobre continua na logo, à direita. Os comandos digitados no Civil 3D (MSP_...) não mudaram.
- Revit, **Entrega passo a passo**, passo 1 **Arquivos**: no topo, a escolha do **Padrão** do modelo. O padrão carrega de
  uma vez o setup, os property sets e o mapeamento, vale na conferência e no Exportar IFC (o mesmo dos dois lados) e
  fica guardado para cada modelo. Com arquivo de coordenação, a lista mostra os padrões que vieram nele.
- Revit, um nome só: **Padrão** é a configuração que se escolhe para exportar. Os "perfis" do arquivo de coordenação
  passaram a se chamar padrões (Exportar alterações, Abrir alterações e a faixa da coordenação), e a classe de cada um
  virou a **base** (o padrão deste computador de onde saem o setup e os .txt).
- Revit: o pacote .metrosp e os arquivos um a um passam a se chamar **Arquivos do Metrô** (botão Arquivos... na Entrega e
  na Conferência), para não confundir com o padrão.

## 1.0.6 (16/11/2026)

Revit 0.3.9 · Civil 3D 0.12.2

- Revit, arquivo de coordenação: leva **tudo do Exportar IFC**. Além do setup, da fase, das categorias, das classes
  por família e dos .txt, vão também os Elementos (modelo inteiro ou o visível numa vista 3D, pelo nome da vista), o
  nome do arquivo .ifc (o nome do modelo ou um nome fixo), o EPSG em branco, o "Sempre exportar os eixos" e as regras
  de Nomenclatura (classe pelo nome do modelo). No Exportar IFC do projetista, essas escolhas vêm preenchidas pelo
  arquivo (a dica de cada uma diz "Escolha do arquivo de coordenação"). A pasta de saída não vai, porque muda de um
  computador para outro. O Exportar IFC também passa a lembrar a vista, o EPSG e o nome da última exportação.
- Revit, **Exportar alterações** mais simples: um lugar só para o perfil (a lista, a classe e **+ Novo perfil**) e uma
  frase que diz o que acontece ao salvar (o perfil escolhido fica igual ao modelo agora e os outros ficam como estão).
  Saíram as caixinhas de incluir, o "tornar padrão" (o primeiro perfil é o padrão), o "juntar de outro arquivo" e o
  "usar também neste modelo" (o modelo do coordenador passa a usar o arquivo sempre). A lista **O que vai no arquivo**
  mostra exatamente o que vai, lido do modelo. As classes por elemento só aparecem quando o modelo tem alguma.
- Revit, **Abrir alterações**: a escolha de perfil só aparece quando o arquivo tem mais de um.
- Revit, **Exportar IFC**: o resumo e o registro (.ifc.log.txt) passam a mostrar a saída (modelo inteiro ou vista, e a
  fase), quantos elementos saíram por classe IFC e quantos ambientes (IfcSpace), e quantos elementos ficaram de fora
  por terem sido criados numa fase posterior à exportada.
- Revit, **Exportar IFC**: quando o IFC sai sem elementos, só com pavimentos e ambientes, o resumo avisa em destaque
  ("IFC exportado SEM elementos") e diz a causa provável: a fase (escolha a última em Extras › Fase) ou a vista.
- Revit e Civil 3D: o texto padrão do Revit no endereço ("Enter address here", "Address Line 2") não vai mais para o
  IfcBuilding e aparece vazio na janela.
- Revit, **Entrega passo a passo**, passo 6 **Exportar**: agora tem tudo o que está no Exportar IFC (padrão e
  arquivos, elementos, arquivo, projeto e local, EPSG, fase e eixos) e **exporta dali mesmo**, pelo botão Exportar.
  À esquerda ficam a situação do projeto e local e se o modelo está pronto. **Salvar e conferir** nesse passo guarda as
  escolhas (os nomes vão para o Revit) sem exportar.
- Revit, **Exportar IFC** (botão da Ribbon): continua exportando direto e ganhou, no topo, o **resumo da Entrega** (a
  última conferência: tudo certo, avisos ou etapas com pendência, com o detalhe na dica) e o link **Passo a passo...**,
  que abre a Entrega já no passo Exportar.
- Revit, **Padrão no lugar de Classe**: no Exportar IFC o grupo "Classe" passou a se chamar **Padrão**. Escolher o padrão
  carrega de uma vez o setup, os property sets e o mapeamento dele.
- Revit, **Padrões**: a janela Arquivos IFC virou **Padrões › Exportação IFC** (e Exportar IFC › Padrões...). O botão
  Arquivos IFC saiu da Ribbon; Categorias IFC ficou sozinho, grande. Na lista de Padrões, os arquivos aparecem como
  "Exportação IFC · setup / property sets / mapeamento".
- Revit, **Entrega** e **Categorias IFC**: lances (Runs), patamares (Landings) e apoios (Supports) de escada não
  aparecem mais como proxy no passo 3. Eles têm categoria própria, filha de Stairs, e agora usam a linha
  "Stairs / Runs" da tabela (ou a classe do exportador: IfcStairFlight, IfcSlab, IfcMember). Em Categorias IFC, essas
  linhas mostram quantos elementos há no modelo, e o Selecionar da Entrega seleciona também a escada de cada um.

## 1.0.5 (16/11/2026)

Revit 0.3.8 · Civil 3D 0.12.1

- Revit e Civil 3D, Ribbon, painel Entrega Metrô: os ícones e as dicas não têm mais os números 1, 2 e 3.
- Revit e Civil 3D, **Padrões**: agora é o lugar para **subir novos padrões**. O botão ficou grande na Ribbon (Revit:
  Coordenação BIM › Padrões; Civil 3D: Configurar padrões › Padrões) e a janela ganhou **Adicionar padrões...** e uma
  área para arrastar os arquivos.
  - O tipo de cada arquivo é reconhecido pelo conteúdo e aparece numa conferência antes de gravar (novo, substitui o
    atual ou fica de fora). Um padrão substituído fica guardado na subpasta Anteriores.
  - Revit: classificação, confronto, dados do projeto, corrigir escrita, setups (.json), **property sets** e
    mapeamentos (.txt) IFC, **arquivo de coordenação (.coord-metrosp)**, **padrões Metrô (.ids)** e **pacote de entrega
    (.metrosp)**. O arquivo de coordenação abre o Abrir alterações para escolher o perfil (é ele que aparece na faixa
    laranja do Exportar IFC). Os arquivos IFC abrem a janela Arquivos IFC para indicar a classe; o .ids passa a ser o
    usado na conferência; o .metrosp passa a ser o pacote em uso, sem sair do lugar.
  - Civil 3D: classificação, confronto, dados do projeto, corrigir escrita e configuração IFC (mapeamento,
    propriedades e opções MetroSP), que entra na configuração ativa.
  - Os pacotes .zip também entram por ali (o antigo Importar pacote). **Excluir** continua igual.
- **Menos caminhos repetidos**, para cada coisa ter um lugar só:
  - Revit, **Projeto e Local** saiu da Ribbon: fica no grupo Extras do Exportar IFC. O passo Projeto e Local do Painel MetroSP abre o Exportar IFC.
  - Revit e Civil 3D, **Padrões de classificação** e **Confrontar planilha**: saíram o importar e o exportar de padrão
    de cada janela; trazer e levar padrões é pela janela Padrões (a dica da lista de padrões indica onde).
  - Revit, **um pacote só para a equipe**: o arquivo de coordenação (.coord-metrosp) passa a levar também os arquivos
    da entrega (requisitos .ids, planilha de classificação e MetroShared), dentro dele. Ao abrir, eles valem na
    conferência, na Classificação e em Parâmetros, sobre os do pacote .metrosp. A janela Padrões do Metrô não grava mais
    .metrosp (os que já existem continuam abrindo) e indica o arquivo de coordenação para mandar à equipe.
  - Civil 3D, **Configurar IFC**: o Importar virou **Adicionar padrões...**, que abre a janela Padrões.
  - Revit: saiu a janela antiga **Verificar Padrões Metrô**, que não tinha mais botão e pedia de novo os property
    sets e as classes IFC. A verificação do IDS é a da conferência (Painel MetroSP).

## 1.0.4 (13/11/2026)

Revit 0.3.7 · Civil 3D 0.12.0

- Revit, **Exportar IFC** com a interface reorganizada: **Saída** traz os elementos (modelo inteiro ou vista 3D) e o
  arquivo, e o novo grupo **Extras** reúne Localização, IfcProject, IfcSite, IfcBuilding, Coordenadas (EPSG em
  branco), Fase e Eixos, cada um com o rótulo à esquerda e a explicação na dica.
  - O botão Projeto e Local saiu. No lugar, **Gravar localização** grava no Location a latitude/longitude do Project
    Base Point com um clique, e os nomes do **IfcProject, IfcSite e IfcBuilding** são preenchidos ali mesmo e gravados
    nos campos do Revit ao exportar. O link **Mais dados...** abre os demais campos (nome longo, descrição, endereço,
    Pset MetroSP_Projeto).
  - Location fora do Project Base Point ao exportar: "Sim" grava a localização e exporta.
  - **Entrega passo a passo**: o último passo passou a se chamar **Exportar**. O botão e os cartões abrem essa
    mesma janela Exportar IFC (o antigo "Preencher projeto e local..." saiu, já que tudo está no grupo Extras).
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
