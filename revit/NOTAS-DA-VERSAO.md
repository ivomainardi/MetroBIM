# MetroSP 0.3.3 (em teste)

## Ribbon reorganizada (mais simples)

- A aba MetroSP fica com 5 painéis na ordem do trabalho e sem menus que escondem botões:
  - **Entrega Metrô**: os 3 passos, numerados no ícone: **1 Conferir Entrega**, **2 Projeto e Local**, **3 Exportar IFC**.
  - **Classificação**: Aplicar e Verificar.
  - **Dados do modelo**: igual (Planilha de Dados, Confrontar Planilha, Corrigir Escrita, Editar em Massa, Filtrar e
    Colorir, Copiar Valores).
  - **Parâmetros e famílias**: **Parâmetros** e Renomear Famílias (o menu Parâmetros saiu; ver abaixo).
  - **Coordenador BIM** (novo, no fim, botões pequenos): Padrões e planilhas, Arquivos IFC e Categorias IFC (antes no
    menu Configurar, no meio da entrega).
- Saíram da Ribbon, sem perder a função:
  - **Verificar Padrões**: a verificação do IDS já roda dentro do Conferir Entrega (botão "Detalhes dos padrões (IDS)...").
  - **Consistência**: abre pelo botão **Códigos × descrições...** na janela Verificar classificação.
  - **Renomear compartilhado**: fica no botão **Renomear...** da aba Compartilhados, em Parâmetros.
- Falta testar no Revit (2022, 2025, 2026 e 2027): abrir a aba e conferir os 5 painéis e os ícones 1, 2 e 3; abrir cada
  botão; em Verificar classificação, abrir Códigos × descrições e unificar; em Parâmetros › Compartilhados, renomear
  um parâmetro; no Conferir Entrega com IDS no pacote, abrir os detalhes do IDS.

## Parâmetros: uma ferramenta só

- **Gerenciar parâmetros**, **Listar compartilhados** e **Renomear compartilhado** viram o botão **Parâmetros**, com as
  abas **Compartilhados**, **Comparar com .txt**, **Parâmetros de projeto** (antes "Parâmetros do modelo"),
  **Criar pela planilha** e **Adicionar em famílias**. Tudo o que cada uma fazia continua igual.
- Por dentro ainda são duas janelas: ao trocar entre as duas primeiras abas e as outras, a janela fecha e reabre no
  mesmo lugar e tamanho, já na aba clicada. Numa família, aparecem só Compartilhados e Comparar com .txt.
- O Conferir Entrega abre Parâmetros na aba Compartilhados.
- Falta testar no Revit (2022, 2025, 2026 e 2027): abrir Parâmetros num projeto e passar por todas as abas (a janela
  deve reabrir no mesmo lugar, também maximizada); renomear, Instância ↔ Tipo e Deletar sem uso na aba Compartilhados;
  criar pela planilha e adicionar em famílias; abrir Parâmetros numa família; abrir pelo Conferir Entrega.

## Ribbon: sai o botão Contar

- O botão **Contar** (Contar Elementos) saiu do painel **Dados do modelo**, no Revit 2022 e no 2025+. O
  **Copiar Valores** passa a ser um botão grande no lugar da pilha.
- Falta testar no Revit (2022 e 2025+): abrir a aba MetroSP, conferir o painel Dados do modelo e abrir o Copiar Valores.

## Revit 2022: Exportar IFC aceita IFC 2x3

- No Revit 2022, o Exportar IFC e o Arquivos IFC deixam de bloquear setups em **IFC 2x3** (o exportador do 2022 vai
  só até o IFC4). Nas versões 2025+ continua valendo IFC4 ou IFC 4.3. COBie segue de fora.
- Em IFC 2x3 a classificação (IfcClassificationReference) não é gravada no pós-processamento: o log avisa.
- Falta testar no Revit 2022: exportar com um setup IFC 2x3 e conferir Psets, IfcSite/IfcProject e a Localização.

## Correção: Conferir Entrega no Revit 2022

- O **Conferir Entrega** parava com "Command Failure for External Command" ao ler um setup IFC salvo pelo exportador
  do Revit 2022. Esse exportador grava a fase (`ActivePhaseId`) como objeto (`{"IntegerValue": n}`) e não como número.
  Agora a fase e o nível de Space Boundaries são lidos nos dois formatos, e em Arquivos IFC a fase é salva mantendo o
  formato original do setup.
- Falta testar no Revit 2022: Conferir Entrega e Exportar IFC com um setup salvo no 2022 (com e sem fase escolhida),
  e mudar a fase em Arquivos IFC e reabrir o setup no exportador do Revit.

# MetroSP 0.3.2 (em teste)

## Exportar IFC: aceita setup em IFC 4.3

- O Exportar IFC deixa de bloquear setups em **IFC 4.3** (ex.: "Ponte Grande IFC4X3 Armação"). Valem IFC4 (CV 2.0,
  Reference View, Design Transfer View) e IFC 4.3; só o IFC 2x3 continua com aviso. O selo do setup fica verde e o
  rodapé não conta mais "setup não é IFC4". Em Arquivos IFC, o mesmo vale para a lista e para salvar o setup.
- Falta testar no Revit (2025, 2026 e 2027): exportar com o setup IFC 4.3 e conferir Psets, classificação,
  IfcSite/IfcProject e a Localização no IFC gerado.

## Revit 2022

- O MetroSP passa a ter versão para o **Revit 2022**, além de 2025, 2026 e 2027, sempre com **instalador separado**:
  `MetroSP-Revit2022-Setup-<versão>.exe` (e `MetroSP-Revit2022-<versão>-instalador.zip`). O instalador de 2025-2027
  continua igual.
- O IFC sai pelo exportador do próprio Revit 2022, que vai até o **IFC4** (Reference View ou Design Transfer View):
  setups em IFC 4.3 avisam que esta versão do Revit não os exporta.
- Diferenças em relação ao 2025+:
  - **Categorias IFC**: o Revit 2022 não deixa o add-in gravar a tabela de categorias no modelo. A janela abre e
    edita as tabelas, mas na exportação vale o mapeamento do próprio Revit (Arquivo › Exportar › Opções › Opções IFC).
  - **Exportar para IFC**: no 2022 esse parâmetro não existe; o filtro do IFC deixa de fora o que tem
    `IfcExportAs = DontExport`. A classe IFC também vem do `IfcExportAs` (no tipo, também `IfcExportAs[Type]`).
  - **Aplicar classificação**: o resumo da seleção é atualizado quando o Revit fica ocioso (o 2022 não avisa a
    mudança de seleção).
  - **Listar compartilhados › Adicionar pasta** usa a janela de pasta do Windows antiga.
  - Sem troca automática de tema claro/escuro nas logos da Ribbon (o 2022 não tem tema escuro).
- Falta testar no Revit 2022: carregar a aba (primeira vez, com "Sempre carregar"), abrir cada janela, exportar um IFC
  com o setup IFC4 Reference View e conferir Psets, classificação, IfcSite/IfcProject e a Conferir entrega;
  Aplicar classificação com a seleção mudando; Forma online (login e download).

## Exportar IFC: o arquivo diz que passou pelo MetroSP

- O IFC exportado registra o plugin e a versão nos dois lugares que as normas preveem:
  - **cabeçalho** (ISO 10303-21): `MetroSP Revit 0.3.2` no fim do `preprocessor_version` do `FILE_NAME`. O texto do
    exportador da Autodesk fica, e o `originating_system` (de onde vieram os dados) não muda;
  - **modelo** (IFC): `IfcApplication` do MetroSP (Ação BIM · Metrô) como `LastModifyingApplication` no
    `IfcOwnerHistory` do IfcProject, com `ChangeAction` MODIFIED e a data. O Revit continua como `OwningApplication`.
- Exportar de novo o mesmo arquivo atualiza a marca, sem repetir. O arquivo `Ids/OrigemIfc.cs` é igual no Civil 3D.
- Falta testar no Revit (2025, 2026 e 2027): exportar um IFC 2x3 e um IFC4 e conferir o cabeçalho e o histórico do
  IfcProject num visualizador (Solibri, BIMcollab ou Navisworks), e abrir o IFC de novo no Revit.

## Exportar IFC: property sets por sistema

- 14 classes novas no Exportar IFC, uma por sistema: AC, BANDEJAMENTO, BOMBAS, CCO, DI, ER, EV, ILUMeTOM, PATIO,
  PSD, SAL, SCC, TEL e VENT (Psets MetroClassificacao, Metro, MetroQto e, em BANDEJAMENTO, MetroLoc).
- Correções em relação aos arquivos recebidos: tipos que o exportador não conhece (Interger, Number, Angle,
  Temperature, Current, KG), linha sem tipo em PATIO, linhas repetidas, tabs duplos e espaços sobrando.
  `Codigo Metro` passou a `CodigoMetro`, como nas outras classes; no SAL, a Potencia Nominal lê `NominalPower[Type]`.
- Falta testar no Revit: escolher cada classe no Exportar IFC, exportar um modelo de cada sistema e conferir os Psets
  no IFC (principalmente MetroClassificacao, ângulos, temperaturas e correntes).

## Projeto e Local: IfcProject, IfcSite e IfcBuilding

- O passo 1 da entrega (antes **Localização**) virou **Projeto e Local**. A janela, redimensionável, preenche:
  - **IfcProject**: nome (Número do projeto), nome longo (Nome do projeto), descrição (`IfcDescription`) e fase
    (`Project Phase`);
  - **IfcSite**: nome (`SiteName`), nome longo (`SiteLongName`), descrição (`SiteDescription`), latitude/longitude pelo
    Project Base Point (como antes) e endereço (Manage › Location);
  - **IfcBuilding**: nome (Nome do edifício), nome longo (`BuildingLongName`), descrição (`BuildingDescription`) e
    endereço (Informações do projeto);
  - **Dados do projeto · MetroSP_Projeto**: tabela propriedade/valor gravada como Pset no IfcProject (linha, lote,
    contrato...). Fica guardada no modelo.
- Os campos são os que o exportador IFC da Autodesk lê; os que não existem no modelo são criados como parâmetros
  compartilhados de texto (grupo IFC) nas Informações do projeto. A dica de cada campo diz onde ele fica no Revit.
- O **Exportar IFC** confere e completa o IFC: grava no IfcProject, no IfcSite e no IfcBuilding o que o exportador não
  gravou (o nome longo do IfcSite, por exemplo) e o Pset MetroSP_Projeto, e mostra a conferência no fim. A janela de
  exportação mostra o nome do IfcProject ao lado do Location.
- Barra **Padrão** no topo da janela: **Salvar como padrão…** guarda os dados (sem latitude/longitude) em
  `%AppData%\MetroSP\DadosProjeto`, e **Usar** preenche outro modelo com eles. É o mesmo padrão do Civil 3D (o mesmo
  arquivo serve nos dois) e aparece em **Padrões e planilhas** como "Dados do projeto IFC".
- Os dados do projeto podem ser gravados mesmo quando a latitude/longitude não pode ser calculada pelo Project Base Point.
- Falta testar no Revit (2025, 2026 e 2027): abrir Projeto e Local num modelo sem esses parâmetros e gravar (os
  parâmetros são criados?), conferir em Informações do projeto, salvar e usar um padrão, exportar o IFC e abrir no
  visualizador para ver IfcProject (Name, LongName, Description, Phase), IfcSite, IfcBuilding e o Pset MetroSP_Projeto.
  O 2027 não foi compilado aqui (falta o .NET 10); o 2025 e o 2026 compilaram sem erro.

## Corrigir escrita

- Novo botão **Dados do modelo › Corrigir Escrita**.
- Procura nos parâmetros de texto (instância e tipo; parâmetros de tipo aparecem uma vez só) dos elementos que vão para o IFC:
  - **espaços**: repetidos no meio (`Concreto  armado`), no começo ou no fim, espaço não separável e caracteres
    invisíveis (vêm do Word e do Excel), quebras de linha e tabulações;
  - **espaços errados na pontuação**: antes de vírgula, ponto, ponto e vírgula, dois-pontos, `!`, `?` e `)`, depois
    de `(`, e o que falta depois de vírgula entre palavras (`aço ,concreto` → `aço, concreto`). Listas de códigos
    (`IfcWall,IfcSlab`) ficam juntas;
  - **número e unidade**: `10m` → `10 m`, `25MPa` → `25 MPa` (mm, cm, m, km, m², m³, kg, kgf, tf, kN, kPa, MPa, GPa,
    kV, kVA, kW, kWh, Hz, rpm, °C); ordinais juntos (`1 º` → `1º`). Códigos como `E1-10m` não mudam;
  - **acentuação**: lista de palavras de projeto e as terminações que sempre levam acento (`estacao` → `estação`,
    `nivel` → `nível`, `eletrica` → `elétrica`, `padroes` → `padrões`), acentos que o Acordo tirou (`idéia`, `ítem`,
    `freqüência`). Mantém maiúsculas (`ESTACAO` → `ESTAÇÃO`), não mexe em camelCase, em palavras que também existem
    sem acento (`calculo`, `analise`, `publico`), em valores em inglês (`Section Area`) nem em caminhos e endereços;
  - **texto embaralhado** (`EstaÃ§Ã£o` → `Estação`) e **caracteres estranhos** (aspas curvas, `d´água`); letra perdida
    (`Esta??o`, `�`) aparece como **Revisar** para digitar a palavra certa.
- Tabela com filtro por coluna: valor **Atual** (com `·` nos espaços sobrando, `¤` nos especiais e `↵` nas quebras),
  **Sugestão** (dá para ajustar com dois cliques antes de gravar), o que foi achado e o motivo. **Corrigir** grava as
  linhas selecionadas numa operação só (desfazível); **Ignorar** guarda no padrão; **Não mexer na palavra...** põe a
  sigla ou o código nos termos do padrão; **Selecionar no modelo**.
- Cada regra liga e desliga (a dica de cada uma tem exemplos). O **padrão** (`%AppData%\MetroSP\Escrita`, o mesmo para
  Revit e Civil 3D) guarda as regras, os **termos que não mudam** (PTL, Pr_20_30...), as **correções próprias**
  (`plataf = plataforma`) e o que foi ignorado. Aparece também em **Padrões e planilhas** (editar e levar no .zip).
- Nomes de família e de tipo também são conferidos, mas a correção é feita no **Renomear Famílias**.
- Falta testar no Revit: abrir pelo ribbon com seleção e sem, procurar no modelo todo de um projeto grande (tempo de
  resposta), corrigir parâmetro de instância e de tipo, desfazer com Ctrl+Z, editar a sugestão na tabela, ignorar,
  "Não mexer na palavra..." e abrir o padrão pelo **Padrões e planilhas**.

# MetroSP 0.3.1

## Padrões e planilhas

- Nova entrada **Configurar › Padrões e planilhas**: um lugar só para todos os padrões salvos (classificação,
  confrontar planilha, setups, property sets e mapeamentos IFC) e as planilhas que eles usam.
  - Aba **Padrões**: tipo, nome, planilhas e situação (em laranja quando a planilha não está mais no lugar);
    **Editar...** abre o editor da ferramenta, **Excluir**, **Abrir pasta**.
  - Aba **Planilhas**: cada planilha e quais padrões a usam; **Abrir planilha**, **Abrir pasta** e **Trocar planilha...**
    (todos os padrões que usam a planilha passam a usar a nova).
  - **Exportar pacote... / Importar pacote...**: leva os padrões num .zip para a equipe, com as planilhas, se quiser.
    Ao importar, as planilhas vão para `%AppData%\MetroSP\Planilhas` e os padrões já apontam para elas.
- Falta testar no Revit: abrir pelo ribbon, editar um padrão de classificação e um setup IFC, trocar uma planilha e
  exportar e importar um pacote em outro computador.

## Classificação no IfcClassification ao exportar o IFC

- O **Exportar IFC** do MetroSP agora completa a classificação no IFC: o que o exportador do Revit deixou só como
  propriedade (parâmetros dos padrões do Verificar classificação com outro nome, `Classification.Uniformat.Number`/
  `.Description` do Classification Manager) vira `IfcClassification` + `IfcClassificationReference` +
  `IfcRelAssociatesClassification`. O que o exportador já classificou (`ClassificationCode`, Código de montagem,
  OmniClass) não é repetido. O nome da classificação e a edição vêm do padrão. No IFC 2x3 não grava. O resultado aparece
  na janela do fim da exportação.
- Só funciona para parâmetros que o exportador leva como propriedade (property sets do Revit ou do setup).
- Falta testar no Revit: exportar um modelo com um padrão de duas classificações (uma em `ClassificationCode`, outra
  em parâmetro próprio ou no Classification Manager) e abrir o IFC num visualizador para ver as duas classificações.

# MetroSP 0.3.0

## Verificar classificação com padrões e várias classificações

- **Padrões salvos:** o Verificar classificação passou a trabalhar com **padrões** (lista no topo da janela e botão
  **Editar padrões...**). Cada padrão tem uma ou mais classificações e fica em `%AppData%\MetroSP\Classificacao`;
  **Exportar/Importar** leva o padrão (.json) para outra máquina ou para a equipe. No primeiro uso, a planilha e o
  parâmetro que a classificação já usava viram o padrão "Padrão Metrô".
- **Mais de uma classificação por elemento**, com os modelos da Autodesk: `ClassificationCode`, `ClassificationCode(2)`...
  do exportador IFC ("Código:Descrição", com opção de gravar "[Nome]Código:Descrição"), parâmetros do Classification
  Manager (`Classification.Uniformat.Number`/`.Description`, OmniClass, Uniclass), Código de montagem e OmniClass do
  Revit, ou qualquer parâmetro de texto. Código e descrição podem estar num parâmetro só ou em dois.
- **Planilha com as colunas indicadas:** para cada classificação, escolhe-se a planilha (.xlsx ou .csv), a aba, a linha
  dos títulos e a coluna de Código, Descrição e, opcionais, Código pai e Categorias, com prévia das primeiras linhas.
  A ferramenta sugere as colunas pelos títulos.
- **Botão de ação em cada linha** da tabela: **Corrigir** (grava a escrita da lista), **Escolher...** (abre a lista,
  filtrada pela categoria, e grava o item escolhido), **Criar parâmetro**, **Remover fórmula** e **Mostrar** (seleciona
  o elemento e explica por que não dá para gravar). Com várias linhas selecionadas, a ação vale para todas.
- A tabela ganhou a coluna **Classificação** quando o padrão tem mais de uma; o .csv exportado leva a classificação e o
  parâmetro.
- Aplicar e Consistência continuam com a planilha de antes (um parâmetro).
- Falta testar no Revit: criar um padrão com ClassificationCode e ClassificationCode(2) em abas diferentes da mesma
  planilha; um com Classification Manager (Uniformat) e outro com Código de montagem; verificar, usar cada botão de ação
  (inclusive com várias linhas selecionadas, em tipo e em grupo) e conferir no IFC exportado as duas
  IfcClassificationReference. Conferir também os nomes dos parâmetros do Classification Manager no modelo de vocês.
## Classe IFC escolhida da lista, sem digitar

Onde se escolhe uma classe IFC, a caixa agora lista **todas as classes do IFC schema** e digitar só filtra. Classe ou
tipo fora do esquema aparece em vermelho e não deixa salvar. As listas vêm dos esquemas oficiais IFC4 (ADD2 TC1) e
IFC4X3_ADD2 (gerador em `tools/gerar-esquema-ifc.py`), o mesmo padrão do Civil 3D.

- **Categorias IFC**: a coluna Classe IFC lista as classes do IFC schema escolhido (IFC4 ou IFC 4.3) e o **Tipo
  predefinido** lista os tipos da classe da linha. Ao trocar a classe, um tipo que ela não tem é apagado.
- **Conferir Entrega** (etapa 3, Exportar como): primeiro as classes dos padrões, depois todas as do IFC schema do
  setup, com Classe e Classe.TIPO.
- **Editar property sets**: Entidades IFC deixou de ser texto livre; clicar duas vezes abre a lista de entidades do
  IFC4 e do IFC 4.3 (inclui IfcElement e os tipos, como IfcWallType) para marcar. Vale para as linhas selecionadas.
- Falta testar no Revit: abrir Categorias IFC e trocar classe e tipo nos dois esquemas (filtrar digitando, Enter,
  sair da célula com texto que não é classe); no Conferir Entrega, Definir classe com IfcWall.SHEAR; no editor de
  property sets, marcar entidades em várias linhas e salvar.

## Confrontar Planilha

Botão novo no painel **Dados do modelo**: compara os parâmetros dos elementos com uma planilha externa (.xlsx ou .csv)
e resolve as diferenças.

- Você indica o campo que identifica cada linha (Id, UniqueId, Família: Tipo, Marca, um código...) e a coluna da
  planilha com esse valor. Uma linha pode casar com vários elementos (ex.: chave pelo tipo).
- Escolhe os parâmetros a conferir (instância ou tipo) e a coluna de cada um. "Sugerir pelos títulos" casa sozinho as
  colunas que têm o nome de um parâmetro.
- A aba **Diferenças** mostra o valor no modelo, o da planilha e o que fazer: **Diferente**, **Vazio no modelo**,
  **Vazio na planilha**, **Sem o campo**, **Conflito no tipo**, **Não está no modelo** e, se marcado, **Fora da planilha**.
  Medidas são comparadas na precisão das unidades do projeto ("2,5" = "2,50 m").
- Ações nas linhas selecionadas: **Aplicar a planilha** (uma transação só, com Ctrl+Z), **Ignorar** (fica salvo no padrão),
  **Voltar a conferir** e **Selecionar no modelo**. Duplo clique também seleciona. **Relatório...** grava as linhas visíveis num .csv.
- **Padrões salvos** em `%APPDATA%\MetroSP\Confrontar\Revit`: planilha, aba, linha dos títulos, colunas, parâmetros,
  opções e as diferenças ignoradas. Exportar e importar o .json para passar a outra pessoa.
- Só elementos que vão para o IFC (FiltroIfc), como nas outras ferramentas de dados.
- Mesma janela do Confrontar Planilha do Civil 3D (código comum em `Confronto/`) e o mesmo leitor de planilha da
  Verificação de classificação (`Planilha/`).
- Falta testar no Revit: confrontar o EP.000.00 com uma planilha de quantitativo; aplicar parâmetros de instância e de
  tipo e desfazer com Ctrl+Z; medida com unidade; chave por Marca e por Família: Tipo; salvar, reabrir e importar um padrão.

# MetroSP 0.2.9

## Excluir padrões

- **Padrões do Metrô** (Conferir Entrega › Padrões...): botão **Excluir padrões...** apaga o arquivo .metrosp aberto
  (pede confirmação; os arquivos que ele indica não são apagados). Cada arquivo dos padrões ganhou um **✕** ao lado do
  Procurar, para tirá-lo dos padrões.
- **Verificar Padrões Metrô**: os arquivos opcionais (Property sets e Classes IFC) ganharam um **✕** para tirar o arquivo
  escolhido. Antes, depois de escolhido, não dava para voltar a "nenhum".
- Já existiam e continuam iguais: Apagar em Arquivos IFC, Remover em Listar Compartilhados, Remover selecionadas no
  editor de property sets e "tirar" nas regras do Filtrar e Colorir.
- Falta testar no Revit: abrir Padrões..., salvar um .metrosp de teste, excluir e conferir que a janela volta para
  "arquivos escolhidos neste computador"; no Verificar Padrões, tirar o .txt de property sets e verificar.

## Faixa MetroSP reorganizada

A aba MetroSP passou de 7 painéis para 4, na ordem do trabalho:

- **Entrega Metrô**: Conferir Entrega e, ao lado, os passos numerados da entrega IFC: **1** Localização,
  **2** Configurar (menu com Arquivos IFC e Categorias IFC), **3** Verificar Padrões (IDS), **4** Exportar IFC;
- **Classificação**: Aplicar, com Verificar e Consistência empilhados;
- **Dados do modelo**: Planilha de Dados, Editar em Massa, Filtrar e Colorir, com Copiar Valores e Contar empilhados;
- **Parâmetros e famílias**: menu Parâmetros (Gerenciar, Listar Compartilhados, Renomear Compartilhado) e Renomear Famílias.

O Sobre continua no logo, no começo da aba.

No Conferir Entrega, o botão **Pacote...** passou a se chamar **Padrões...** (e a janela, **Padrões do Metrô**): é onde se
escolhem os arquivos que o Metrô pede (IDS, setup, property sets, planilha, MetroShared).

## Todas as janelas redimensionáveis

Todas as janelas do MetroSP agora podem ser redimensionadas pela borda ou pela alça no canto, têm um tamanho mínimo e
nunca passam da altura da tela. As de formulário (Localização, Exportar IFC, Verificar Padrões, Pacote, Sobre e outras)
ganham barra de rolagem quando ficam menores que o conteúdo.

## Todas as ferramentas só com elementos que vão para o IFC

Classificação (Aplicar, Verificar e Consistência), Planilha de Dados, Editar em Massa, Filtrar e Colorir, Copiar Valores,
Contar, Verificar Padrões e Conferir Entrega usam o mesmo filtro: ficam de fora opções de projeto secundárias,
demolidos, elementos sem sólido, os com "Exportar para IFC = Não", origem interna, ponto base, ponto de levantamento
e aberturas (que saem como vazio no elemento que cortam).

## Conferir Entrega: corrigir na própria tela

- **Dados preenchidos:** ao abrir uma célula da matriz, a barra **Preencher "parâmetro" com** grava o valor nas linhas
  escolhidas (ou em todas as vazias e fora do exigido), na instância ou no tipo, e confere de novo. Quando os padrões
  têm a lista de valores aceitos, ela aparece para escolher. Ctrl+Z no Revit desfaz.
- **Editar em Massa...** ao lado (e na aba Lista) abre a ferramenta com os elementos selecionados e o parâmetro já
  escolhido, para juntar outros parâmetros, prefixos ou localizar e substituir.
- Cada pendência aponta a ferramenta que resolve: **Categorias IFC...** na classe IFC dos elementos, **Editar property
  sets...** (abre o .txt dos padrões) nos campos sem mapeamento e nos tipos errados, além das que já existiam.
- **Só os elementos dos padrões:** as etapas de dados e de classificação conferem só os elementos de classes IFC que
  fazem parte dos padrões (com algum campo exigido). Os que saem como proxy ou numa classe sem campos do Metrô aparecem
  só na etapa 3, onde **Exportar como [classe] → Definir classe** grava o "Exportar para IFC como" dos tipos e
  confere de novo.
- **Matriz mais legível:** os títulos das colunas ficam na horizontal, com a propriedade inteira (quebrando entre as
  palavras) e o property set embaixo.

## Filtros das tabelas no título das colunas

Em todas as tabelas com filtro (Gerenciar Parâmetros, Lista de compartilhados, Verificar Classificação, Copiar Valores,
Resultados do IDS e outras), o filtro passou para dentro do título de cada coluna:

- caixa **Filtrar...** logo abaixo do nome da coluna, sempre com a mesma largura dela (acompanha ao redimensionar e ao rolar
  para o lado); Esc limpa;
- botão **▾** ao lado do nome, com a lista de valores (como no Excel); **Limpar filtro** limpa também o texto;
- filtro em uso fica em laranja: a caixa com texto e o ▾ com valores escolhidos;
- corrige o ▾ largo demais, que desalinhava a linha de filtros (também no Editar Property Sets).

## Verificar Padrões Metrô (antigo Verificar Mapeamento IDS)

- **Nome novo:** o botão, as janelas e os textos passam a falar em **Padrões Metrô**. O arquivo continua sendo o `.ids` (formato IDS da buildingSMART).
- **Sem "fantasmas" na contagem:** a verificação só conta os elementos que vão de fato para o IFC. Ficam de fora
  elementos de **opção de projeto secundária**, **demolidos** ou criados numa **fase posterior** à da exportação
  (última fase do projeto, ou a do setup na Conferir Entrega), elementos **sem geometria 3D** (sistemas de vigas,
  famílias vazias ou só com linhas, como vigas no local feitas só de contorno) e os com **Exportar para IFC = Não**. Antes eles entravam nas contagens e, com duplo clique,
  selecionavam algo que no Revit aparecia só como contorno ou meio-tom.
- Os elementos deixados de fora aparecem no fim da lista, nas linhas **(fora do IFC)**, com o motivo, e num selo
  no topo; duplo clique seleciona para conferir.
- No rodapé, o duplo clique agora mostra **o que foi selecionado** (categoria, família, tipo e id).
- A Conferir Entrega usa o mesmo filtro.

# MetroSP 0.2.8

Revit 2025, 2026 e 2027. Junta a 0.2.7 (não publicada separadamente), a Conferir Entrega Metrô, a troca
instância ↔ tipo e as ferramentas de dados.

## Novo: Conferir Entrega Metrô

Botão **Conferir Entrega**, no painel novo **Entrega**. Uma janela só mostra, com semáforo, se o modelo tem tudo o que
o Metrô pede antes de exportar o IFC:

1. **Arquivos do Metrô**: IDS, setup de exportação, property sets, classes IFC, planilha de classificação e MetroShared
   achados e válidos (avisa, por exemplo, tipo `Lenght` no .txt, que sairia como texto);
2. **Parâmetros e mapeamento**: o parâmetro de cada campo existe nas categorias, com tipo compatível e o GUID do MetroShared;
3. **Classe IFC dos elementos**: proxies e elementos que não recebem nenhum campo do Metrô;
4. **Dados preenchidos**: matriz categoria × campo com a % preenchida; clicando numa célula aparecem os tipos e elementos;
5. **Classificação**: preenchida, no formato e na planilha.

Os arquivos ficam num **pacote** (`.metrosp`) que o coordenador BIM salva uma vez numa pasta compartilhada ou no Forma;
o projetista só abre o pacote. Na primeira vez, a janela aproveita os arquivos já escolhidos nas outras ferramentas.

Para corrigir: **Selecionar no Revit**, **Pintar na vista** (com **Limpar pintura** para desfazer) e botões que abrem
Parâmetros de projeto, Listar Compartilhados, Aplicar Classificação (com os elementos já selecionados), Verificar
Classificação e os detalhes do IDS. A janela fica aberta enquanto se trabalha no modelo; **Conferir de novo** atualiza.
**Exportar IFC...** abre a exportação com os arquivos do pacote e avisa se ainda há pendência.

## Novo painel Dados e ferramentas de parâmetros

- **Planilha de Dados:** escolha o escopo (seleção, vista ativa ou modelo), as categorias e os parâmetros, e
  exporte um .xlsx com uma linha por elemento e uma coluna por parâmetro (parâmetros de tipo aparecem com
  [tipo]; identificação e somente leitura em cinza). Edite no Excel e importe: a janela mostra só as células
  que mudaram, com o valor atual e o novo, e aponta o que não converte, o que é somente leitura e linhas do mesmo
  tipo pedindo valores diferentes. Nada é gravado antes de "Gravar alterações" (uma operação, Ctrl+Z desfaz).
- **Editar em Massa:** um parâmetro em muitos elementos: definir valor, acrescentar prefixo ou sufixo,
  localizar e substituir (com expressão regular) ou limpar. O texto aceita outros parâmetros entre chaves,
  por exemplo `{Nível}-{Marca}`, além de {Categoria}, {Família}, {Tipo} e {Id}. Prévia antes de gravar.
- **Filtrar e Colorir:** regras sobre parâmetros (vazio, igual, diferente, contém, começa com, termina com,
  maior, menor) combinadas com E ou OU. O resultado pode ser selecionado, isolado na vista (temporário) ou
  colorido por valor de um parâmetro, com legenda clicável; "Limpar cores" tira as cores dos filtrados.
- **Gerenciar Parâmetros** (painel Parâmetros): lista e exporta os parâmetros de projeto do modelo; cria vários
  de uma vez a partir de uma planilha (nome, tipo de dado, instância ou tipo, categorias, grupo, descrição, GUID),
  acrescentando categorias aos que já existem; e acrescenta parâmetros do arquivo de compartilhados em várias
  famílias de uma vez (abre, altera e recarrega cada família).
- **Renomear Famílias** (painel Modelagem): famílias e tipos por regra (prefixo, sufixo, substituir, maiúsculas,
  minúsculas ou modelo com {Nome}, {Família}, {Categoria} e parâmetros do tipo), com prévia, conferência de nomes
  repetidos e de caracteres que o Revit não aceita.

## Exportar IFC: classes prontas no instalador

- O instalador traz os .txt de property sets (e, quando houver, o setup .json e o mapeamento) de cada classe.
  Na janela, basta escolher a **Classe** e exportar.
- Continua dando para trocar qualquer arquivo pelo **Procurar...** (a classe passa para "Outros arquivos").
- Novo **Editar property sets...**: mostra o .txt em tabela, com as propriedades cujo parâmetro não existe no
  modelo destacadas, sugere os parâmetros do modelo, e permite acrescentar e remover propriedades. A edição de
  uma classe instalada fica salva só para o usuário, com **Restaurar padrão** para voltar ao original.
  Os selos do topo do editor filtram a tabela: o de property sets abre a escolha dos psets, o de parâmetros não
  encontrados e o de tipos não aceitos mostram só essas linhas (novo clique desliga), e o de propriedades limpa
  os filtros.
- **Classes do MetroSP no instalador:** B2, B3, B5, B7, I1, J2, K9, M1, N3 e S5 (property sets MNEPI-L20).
- **Conferência pelo padrão do exportador da Autodesk:** todo .txt incluído (classe, Procurar ou Forma) e toda
  edição manual são conferidos pelas mesmas regras do leitor do exportador IFC da Autodesk: tipo de dado
  reconhecido (senão sai como Text), cabeçalho com I/T e entidades, colunas a mais, linhas fora de um property set,
  nomes Pset_ reservados. Ao exportar com problemas, a janela oferece abrir o editor; ao salvar no editor, os tipos
  com grafia conhecida ("Lenght", "Number", "Wattage", "Percent"...) são corrigidos na hora.
- Os .txt das classes B2, B3, M1, N3 e S5 foram corrigidos nesse padrão (revisão registrada no cabeçalho de cada um).
- **Classe pelo nome do modelo:** a janela já abre com a classe indicada pelos dois últimos caracteres do nome do
  modelo (ex.: `...B3.rvt` → B3), regra que fica no `Nomenclatura.txt` e pode ser mudada.
- **Forma online:** dá para entrar com a conta Autodesk e escolher uma pasta do Forma direto pela internet, sem o
  Desktop Connector. Os arquivos dela são baixados e, a cada exportação, só o que mudou no Forma é atualizado.
  O **Client ID** do app do MetroSP na Autodesk é informado no próprio plugin (botão "Client ID..." na janela
  Exportar IFC), sem editar arquivo.

## Correção: Revit 2027 só exportava o primeiro bloco de cada property set

No 2027 os property sets do .txt são carregados no modelo, onde cada um é guardado pelo nome. Nos .txt com um
bloco "Metro" (ou MetroLoc, MetroQto...) por entidade, só um bloco sobrevivia, e as outras entidades saíam sem
esses psets (no J2, só as paredes recebiam Metro/MetroLoc/MetroQto). Agora os blocos de mesmo nome são juntados
antes de carregar, com as entidades unidas. O .txt original não muda. No 2025 e no 2026 nada muda.

No modelo, cada propriedade aponta para um só parâmetro. Quando o .txt tira a mesma propriedade de parâmetros
diferentes conforme a entidade (MetroLoc.Nivel = Reference Level na viga e Level na laje), ou quando ela só existe
em parte dos blocos, a exportação cria um parâmetro auxiliar temporário (MetroSP_<Pset>_<Propriedade>), preenche
em cada elemento com o parâmetro certo para a entidade dele e o remove no fim. Assim lajes, sapatas, paredes e
lances de escada voltam a receber o nível, o desvio, o material e as quantidades do bloco deles, e nenhuma entidade
recebe propriedade de bloco de outra.
J2: o MetroLoc dos aparelhos de apoio passou para IfcMechanicalFastener (estava IfcDiscreteAccessory).

## Categorias IFC: categoria do Revit → classe IFC, em IFC4 e IFC 4.3

- Novo botão **Categorias IFC** na aba MetroSP: uma tabela para IFC4 (4.0.2.1, o que o Revit grava) e outra
  para IFC 4.3, com a lista de classes de cada esquema (o 4.3 traz IfcBuiltElement, IfcRail, IfcCourse,
  IfcEarthworksFill, IfcGeotechnicalStratum...). Por padrão aparecem só as categorias usadas no modelo, com a
  contagem; as linhas diferentes entre os dois esquemas ficam destacadas.
- Sem tabela salva, começa pelo mapeamento que o modelo usa hoje. Salvar grava a sua cópia em
  %AppData%\MetroSP\IFC\Categorias (IFC4.txt e IFC4x3.txt); também dá para importar, exportar e copiar de um
  esquema para o outro.
- Na exportação, a tabela do esquema do setup é gravada no modelo como modelo de mapeamento do Revit
  ("MetroSP IFC4" ou "MetroSP IFC4x3") e fica ativa. Sem "gravar GUID", o modelo volta como estava; com ele, o
  modelo de mapeamento MetroSP fica salvo e ativo no modelo.

## Arquivos IFC: gestão dos setups, property sets e mapeamentos

- Novo botão **Arquivos IFC** na aba MetroSP (e o link **Gerenciar arquivos...** na janela Exportar IFC).
  Uma aba para cada tipo de arquivo, com as classes à esquerda e de onde vem cada arquivo: seu, do Forma online
  ou instalado. Os seus valem no lugar dos outros.
- **Vários de uma vez**: arraste (ou escolha) quantos .json e .txt quiser. O MetroSP reconhece o tipo pelo
  conteúdo e a classe pelo nome do arquivo, mostra uma lista para conferir e trocar, e avisa o que substitui.
- **Setup**: as opções principais aparecem abertas e editáveis (versão IFC, fase, space boundaries, divisão por
  nível, só visíveis, quantidades, GUID e psets padrão, do Revit e de material). Salvar um setup instalado ou do
  Forma grava uma cópia sua. Coordenadas e EPSG continuam definidos pelo MetroSP na exportação.
- **Property sets**: cada classe é conferida no modelo aberto: parâmetros não encontrados e propriedades sem
  valor em parte dos elementos, por property set. "Editar..." (ou clique duplo num property set) abre o editor.
- Voltar ao padrão, apagar, mudar a classe e salvar uma cópia. Toda versão substituída ou apagada fica guardada em
  %AppData%\MetroSP\IFC\Anteriores.

## Editor de property sets: verificar e corrigir no mesmo lugar

- Árvore à esquerda com cada property set e as entidades IFC dele; escolher um item filtra a grade, e o número
  laranja mostra quantas linhas têm problema.
- Nova coluna **No modelo**: em quantos elementos da entidade o parâmetro tem valor (ex.: "212 de 240").
  Linhas sem valor em parte dos elementos também ficam destacadas, com um selo próprio que filtra.
- Ao escolher uma linha com problema, um painel explica o motivo e sugere parâmetros de nome parecido que existem
  no modelo, com a contagem; um clique troca.

## Exportar IFC: EPSG em branco

- Nova opção "Exportar com o EPSG em branco" na seção Localização, ligada por padrão. Com ela ligada, o IFC sai sem EPSG (sem IfcMapConversion/IfcProjectedCRS) e a janela não cobra mais o dado GIS do Location.
- Desligada, o IFC mantém o EPSG do setup ou o que o exportador do Revit gerar.

## Interface mais limpa

- **Exportar IFC** reorganizada: classe numa linha (com o motivo da escolha e o Forma online como link ao lado),
  setup, property sets e mapeamento numa lista com o nome do arquivo, um selo verde ou laranja e o botão de ação
  (o caminho completo aparece ao passar o mouse; em "Outros arquivos" os caminhos ficam editáveis), localização
  só conferindo com o botão Localização IFC..., pasta e nome do .ifc num campo só e os avisos somados no rodapé.
- O mesmo padrão nas outras janelas: títulos de seção e rótulos em maiúsculas pequenas, arquivos como lista com
  selos no Verificar mapeamento IDS (com resumo no rodapé) e textos de ajuda movidos para as dicas.

## Sobre

- Clicar na logo Ação BIM · Metrô SP, no fim da aba MetroSP, abre a janela **Sobre** (o botão Sobre do painel
  Geral abre a mesma): versão, data da compilação, Revit em uso, ferramentas por painel e pasta de instalação,
  com **Copiar informações** para mandar ao suporte.

## Listar Compartilhados: trocar instância ↔ tipo

Novo botão **Instância ↔ Tipo...** no rodapé. Troca os parâmetros selecionados de instância para tipo ou de tipo
para instância, mantendo as categorias e o grupo, numa transação só (Ctrl+Z desfaz).

- **No projeto**, os valores já preenchidos vão junto: *instância → tipo* grava em cada tipo o valor das suas
  instâncias; *tipo → instância* grava em cada instância o valor do seu tipo.
- Antes de trocar, a janela mostra o que acontece com cada parâmetro e **avisa onde há perda de valores**: tipos
  cujas instâncias têm valores diferentes (escolha entre ficar com o valor mais comum ou deixar o tipo vazio),
  elementos sem tipo (ambientes, áreas...) e tipos sem nenhuma instância no modelo.
- Elementos de famílias que já trazem o parâmetro definido dentro da família não mudam: vale o que a família define
  (para trocar nesses, troque na família e recarregue). Parâmetros que existem só em famílias ou etiquetas
  carregadas não são trocados daqui.
- **Na família**, usa a mesma troca do editor de famílias; os valores de cada tipo da família são mantidos.
  Ao passar de instância para tipo, os projetos onde a família for recarregada perdem os valores por instância.
- **Fórmulas** (só existem na família) ficam como estão. Como um parâmetro de tipo não pode usar parâmetros de
  instância na fórmula, a janela não troca, e diz por quê: para tipo, um parâmetro cuja fórmula usa parâmetros de
  instância; para instância, um parâmetro que aparece na fórmula de parâmetros de tipo. Selecionando os parâmetros
  ligados por fórmula juntos, eles são trocados na ordem que o Revit aceita. No projeto não há fórmulas; tabelas e
  filtros que usam o parâmetro continuam funcionando, porque o parâmetro é o mesmo.
- No fim, um resumo diz quantos valores foram regravados, perdidos ou não gravados (somente leitura, grupos,
  elementos de outro usuário).


# MetroSP 0.2.6 (anterior)

Revit 2025, 2026 e 2027.

## Correção: Exportar IFC no Revit 2027 sem os property sets do usuário

No Revit 2027 o exportador IFC passou a ler os property sets definidos pelo usuário do próprio modelo
(os que a janela nativa cria ao importar o .txt) e ignora o .txt passado pelo plugin. Por isso os psets só
saíam depois de configurados na janela nativa.

Agora, no 2027, o .txt escolhido é carregado no modelo logo antes de exportar, no lugar dos psets que já
estavam lá. Se o setup não grava o GUID IFC, isso é desfeito no fim e o modelo fica como estava; se grava,
os psets do .txt ficam salvos no modelo (o relatório avisa). No 2025 e no 2026 nada muda.

# MetroSP 0.2.5 (anterior)

Revit 2025, 2026 e 2027.

## Correção: Renomear Compartilhado com famílias

Na 0.2.4 o comando parava com "O Revit não deixou liberar o nome antigo no projeto". O Revit só deixa apagar
o registro do nome antigo quando nenhuma família carregada usa mais o parâmetro, e a ferramenta tentava apagar
com as famílias ainda usando. Agora são duas passagens:

1. em cada família (e nas aninhadas), o parâmetro vira parâmetro de família, com valores e fórmulas, e ela é
   recarregada assim: o projeto deixa de usar o parâmetro;
2. o nome antigo é apagado no projeto;
3. em cada família o parâmetro volta a ser compartilhado, com o mesmo GUID e o nome novo, e ela é recarregada de novo;
4. o vínculo do projeto e os valores são refeitos, e o nome é conferido.

Também corrigido o "Loaded Family Editing failed" em aninhadas: as famílias preparadas ficavam abertas, e o Revit
não abre a mesma família duas vezes (acontecia com aninhadas compartilhadas, que também estão no projeto).
Agora cada família é guardada num arquivo temporário e reaberta só quando precisa.

Se ainda assim o nome antigo não puder ser liberado, a mensagem diz quais famílias continuam usando o parâmetro.

# MetroSP 0.2.4

Revit 2025, 2026 e 2027.

## Renomear Compartilhado mais rápido com famílias aninhadas

Com "Incluir famílias aninhadas" marcado, a 0.2.3 abria todas as famílias aninhadas de todas as famílias,
em todos os níveis, mesmo as que não tinham nada a ver com o parâmetro. Agora só abre as aninhadas de uma
família quando o parâmetro está registrado nela (o que acontece quando alguma aninhada o usa). Nas famílias
sem o parâmetro, ela é aberta, conferida e fechada na hora. Em modelos com muitas famílias aninhadas, o número
de aberturas cai muito; a contagem aparece no relatório final ("N abertas").

## Listar Compartilhados: base MetroShared e deletar parâmetros sem uso

**Base MetroShared.** O botão **Base** (acima da tabela, na aba "Parâmetros do arquivo") escolhe o .txt do
MetroShared. É a mesma base que as outras ferramentas já usam (Aplicar Classificação, por exemplo), guardada em
`%AppData%\MetroSP\referencias.ini`; quem já tinha definido a base em "Comparar com .txt" não precisa fazer nada.
A nova coluna **Padrão (base)** mostra, para cada parâmetro:

- **No padrão**: mesmo GUID e mesmo nome do MetroShared;
- **Nome diferente do padrão**: mesmo GUID, outro nome;
- **Duplicado**: o MetroShared tem um parâmetro com esse nome, mas com outro GUID (ou o do arquivo não é compartilhado);
- **Parecido com o padrão**: o nome só difere em acentos, maiúsculas, espaços ou pontuação (ex.: "Classificacao");
- **Fora do padrão**: não existe no MetroShared;
- **nome repetido no arquivo**: dois parâmetros do arquivo com o mesmo nome (ou parecido) e GUIDs diferentes.

Os contadores no topo e a cor da linha acompanham. A coluna também sai no .csv.

**Verificar uso e Deletar sem uso**, no rodapé. *Verificar uso* preenche a coluna **Uso**:

- no projeto: valor preenchido em algum elemento (ou tipo, para vínculo de tipo) das categorias vinculadas,
  campo de alguma tabela, regra de algum filtro;
- na família: valor em algum tipo, fórmula própria ou na fórmula de outro parâmetro, associação a parâmetros de
  elementos (visibilidade, material, família aninhada...), rótulo de cota.

*Deletar sem uso...* deleta os selecionados (sem seleção: os visíveis com os filtros atuais) que a verificação
achou sem uso, depois de mostrar a lista para confirmar. Os que estão em uso nunca são deletados. Para auditar os
duplicados fora do MetroShared: filtre a coluna Padrão, clique em Verificar uso e em Deletar sem uso.

Cuidados que a ferramenta toma:

- No projeto, deletar remove o vínculo de parâmetro de projeto, como o **Remover** do diálogo Parâmetros de projeto.
  O registro do GUID fica no arquivo, porque alguma família carregada pode usá-lo.
- Parâmetros que existem só por causa de famílias ou etiquetas carregadas não são deletados daqui: apague nas famílias.
- Em famílias de anotação (etiquetas, carimbos) nada é deletado, porque a API não mostra os rótulos de texto.
- Tudo numa transação: Ctrl+Z no Revit desfaz depois de fechar a janela.

# MetroSP 0.2.3

Revit 2025, 2026 e 2027.

## Exportar IFC: cada arquivo é indicado pelo usuário

Sai a pasta padrão (e o botão "Criar pasta padrão"). A janela agora pede, cada um com o seu **Procurar...**:

1. o **setup de exportação** (.json);
2. o **.txt de property sets**;
3. a **tabela de mapeamento de parâmetros** (.txt, opcional).

Os arquivos podem estar em qualquer pasta: computador, rede ou Forma pelo Desktop Connector. Ao escolher
o setup, os .txt indicados nele são preenchidos quando achados nesta máquina, e dá para trocar qualquer um.
As três escolhas ficam lembradas para a próxima exportação. A conferência dos parâmetros contra o modelo continua.

# MetroSP 0.2.2

Revit 2025, 2026 e 2027.

## Novo: janela de progresso nas operações longas

Uma janela mostra a etapa atual, a barra com "N de total", o item em andamento, o tempo decorrido
com estimativa do que falta e um histórico do que já foi feito. Ela continua se atualizando mesmo
com o Revit ocupado.

- **Renomear Compartilhado:** as 4 etapas (renomear dentro das famílias, guardar valores, recarregar,
  restaurar), com o nome de cada família aberta, aninhadas incluídas. Tem botão **Cancelar**: o que já foi
  feito é desfeito e o projeto fica como estava.
- **Verificar Classificação:** análise elemento a elemento e as famílias abertas para ler fórmulas.
- **Remover fórmula da família:** uma família por vez, com **Cancelar** (as que já foram recarregadas ficam).
- **Gravar classificação** (Aplicar, Verificar e Consistência): aparece a partir de 200 elementos, incluindo
  o empréstimo de elementos no worksharing.
- **Exportar IFC:** indica que a exportação está em andamento (o exportador do Revit não informa a porcentagem).

# MetroSP 0.2.1

Revit 2025, 2026 e 2027.

## Correção: Renomear Compartilhado

Na 0.2.0, o comando dizia que tinha renomeado, mas o projeto continuava mostrando o nome antigo.
O projeto (e cada família) guarda um único registro do nome por GUID, e recarregar as famílias
renomeadas não mudava esse registro.

- O nome antigo agora é liberado no projeto e nas famílias antes de o GUID voltar com o nome novo.
- Projeto e famílias são renomeados numa única operação: famílias renomeadas, registro antigo apagado,
  famílias recarregadas, vínculo refeito e valores e tabelas restaurados.
- No final o nome é conferido. Se o projeto não mostrar o nome novo, tudo é desfeito e a mensagem diz o motivo
  (por exemplo, uma família in-place ou protegida que tem o parâmetro). Não aparece mais "renomeado" sem ter renomeado.
- Rodar de novo num modelo em que as famílias já foram renomeadas, mas o projeto não, agora resolve.

# MetroSP 0.2.0

Revit 2025, 2026 e 2027.

## Novo: Exportar IFC (painel IFC / IDS)

Exporta o IFC com o setup do exportador (.json) e o mapeamento de property sets (.txt) guardados
fora do modelo, para toda a equipe usar a mesma configuração.

- **Origem dos arquivos:** projeto do Autodesk Forma, lido pelo Desktop Connector, ou uma pasta no computador ou na rede.
- **Pasta padrão no Forma**, igual em todo projeto: `Project Files\BIM\Configuracoes IFC\` com `Setups\`,
  `PropertySets\` e `Mapeamentos\`. Escolhendo `Civil.json`, o `Civil.txt` de cada subpasta entra sozinho.
  O botão **Criar pasta padrão** monta a estrutura num projeto novo. O caminho pode ser alterado na janela.
- **Projeto na nuvem:** se o modelo aberto está no Forma, o projeto dele já vem selecionado.
- **Conferência antes de exportar:** lê o .txt e avisa quais parâmetros do mapeamento não existem no modelo.
- **Vista:** exporta o modelo inteiro ou só o que está visível numa vista 3D. Pasta de saída e nome do arquivo ficam lembrados.
- **Caminho do .txt:** o caminho gravado no .json, que é o da máquina de quem salvou, é corrigido sozinho procurando o arquivo pelo nome.

Limites: vínculos não são exportados; a classificação e o endereço do projeto usados são os salvos no modelo;
o modelo de mapeamento guardado no .rvt (Revit 2025+) é ignorado, para valer o .txt.

## Também nesta versão

- **Renomear Compartilhado:** renomeia o parâmetro também em todas as famílias carregadas, inclusive as
  aninhadas, mantendo o GUID e os valores.
- **Verificar Classificação:** remove a fórmula da família quando ela impede gravar a classificação.
- **Ribbon:** painel com as logos.
- **Janelas de lista:** filtros por coluna, seleção por categoria e "aplicar só as selecionadas".

## Instalar

Feche o Revit, extraia o .zip e rode `Instalar.bat`. Ele instala em todas as versões do Revit
encontradas no computador. Para desinstalar: `Instalar.bat -Desinstalar`.
