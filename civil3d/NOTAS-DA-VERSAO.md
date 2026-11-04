# Notas da versão: MetroSP para Civil 3D

## 0.9.0 (em teste)

- **Desenhos sem alinhamento** (pavimentação, interferências): o Exportar IFC avisa "desenho sem alinhamento" e segue com projeto, terreno, instalações e os objetos do desenho, sem perguntar perfil.
- **Pavimentação no padrão MetroSP**: os sólidos de corredor por layer viram classes IFC 4.3:
  - `MT_PAVIMENTO*`, `BIM-PAV-CBUQ`, `BIM-PAV-BASE`, `BIM-PAV-SUBBASE` → `IfcCourse` (PAVEMENT);
  - `BIM-PAV-CONCRETO` → `IfcPavement` (RIGID);
  - `MT_GUIA`, `MT_SUPERGUIA`, `MT_SARJETA` → `IfcKerb`.
- **Todas as janelas redimensionáveis** (Sobre, Exportador, Perfis, Configurar IFC): as que abrem no tamanho do conteúdo ficam livres depois de abertas, com rolagem no corpo.
- **Erro "Value does not fall within the expected range" ao exportar**: um perfil ou alinhamento que o Civil 3D não consegue ler não para mais a exportação. O perfil com trecho sem cota pula essas estacas; o que não dá para ler vai só com a planta ou fica como a Autodesk exportar, com aviso na linha de comando.
- Erros gravados com o detalhe completo em `%APPDATA%\MetroSP\erros.log`.
- Falta testar no Civil 3D: repetir a exportação que deu o erro; abrir cada janela e esticar/encolher; exportar o MB-20.03.01.00-4F4 (pavimentação) e o 4D2 (interferências) e conferir no Iris as classes, as redes de tubulação e se os Property Sets (Código Metrô, material, situação) chegam ao IFC.

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
