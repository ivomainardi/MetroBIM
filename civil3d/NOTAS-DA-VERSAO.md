# Notas da versão: MetroSP para Civil 3D

## Próxima versão (em preparo)

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
