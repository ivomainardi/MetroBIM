# MetroSP para Civil 3D — exportador IFC com configuração no Forma

Aba **MetroSP** na ribbon do Civil 3D. O botão **Exportar IFC** baixa do Autodesk Forma
(Autodesk Docs) os JSON de configuração do IFC 4.3 Extension, grava na pasta do desenho e
chama `IFCINFRAEXPORT`. Assim todo mundo exporta com a mesma configuração, mantida num lugar só.

## Fluxo

1. Usuário clica **Exportar IFC** (comando `MSP_IFCEXPORT`).
2. Login Autodesk pelo navegador na primeira vez (OAuth PKCE; a sessão fica salva e criptografada em `%APPDATA%\MetroSP\token.bin`).
3. O plugin lista a pasta do Forma, pega a versão mais recente de cada arquivo e valida o JSON.
4. Grava os arquivos na pasta do DWG. No modo `PorDesenho` fica `[nome do DWG]-IfcInfraConfiguration.json`, que é o nível mais específico que o IFC Extension lê antes do arquivo IFC.
5. Se já existia um arquivo diferente, guarda uma cópia `.bak`.
6. Escreve `MetroSP-IFC-origem.txt` com versão, data e autor de cada JSON (rastreabilidade de qual configuração gerou o IFC).
7. Dispara `IFCINFRAEXPORT` (ou `IFCINFRAEXPORTSELECTED` no botão **Exportar seleção**).

## Aba MetroSP

| Painel | Botão | Comando |
|---|---|---|
| IFC 4.3 | Exportar IFC | `MSP_IFCEXPORT` |
| IFC 4.3 | Exportar seleção | `MSP_IFCEXPORTSEL` |
| IFC 4.3 | Só sincronizar | `MSP_IFCSYNC` |
| Forma | Entrar / Sair | `MSP_FORMALOGIN` / `MSP_FORMALOGOUT` |
| Forma | Pasta de config | `MSP_IFCPASTA` |
| Forma | Configurações | `MSP_CONFIG` |
| Ajuda | Sobre | `MSP_SOBRE` |

## Preparação (uma vez)

1. **App APS**: em https://aps.autodesk.com/myapps crie um app do tipo *Desktop, Mobile, Single-Page App*,
   com a API *Data Management*, e cadastre o Callback URL `http://localhost:8765/metrosp/callback`. Copie o Client ID.
2. **Liberar o app no hub**: um administrador da conta (Account Admin > Custom Integrations) adiciona o Client ID.
3. **Pasta no Forma**: crie uma pasta, por exemplo `Project Files/BIM/IFC_Config`, e suba os JSON.
   Para gerar os modelos de partida use no Civil 3D `IFCInfraSaveConfig` e `IFCInfraSaveMappingConfig`.
4. **MetroSP.settings.json** (em `MetroSP.bundle/Contents`): preencha `ClientId`, `ProjectId` (GUID do projeto, que aparece na URL)
   e `FolderUrn` (valor de `folderUrn=` na URL da pasta aberta no navegador).
   Cada usuário pode sobrescrever com uma cópia em `%APPDATA%\MetroSP\` (botão **Configurações**).

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

- O nome do arquivo IFC ainda é escolhido no diálogo do próprio `IFCINFRAEXPORT`.
- Os nomes dos comandos do IFC Extension podem mudar por versão; ficam configuráveis em `ExportCommand` e `ExportSelectedCommand`.
- Os ícones são gerados em código (quadrados com sigla). Troque por PNG 16/32 px com a identidade visual.
- Compilado sem erros nem avisos para .NET 8 e .NET 10, mas ainda não testado dentro do Civil 3D.
