# Notas da versão: MetroSP para Civil 3D

## 0.9.0 (em teste)

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
