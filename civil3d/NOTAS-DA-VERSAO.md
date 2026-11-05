# Notas da versão: MetroSP para Civil 3D

## 0.10.0 (em teste)

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
  property set `Classification` chega ao IFC exportado. Hoje ele vai como property set; transformar em
  `IfcClassificationReference` no pós-processamento fica para um próximo passo.

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
- **Configuração inicial da equipe no padrão do plugin** (pasta Padrao): IfcInfraConfiguration.json, IFCInfraExportPropertyMapping.json e .csv como vieram da equipe; o IfcInfraExportMapping.json juntou o da equipe com as regras do plugin, com correções: vírgulas sobrando e "MapShapeCode" repetido (o arquivo não abria como JSON), "fcTrackElement" → IfcTrackElement.VEHICLESTOP, "IfcSlabTypeEnum.BASESLAB" → IfcSlab.BASESLAB, espaço antes de IfcSlab.TRACKSLAB, U-3D-OCUPACAO_VP com "--------" → IfcBuiltElement (o que já saía; IfcBuiltElement não tem tipo no IFC 4.3, então ".USERDEFINED" também saiu da regra ^G-3D-), U-3D-TRILHO_AMV antes de U-3D-TRILHO. Todas as classes conferidas no esquema IFC4X3_ADD2.
- **Classe IFC escolhida numa lista** (Configurar IFC > Classes IFC): a lista traz todas as classes e Classe.TIPO do esquema escolhido na configuração da Autodesk (IFC 4.3 ou IFC4), geradas do esquema oficial. Digitar só filtra (ex.: TRACKSLAB); não dá mais para escrever uma classe solta. Aplicar vale para todas as regras selecionadas. Regra com classe fora do esquema aparece em vermelho e o Salvar avisa quais são antes de gravar.
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
