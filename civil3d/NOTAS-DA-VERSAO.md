# Notas da versão: MetroSP para Civil 3D

## 0.12.0 (12/11/2026)

- **Ferramentas reorganizadas como no plugin do Revit** (proposta aprovada em 12/11):
  - **Ribbon** com 5 painéis e 11 entradas. Só a **Entrega Metrô** é numerada: **1 Entrega passo a passo**, **2 Projeto e
    Local** (o antigo Projeto IFC) e **3 Exportar IFC**, com a setinha para exportar **só a seleção** ou **só os
    alinhamentos** (IFC separado). Depois vêm **Acompanhar** (Painel MetroSP), **Classificação** (Verificar), **Dados do
    modelo** (Property sets, Confrontar planilha, Corrigir escrita) e **Configurar padrões**, com botões pequenos
    (Padrões e planilhas, Configurar IFC, Exportador e Forma). O painel Forma virou uma janela só (Entrar, Sair,
    Sincronizar e Conta), o Sobre fica no clique do selo e a Pasta foi para dentro do Exportador. Os comandos antigos
    continuam valendo.
  - **Entrega passo a passo** (`MSP_ENTREGA`): janela única, redimensionável, com as 7 etapas em cima numa linha fina
    com setinhas (verde = pronta, laranja com o número de pendências, a aberta sublinhada): **1 Exportador**,
    **2 Property sets**, **3 Classe IFC**, **4 Dados**, **5 Classificação**, **6 Alinhamentos** e **7 Local e
    exportar**. Embaixo, à esquerda, os cartões de pendência com os botões que resolvem (Criar no desenho, Anexar,
    Usar o perfil, Sincronizar, Selecionar...) e, à direita, a ferramenta da etapa embutida (Property sets, Classes
    IFC, Corrigir escrita · Confrontar planilha em abas, Verificar classificação, Projeto e Local). Rodapé com
    Passo anterior · Salvar e conferir · Próximo passo. Abre na primeira etapa com pendência.
  - O que cada etapa confere, só nos objetos que vão para o IFC: extensão IFC 4.3 instalada, desenho salvo e
    configuração do Forma em dia (1); property sets que o mapeamento IFC pede, no desenho e anexados (2); classes fora
    do esquema IFC (3); erros de escrita (4); classificação pela planilha do padrão (5); alinhamento com mais de um
    perfil sem escolha guardada ou sem perfil (6); nome do IfcProject e do IfcSite e as instalações (7).
  - **Alinhamentos** (só no Civil 3D): tabela com o perfil de cada alinhamento; o que for escolhido ali fica guardado
    para o desenho e a exportação não pergunta mais.
  - **Painel MetroSP** (`MSP_PAINEL`): painel encaixável com as 7 etapas e o semáforo da última conferência do desenho
    ativo; clique numa etapa para abrir a entrega nela.
- Falta testar no Civil 3D (2025, 2026 e 2027): a Ribbon nova (inclusive a setinha do Exportar IFC), abrir a Entrega
  num desenho real (MB-20) e conferir as 7 etapas, cada ferramenta embutida (salvar por dentro e pelo Salvar e
  conferir), os botões Anexar, Criar no desenho e Usar o perfil, exportar pela etapa 7 e o Painel MetroSP encaixado.
  Compilado aqui (R25 e R26), sem teste visual: o Civil 3D não roda neste ambiente.

- **Exportar IFC**: o arquivo registra o plugin e a versão nos dois lugares que as normas preveem:
  - **cabeçalho** (ISO 10303-21): `MetroSP Civil 3D 0.11.0` no fim do `preprocessor_version` do `FILE_NAME`. O texto da
    Autodesk fica, e o `originating_system` (de onde vieram os dados) não muda;
  - **modelo** (IFC): `IfcApplication` do MetroSP (Ação BIM · Metrô) como `LastModifyingApplication` no
    `IfcOwnerHistory` do IfcProject, com `ChangeAction` MODIFIED e a data. O Civil 3D continua como `OwningApplication`.
  - Exportar de novo atualiza a marca, sem repetir. O IFC só de alinhamento também leva a versão no cabeçalho.
    O arquivo `Ifc/OrigemIfc.cs` é igual no Revit.
  - Testado aqui no IFC de teste `MB-20.00.00.00-4U1-002-3.autodesk.ifc`. Falta testar no Civil 3D (2025, 2026 e 2027):
    exportar e conferir o cabeçalho e o histórico do IfcProject num visualizador (Solibri, BIMcollab ou Navisworks).
- **Exportar IFC leva todos os property sets**: o exportador da Autodesk só grava os property sets que estão no
  mapeamento (`IFCInfraExportPropertyMapping.json` e `.csv`). Os criados no Style Manager (ex.: Metro,
  MetroClassificacao, MetroLoc, MetroQto) ficavam de fora. Agora, a cada exportação, o plugin acrescenta às cópias do
  mapeamento gravadas para o desenho todo property set anexado a algum objeto que vai para o IFC, com todas as
  propriedades. O pacote ativo (Configurar IFC) não muda. A linha de comando lista os que entraram.
- Falta testar no Civil 3D: exportar o MB-20.03.01.00-4D2-001-1 e conferir no IFC os Psets Metro, MetroClassificacao,
  MetroLoc e MetroQto nos tubos e caixas, com os valores preenchidos.

- Botão **Projeto IFC** (painel IFC 4.3, comando `MSP_IFCPROJETO`): abre só a aba Projeto do Configurar IFC, numa
  janela própria e redimensionável. Os campos são os mesmos do plugin do Revit:
  - **IfcProject**: nome, nome longo, descrição e fase;
  - **IfcSite**: nome, nome longo, descrição, latitude, longitude, elevação, matrícula, coordenadas e **endereço** (novo);
  - **Instalações** (a linha como IfcRailway, cada estação como IfcBuilding): agora com **descrição** e **endereço** na
    tabela. A instalação que a Autodesk já gravou com o mesmo nome também recebe nome longo, descrição e endereço
    (antes só as novas recebiam);
  - **Dados do projeto · MetroSP_Projeto**.
- Barra **Padrão** no topo: **Salvar como padrão…** guarda os dados (sem latitude/longitude) em
  `%AppData%\MetroSP\DadosProjeto`, e **Usar** preenche com eles. É o mesmo padrão do Revit: um padrão salvo no Revit
  serve no Civil 3D (o IfcBuilding dele entra na tabela de instalações) e vice-versa. Aparece em **Padrões e planilhas**
  como "Dados do projeto IFC".
- O painel da aba Projeto é o mesmo arquivo nos dois plugins (`Ui/DadosProjetoPainel.cs`, `Ifc/DadosProjetoIfc.cs`).
- Falta testar no Civil 3D (2025, 2026 e 2027): botão novo no ribbon, salvar e usar um padrão (inclusive um salvo no
  Revit), exportar e conferir no IFC o endereço do IfcSite e das estações e a descrição de uma instalação que a
  Autodesk já grava. O R26 (.NET 10) não foi compilado aqui; o R25 compilou sem erro.

- **Corrigir escrita** (painel Dados, comando `MSP_ESCRITA`).
- Procura nos property sets (propriedades de texto), no nome e na descrição dos objetos que vão para o IFC:
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
- Propriedades automáticas e de fórmula aparecem como **Não grava** (corrigir na origem).
- Falta testar no Civil 3D 2025/2026/2027: abrir pelo ribbon com seleção e sem, procurar no desenho todo, corrigir
  property set, nome e descrição, desfazer com U, editar a sugestão na tabela, ignorar e abrir o padrão pelo
  **Padrões e planilhas**.

## 0.11.0 (em teste)

- **Property sets** (painel Dados, comando `MSP_PSETS`): monta os property sets do próprio Civil 3D (as mesmas
  Property Set Definitions do Style Manager) e anexa aos objetos. Não existe ferramenta nativa para isso além do Style
  Manager, que cria um property set e uma propriedade de cada vez, sem importar de planilha.
  - **Aba Montar**: lista dos property sets do desenho, com nome, descrição, a quais tipos de objeto se aplica
    (alinhamento, corredor, superfície, tubos, sólidos, blocos... ou todos) e as propriedades com tipo (Texto, Inteiro,
    Real, Sim/Não) e valor padrão. Tabela com filtro por coluna e situação de cada um: Novo, Muda ou No desenho.
  - **Importar planilha** (.xlsx ou .csv, uma linha por propriedade): a janela mostra a aba, a linha dos títulos e
    pede a coluna de cada informação (Property set e Propriedade obrigatórias; Tipo, Valor padrão, Aplica-se a,
    Descrição e IFC opcionais), com prévia e a contagem do que vai entrar. Cria só a estrutura: os valores dos objetos
    não são preenchidos. As colunas indicadas ficam lembradas (`%APPDATA%\MetroSP\Civil3D\PropertySets`) para a
    próxima planilha do mesmo padrão. Também lê o `IFCInfraExportPropertyMapping.csv` da Autodesk. **Exportar
    planilha** salva a lista em .csv para editar no Excel ou levar a outro desenho.
  - **Do mapeamento IFC**: traz os property sets que o mapeamento do exportador (Configurar IFC) espera achar no desenho.
  - **Criar no desenho** com **Já anexar aos objetos** (ligado por padrão): além de criar, anexa os property sets
    criados ou alterados aos objetos que vão para o IFC (da seleção, se houver), conforme o "Aplica-se a". Assim já
    aparecem em Propriedades › Extended Data, sem passar pela aba Anexar.
  - **Criar no desenho**: cria os novos e completa os que mudaram. Nada é apagado e o tipo de uma propriedade que já
    existe não muda (os valores dos objetos se perderiam).
  - **Levar para o IFC** (por property set): acrescenta o Pset ao mapeamento ativo do exportador da Autodesk (modelo no
    `IFCInfraExportPropertyMapping.json`, com a entidade IFC e o tipo de cada propriedade, e a origem `PropertySet` no
    `.csv`).
  - **Aba Anexar aos objetos**: só objetos que vão para o IFC (mesmo filtro das outras ferramentas), da seleção ou do
    desenho todo, com os property sets já anexados e o que falta. Anexa aos visíveis na tabela, respeitando os tipos
    de objeto de cada property set.
  - Falta testar no Civil 3D: abrir no MB-20 e no 4D2 (que já tem property sets), conferir se a lista bate com o Style
    Manager, criar um property set só para corredores e outro para todos, importar uma planilha indicando as colunas (e reabrir para ver se as colunas foram lembradas), anexar em sólidos,
    tubos e alinhamentos, ver os valores na paleta Propriedades, Desfazer (U) e exportar o IFC para ver se o Pset chega.
- **Padrões e planilhas** (painel Padrões, comando `MSP_PADROES`): um lugar só para todos os padrões salvos
  (classificação, confrontar planilha e exportação IFC) e as planilhas que eles usam.
  - Aba **Padrões**: tipo, nome, planilhas e situação (em laranja quando a planilha não está mais no lugar);
    **Editar...** abre o editor da ferramenta, **Excluir**, **Abrir pasta**.
  - Aba **Planilhas**: cada planilha e quais padrões a usam; **Abrir planilha**, **Abrir pasta** e **Trocar planilha...**
    (a planilha mudou de lugar ou de nome: todos os padrões que a usam passam a usar a nova).
  - **Exportar pacote... / Importar pacote...**: leva os padrões selecionados (ou todos) num .zip para a equipe, com as
    planilhas, se quiser. Ao importar, as planilhas vão para `%APPDATA%\MetroSP\Planilhas` e os padrões já apontam para elas.
- Falta testar no Civil 3D: abrir pelo ribbon, editar um padrão de classificação, trocar uma planilha e exportar e
  importar um pacote em outro computador.
- **Classificação no IfcClassification**: o pós-processamento do IFC (Exportar IFC) agora transforma a classificação
  gravada em property set em `IfcClassification` + `IfcClassificationReference` + `IfcRelAssociatesClassification`,
  como o exportador do Revit faz. Lê as classificações de todos os padrões salvos e também os nomes da Autodesk
  (`ClassificationCode`, `ClassificationCode(2)`..., `Classification.X.Number`/`.Description`). O nome da classificação
  vem do padrão (ou do `[Nome]` no valor) e a edição do padrão; o property set continua no IFC. Objeto que já tem a
  mesma referência não é repetido. No IFC 2x3 não grava (só avisa no registro).
- Falta testar no Civil 3D: exportar o MB-20 com classificação em dois property sets e abrir o IFC num visualizador
  (BIMcollab Zoom, Solibri ou o Navisworks) para ver a aba de classificação de cada objeto.
- **Configuração inicial da equipe no padrão do plugin** (pasta Padrao): IfcInfraConfiguration.json, IFCInfraExportPropertyMapping.json e .csv como vieram da equipe; o IfcInfraExportMapping.json juntou o da equipe com as regras do plugin, com correções: vírgulas sobrando e "MapShapeCode" repetido (o arquivo não abria como JSON), "fcTrackElement" → IfcTrackElement.VEHICLESTOP, "IfcSlabTypeEnum.BASESLAB" → IfcSlab.BASESLAB, espaço antes de IfcSlab.TRACKSLAB, U-3D-OCUPACAO_VP com "--------" → IfcBuiltElement (o que já saía; IfcBuiltElement não tem tipo no IFC 4.3, então ".USERDEFINED" também saiu da regra ^G-3D-), U-3D-TRILHO_AMV antes de U-3D-TRILHO. Todas as classes conferidas no esquema IFC4X3_ADD2.
- **Classe IFC escolhida numa lista** (Configurar IFC > Classes IFC): a lista traz todas as classes e Classe.TIPO do esquema escolhido na configuração da Autodesk (IFC 4.3 ou IFC4), geradas do esquema oficial. Digitar só filtra (ex.: TRACKSLAB); não dá mais para escrever uma classe solta. Aplicar vale para todas as regras selecionadas. Regra com classe fora do esquema aparece em vermelho e o Salvar avisa quais são antes de gravar.
- Falta testar no Civil 3D: exportar com a configuração inicial da equipe (pasta Padrao) e conferir as classes no Iris; em Configurar IFC > Classes IFC, filtrar digitando, aplicar em várias regras e salvar.

## 0.10.0

- **Instalador com a identidade MetroSP** (`MetroSP-Civil3D-Setup-0.10.0.exe`), igual ao do Revit: instala o bundle só para o usuário, sem administrador, em `%APPDATA%\Autodesk\ApplicationPlugins\MetroSP.bundle`, e serve para o Civil 3D 2025, 2026 e 2027. Aparece em Aplicativos instalados para desinstalar. O `MetroSP.settings.json` (Forma/APS) só é gravado na primeira instalação, para não apagar o que já foi preenchido. O zip com o bundle e o `instalar.bat` continua saindo junto. Gerado por `build.ps1`.
- Falta testar no Windows: instalar por cima da 0.9 (zip), abrir o Civil 3D 2025/2026/2027 e ver a aba MetroSP; desinstalar e conferir se a pasta do bundle sai.

- **Verificar classificação** (painel Classificação, comando `MSP_CLASSVERIFICAR`): confere a classificação dos objetos
  que vão para o IFC (mesmo filtro das outras ferramentas) com planilhas externas, e propõe as correções.
  - **Padrões salvos** em `%APPDATA%\MetroSP\Civil3D\Classificacao`, com **Exportar/Importar** (.json) para a equipe.
  - **Mais de uma classificação por objeto**, com os nomes da Autodesk: `ClassificationCode`, `ClassificationCode(2)`...
    ("Código:Descrição", com opção de "[Nome]Código:Descrição") ou código e descrição separados como no Classification
    Manager (`Classification.Uniformat.Number`/`.Description`, OmniClass, Uniclass). Cada classificação fica num
    property set (padrão `Classification`) e numa propriedade de texto, escolhidos na janela.
  - **Planilha com as colunas indicadas**: para cada classificação, planilha (.xlsx ou .csv), aba, linha dos títulos e
    a coluna de Código, Descrição e, opcional, Tipos ou layers (aceita `*`), com prévia. As colunas são sugeridas pelos títulos.
  - **Botão de ação em cada linha**: **Corrigir**, **Escolher...** (lista filtrada pelo tipo e layer do objeto),
    **Anexar property set** (cria a definição, se não existir, e anexa ao objeto) e **Mostrar** (seleciona e aproxima).
    Com várias linhas selecionadas, a ação vale para todas. Também há **Aplicar todas**, **Aplicar selecionadas** e
    exportação em .csv.
- Leitor de planilha e escolha de colunas comuns com o plugin do Revit (`src/Planilha`).
- Falta testar no Civil 3D: abrir a janela no MB-20, criar um padrão com duas classificações em abas da mesma planilha,
  anexar o property set em corredores e sólidos, escolher e corrigir; conferir Desfazer (U), Mostrar (zoom) e se o
  property set `Classification` chega ao IFC exportado.

- **Confrontar planilha** (painel Dados, `MSP_CONFRONTAR`): compara os dados dos objetos com uma planilha externa (.xlsx ou .csv).
  - Você indica o campo que identifica cada linha (Handle, Nome, uma propriedade de property set...) e a coluna da planilha com esse valor; depois escolhe os dados a conferir (propriedades dos property sets, Nome, Descrição, Layer) e a coluna de cada um. "Sugerir pelos títulos" casa sozinho as colunas que têm o nome de uma propriedade.
  - A aba Diferenças mostra cada caso com o valor no modelo, o da planilha e o que fazer: **Diferente**, **Vazio no modelo**, **Vazio na planilha**, **Sem o campo** (property set não anexado), **Não está no modelo** (chave da planilha sem objeto) e, se marcado, **Fora da planilha**.
  - Ações nas linhas selecionadas: **Aplicar a planilha** (numa operação só, com Ctrl+Z), **Ignorar** (fica salvo no padrão), **Voltar a conferir** e **Selecionar no modelo** (seleciona e aproxima a vista). Duplo clique também seleciona. **Relatório...** grava as linhas visíveis num .csv.
  - **Padrões salvos** em `%APPDATA%\MetroSP\Confrontar\Civil3D`: planilha, aba, linha dos títulos, colunas, campos, opções e as diferenças ignoradas. Dá para exportar e importar o .json para passar a outra pessoa.
  - Só entram objetos que vão para o IFC (novo `FiltroIfc`): espaço do modelo, layer ligado e descongelado, fora dos layers e blocos com `"Export": false` no mapeamento ativo, sem textos, cotas e hachuras.
  - Mesma janela e mesmas regras do Confrontar Planilha do Revit (código comum em `src/Confronto`).
- Falta testar no Civil 3D: abrir a janela num desenho com property sets (MB-20), escolher uma planilha, confrontar, aplicar alguns valores e desfazer com Ctrl+Z; conferir se "Selecionar no modelo" mostra a seleção com a janela aberta e se o zoom acerta; gravar Nome e Layer; salvar, reabrir e importar um padrão.

## 0.9.0

- **Excluir em Configurar IFC**: as tabelas Instalações, Dados do projeto, Classes IFC e Propriedades ganharam os botões **+** (incluir linha depois da selecionada) e **✕** (excluir as linhas selecionadas; com mais de uma, pede confirmação). A tecla Delete também exclui. Antes só Classes IFC tinha o ✕, e só para uma linha.
- **Desenhos sem alinhamento** (pavimentação, interferências): o Exportar IFC avisa "desenho sem alinhamento" e segue com projeto, terreno, instalações e os objetos do desenho, sem perguntar perfil.
- **Pavimentação no padrão MetroSP**: os sólidos de corredor por layer viram classes IFC 4.3:
  - `MT_PAVIMENTO*`, `BIM-PAV-CBUQ`, `BIM-PAV-BASE`, `BIM-PAV-SUBBASE` → `IfcCourse` (PAVEMENT);
  - `BIM-PAV-CONCRETO` → `IfcPavement` (RIGID);
  - `MT_GUIA`, `MT_SUPERGUIA`, `MT_SARJETA` → `IfcKerb`.
- **Todas as janelas redimensionáveis** (Sobre, Exportador, Perfis, Configurar IFC): as que abrem no tamanho do conteúdo ficam livres depois de abertas, com rolagem no corpo.
- **Erro "Value does not fall within the expected range" ao exportar**: um perfil ou alinhamento que o Civil 3D não consegue ler não para mais a exportação. O perfil com trecho sem cota pula essas estacas; o que não dá para ler vai só com a planta ou fica como a Autodesk exportar, com aviso na linha de comando.
- **Latitude e longitude do IfcSite**: "-23,63" (com vírgula) era gravado como -2363 graus e o modelo ia para o lugar errado no Iris. Agora o campo aceita vírgula ou ponto, e valor fora do intervalo fica de fora (vale o da Autodesk), com aviso.
- **Coordenadas no PTL do Metrô, sem EPSG** (mesmo critério do plugin do Revit), ligado por padrão em Configurar IFC > Projeto: o deslocamento que a Autodesk põe no IfcMapConversion volta para a colocação, e o IFC sai nas coordenadas do desenho, sem IfcMapConversion, IfcProjectedCRS e EPSG. Latitude e longitude do IfcSite calculadas pelo PTL (núcleo GeoDoc v6.5, o mesmo do Revit). Assim a via do Civil 3D e as estruturas do Revit ficam no mesmo lugar no Iris e no Navisworks. Conferido no IFC do MB-20 002-0: Via 01 começa em E 160.572,74 / N 241.944,14 (PTL), sem erros no validador do IfcOpenShell.
- **Alinhamento em coordenadas absolutas do PTL**: com o PTL ligado, o IfcAlignment (horizontal, vertical, eixo 3D e as estacas) passa a ter colocação na origem e pontos já nas coordenadas do PTL, como o MSP_IFCALINHAMENTO grava. O Iris lê o alinhamento sem aplicar a colocação; antes o eixo ficava perto da origem enquanto os sólidos iam para o PTL. Conferido no 002-0 e no 001-0: o eixo sai em E 160.572,74 / N 241.944,14 e as 582 estacas ficam no mesmo lugar de antes. Também sai o texto WKT do UTM que a Autodesk deixava solto (era o único erro do validador no 001-0).
- Erros gravados com o detalhe completo em `%APPDATA%\MetroSP\erros.log`.
- Falta testar no Civil 3D: em Configurar IFC, incluir e excluir instalações, dados do projeto, regras e propriedades, salvar e reabrir; repetir a exportação que deu o erro; abrir cada janela e esticar/encolher; exportar o MB-20.03.01.00-4F4 (pavimentação) e o 4D2 (interferências) e conferir no Iris as classes, as redes de tubulação e se os Property Sets (Código Metrô, material, situação) chegam ao IFC.

## 0.8.0

Checagem da IFC 4.3 Extension antes de exportar, botão Exportador, escolha de perfil numa janela só, pós-processamento ajustado ao IFC real da Extension 2026.1 (Via 02 - Lote 02 vazia é preenchida pelo plugin).

## Versões anteriores

| Versão | O que entrou |
|---|---|
| 0.7 | Alinhamento sempre no IFC, ribbon com 3 botões grandes, janela com abas, Nome obrigatório, Instalações |
| 0.6 | Identidade visual Ação BIM · MetroSP |
| 0.5 | Casamento tolerante do `IfcAlignment` |
| 0.4 | Motor da Autodesk + pós-processamento, pacote de configuração, janela Configurar IFC |
| 0.3 | Comando `MSP_IFCALINHAMENTO` (alinhamento + perfil em IFC 4.3 com eixo 3D) |
| 0.1–0.2 | Ribbon, login Autodesk (APS), configuração do Forma, suporte a 2025/26/27 |
