# MetroSP para Civil 3D — exportador IFC 4.3 com alinhamento 3D

Aba **MetroSP** na ribbon do Civil 3D. O botão **Exportar IFC** usa o motor da Autodesk
(`IFCINFRAEXPORT`, do IFC 4.3 Extension), com a configuração IFC padronizada do MetroSP, e depois
pós-processa o arquivo gerado para colocar no mesmo IFC o que a Autodesk não grava:
o `IfcAlignment` com as cotas do perfil, o `IfcAlignmentVertical`, os dados de `IfcProject`/`IfcSite`
e os `IfcBuilding`. Assim o plugin recebe as atualizações do exportador da Autodesk e corrige só o que falta.

## Fluxo do Exportar IFC

1. Usuário clica **Exportar IFC** (`MSP_IFCEXPORT`) ou **Exportar seleção** (`MSP_IFCEXPORTSEL`).
2. Configuração ativa, conforme `FonteConfiguracao` em `MetroSP.settings.json`:
   - `Forma`: baixa do Forma a versão mais recente de cada JSON para o pacote ativo (login Autodesk na primeira vez);
   - `Local`: usa o pacote ativo como está (o padrão MetroSP ou o que foi importado/editado na janela);
   - `Automatico` (padrão): Forma se `ClientId`, `ProjectId` e `FolderUrn` estiverem preenchidos, senão Local.
3. Grava os JSON da Autodesk onde o IFC Extension procura. No modo `PorDesenho` fica
   `[nome do DWG]-IfcInfraConfiguration.json` / `-IfcInfraExportMapping.json` na pasta do desenho
   (com `.bak` se já existia um diferente) e `MetroSP-IFC-origem.txt` com a origem de cada arquivo.
4. Lê todos os alinhamentos do desenho com o perfil de projeto (ver *Alinhamento 3D*).
5. Dispara `IFCINFRAEXPORT` (ou `IFCINFRAEXPORTSELECTED`).
6. Quando o comando da Autodesk termina, roda `MSP_IFCPOSPROC` sobre o IFC recém-gerado:
   - guarda o original como `[arquivo].autodesk.ifc`;
   - troca o eixo de cada `IfcAlignment` (casado pelo nome) pela polilinha 3D e acrescenta o `IfcAlignmentVertical`
     se faltar, convertendo para o sistema local do alinhamento e compensando deslocamento de origem;
   - preenche `IfcProject` e `IfcSite` (nome, descrição, latitude/longitude, cota de referência, matrícula),
     cria os `IfcBuilding` configurados sob o `IfcSite`, cria `IfcMapConversion` se faltar e grava o Pset `MetroSP_Projeto`.
   `MSP_IFCPOSPROC` também pode ser rodado à mão, escolhendo um IFC já exportado.

## Aba MetroSP

| Painel | Botão | Comando |
|---|---|---|
| IFC 4.3 | Exportar IFC | `MSP_IFCEXPORT` |
| IFC 4.3 | Exportar seleção | `MSP_IFCEXPORTSEL` |
| IFC 4.3 | Só sincronizar | `MSP_IFCSYNC` |
| IFC 4.3 | Alinhamento 3D (IFC separado) | `MSP_IFCALINHAMENTO` |
| IFC 4.3 | Configurar IFC | `MSP_IFCCONFIG` |
| Forma | Entrar / Sair | `MSP_FORMALOGIN` / `MSP_FORMALOGOUT` |
| Forma | Pasta de config | `MSP_IFCPASTA` |
| Forma | Conta e Forma | `MSP_CONFIG` |
| Ajuda | Sobre | `MSP_SOBRE` |
| (linha de comando) | Pós-processar IFC | `MSP_IFCPOSPROC` |

## Pacote de configuração IFC

O pacote ativo fica em `%APPDATA%\MetroSP\Config` e vem pré-carregado com o padrão MetroSP
(`MetroSP.bundle/Contents/Padrao`). Arquivos:

| Arquivo | Dono | Conteúdo |
|---|---|---|
| `IfcInfraConfiguration.json` | Autodesk | opções gerais do IFC Extension |
| `IfcInfraExportMapping.json` | Autodesk | classe IFC por layer, bloco, baseline etc. (`MapCADLayerName`, `MapBlockName`...) |
| `IFCInfraExportPropertyMapping.json` / `.csv` | Autodesk | property sets e de onde vem cada propriedade |
| `MetroSP.ifc.json` | MetroSP | projeto, terreno, edificações, sistema de coordenadas, alinhamento e Pset do projeto |

Os três primeiros seguem o formato da Autodesk, então servem também fora do plugin.
`MetroSP.ifc.json` usa os nomes de entidades e atributos do IFC 4.3 da buildingSMART.

## Janela Configurar IFC (`MSP_IFCCONFIG`)

- **Pacote**: lista os arquivos ativos; importa JSON/CSV (o tipo é reconhecido pelo nome ou conteúdo);
  exporta o pacote para uma pasta; importa os modelos gerados por `IFCInfraSaveConfig`/`IFCInfraSaveMappingConfig`;
  baixa do Forma; restaura o padrão MetroSP.
- **Mapeamento de classes**: tabela por seção (layer, bloco, baseline...) com expressão, classe IFC
  (`IfcRail.RAIL`, `IfcTrackElement.SLEEPER`...) e exportar sim/não; a ordem importa (avaliado de baixo para cima).
- **Property sets**: tabela do CSV de mapeamento (Pset, propriedade, ativo, origem, grupo, nome no Civil) e editor dos modelos JSON.
- **Projeto, terreno e edificações**: `IfcProject`, `IfcSite`, lista de `IfcBuilding`, EPSG e propriedades do projeto
  (linha, contrato, OS, emitente...).
- **Alinhamento**: aplicar no IFC, passo dos pontos e perfis preferidos (expressões regulares).
- **Configuração Autodesk**: árvore editável do `IfcInfraConfiguration.json`.

Todo arquivo é validado antes de gravar e a versão anterior fica como `.bak`.

## Alinhamento 3D

O IFC do Civil 3D grava o alinhamento como curva plana na cota 0. O **Exportar IFC** corrige isso no
próprio IFC exportado. O botão **Alinhamento 3D** (`MSP_IFCALINHAMENTO`) gera só os alinhamentos num IFC 4.3
separado. Nos dois casos:

- **Geometria**: eixo 3D (`Axis` / `Curve3D`) com pontos a cada `Passo` metros (janela, aba Alinhamento) (padrão 1 m)
  e em todo início/fim de segmento, com X e Y do alinhamento e Z do perfil. Abre com as cotas certas
  em qualquer visualizador.
- **Semântica IFC 4.3**: `IfcAlignment` com `IfcAlignmentHorizontal` (retas, arcos e espirais, com raios
  e direções) e `IfcAlignmentVertical` (rampas, parábolas e curvas circulares, com rampas e raios),
  incluindo o segmento final de comprimento zero exigido no IFC4X3_ADD2.
- Propriedades `MetroSP_Alinhamento` (nome, perfil, estacas inicial e final, DWG de origem) e
  `IfcMapConversion` com o sistema configurado (padrão `EPSG:31983`, SIRGAS 2000 / UTM 23S).
- GUIDs estáveis: reexportar o mesmo desenho mantém os mesmos `GlobalId`, o que ajuda na federação e no
  controle de versões no Forma.

Perfil: se o alinhamento tiver mais de um perfil de projeto, vale o primeiro que casar com
`PerfisPreferidos`; se nenhum casar, o comando pergunta. No `MSP_IFCALINHAMENTO` escolha `Selecionar` ou `Todos`. O arquivo sugerido é `[desenho]-[alinhamento].ifc` na pasta do DWG.
Esse IFC pode ser federado com o exportado pelo `IFCINFRAEXPORT`.

Parábolas assimétricas viram duas parábolas que se encontram no PIV, como manda o IFC.
Espirais não clotoides são mapeadas para o tipo IFC equivalente (cúbica, Bloss, seno, cosseno, Helmert).

## Preparação (uma vez)

1. **App APS**: em https://aps.autodesk.com/myapps crie um app do tipo *Desktop, Mobile, Single-Page App*,
   com a API *Data Management*, e cadastre o Callback URL `http://localhost:8765/metrosp/callback`. Copie o Client ID.
2. **Liberar o app no hub**: um administrador da conta (Account Admin > Custom Integrations) adiciona o Client ID.
3. **Pasta no Forma**: crie uma pasta, por exemplo `Project Files/BIM/IFC_Config`, e suba os JSON.
   Para gerar os modelos de partida use no Civil 3D `IFCInfraSaveConfig` e `IFCInfraSaveMappingConfig`.
4. **MetroSP.settings.json** (em `MetroSP.bundle/Contents`): preencha `ClientId`, `ProjectId` (GUID do projeto, que aparece na URL)
   e `FolderUrn` (valor de `folderUrn=` na URL da pasta aberta no navegador).
   Cada usuário pode sobrescrever com uma cópia em `%APPDATA%\MetroSP\` (botão **Conta e Forma**).
   Sem Forma, nada disso é obrigatório: o plugin usa o pacote local.

## Versões suportadas

| Civil 3D | Série | .NET | DLL no bundle |
|---|---|---|---|
| 2025 | R25.0 | .NET 8 | `Contents/R25/MetroSP.dll` |
| 2026 | R25.1 | .NET 8 | `Contents/R25/MetroSP.dll` |
| 2027 | R26.0 | .NET 10 | `Contents/R26/MetroSP.dll` |

O 2027 não é binário-compatível com 2025/2026, por isso são duas DLLs. O `PackageContents.xml`
faz cada versão do Civil 3D carregar a DLL certa sozinha.

## Instalar

Compile (seção abaixo) para gerar as DLLs em `MetroSP.bundle\Contents\R25` e `R26`,
rode `instalar.bat` e reinicie o Civil 3D. As DLLs não ficam versionadas no Git.
O bundle vai para `%APPDATA%\Autodesk\ApplicationPlugins` e serve para as três versões ao mesmo tempo.
Para instalar para todos os usuários da máquina, copie `MetroSP.bundle` para `C:\ProgramData\Autodesk\ApplicationPlugins`.

## Compilar (só se mudar o código)

Requisitos: .NET 8 SDK e .NET 10 SDK (ou Visual Studio 2026). Não precisa ter o Civil 3D instalado:
as referências vêm do pacote NuGet oficial `AutoCAD.NET` (25.0.1 para 2025/2026, 26.0.0 para 2027).

```bat
dotnet build src\MetroSP.csproj -c Release
```

O build gera as duas DLLs e copia cada uma para `MetroSP.bundle\Contents\R25` e `R26`.

## Limitações conhecidas

- O nome do arquivo IFC ainda é escolhido no diálogo do próprio `IFCINFRAEXPORT`; o pós-processamento acha o IFC
  mais novo na pasta do desenho ou em Documentos e, se não achar, pede o arquivo.
- O pós-processamento foi testado com um IFC sintético no formato esperado da Autodesk (validado com IfcOpenShell:
  geometria regenerada a partir da semântica bate com o eixo 3D em 0,0 mm). Falta conferir com um IFC real do Civil 3D.
- A janela de configurações compila, mas ainda não foi aberta num Windows.
- Os nomes dos comandos do IFC Extension podem mudar por versão; ficam configuráveis em `ExportCommand` e `ExportSelectedCommand`.
- Os ícones são gerados em código (quadrados com sigla). Troque por PNG 16/32 px com a identidade visual.
- Compilado sem erros nem avisos para .NET 8 e .NET 10, mas ainda não testado dentro do Civil 3D.
