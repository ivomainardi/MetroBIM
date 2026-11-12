; Instalador único do MetroSP (Inno Setup 6): todos os plugins num .exe só.
;   Revit 2022 a 2027   -> %AppData%\Autodesk\Revit\Addins\<versão>
;   Civil 3D 2025-2027  -> %AppData%\Autodesk\ApplicationPlugins\MetroSP.bundle (R25 e R26)
;   Navisworks 2025-2027 (Manage e Simulate) -> %AppData%\Autodesk\ApplicationPlugins\MetroSP-Navisworks.bundle
; Instala por usuário, sem precisar de administrador. Aparecem só os programas encontrados no computador.
; Gerado pelo build.ps1 da raiz, que passa:
;   /DVersao=1.0.0 /DRevitVersion=0.3.4 /DCivil3DVersion=0.12.0 /DNavisworksVersion=1.0.0 /DDist=<pasta dist>
; Pastas esperadas em Dist: revit\<2022..2027>, civil3d\MetroSP.bundle, navisworks\<2025..2027> (opcional).

#ifndef Versao
  #define Versao "0.0.0"
#endif
#ifndef RevitVersion
  #define RevitVersion "?"
#endif
#ifndef Civil3DVersion
  #define Civil3DVersion "?"
#endif
#ifndef NavisworksVersion
  #define NavisworksVersion "?"
#endif
#ifndef Dist
  #define Dist "..\dist"
#endif
#define Nome "MetroSP"
#define Revit Dist + "\revit"
#define Civil3D Dist + "\civil3d\MetroSP.bundle"
#define Navis Dist + "\navisworks"
#define BundleCivil "{userappdata}\Autodesk\ApplicationPlugins\MetroSP.bundle"
#define BundleNavis "{userappdata}\Autodesk\ApplicationPlugins\MetroSP-Navisworks.bundle"
#if DirExists(Navis)
  #define ComNavisworks
#endif

[Setup]
; Mesmo AppId do instalador do Revit 2025-2027 e do antigo instalador Revit + Civil 3D: atualiza o que já estiver instalado.
; Os instaladores antigos só do Civil 3D e só do Revit 2022-2024 são removidos em PrepareToInstall.
AppId={{8F3A2C61-4B7E-4D29-9E15-6A0C3B7D5E42}
AppName={#Nome}
AppVersion={#Versao}
AppVerName={#Nome} {#Versao}
AppPublisher=Ação BIM
AppComments=Plugins do padrão BIM do Metrô de São Paulo: Revit {#RevitVersion}, Civil 3D {#Civil3DVersion} e Navisworks {#NavisworksVersion}
AppCopyright=Ação BIM
VersionInfoVersion={#Versao}
VersionInfoDescription=Instalador do {#Nome}
DefaultDirName={userappdata}\Autodesk\Revit\Addins
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=lowest
OutputDir=..
OutputBaseFilename=MetroSP-Setup-{#Versao}
Compression=lzma2
SolidCompression=yes
UninstallDisplayName={#Nome} {#Versao}
UninstallDisplayIcon={uninstallexe}
CloseApplications=no
; Identidade visual: branco, laranja Ação BIM (#FF9900) e preto, com as logos da Ação BIM e do Metrô.
WizardStyle=modern
WizardImageFile=marca\lateral100.bmp,marca\lateral200.bmp
WizardSmallImageFile=marca\pequena100.bmp,marca\pequena200.bmp
SetupIconFile=marca\metrosp.ico
WizardSizePercent=110

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Messages]
ptbr.WelcomeLabel1=MetroSP {#Versao}
ptbr.WelcomeLabel2=Ferramentas da Ação BIM para modelar e entregar no padrão BIM do Metrô de São Paulo.%n%nFeche o Revit, o Civil 3D e o Navisworks antes de continuar. O MetroSP é instalado só para o seu usuário, sem precisar de administrador.
ptbr.SelectComponentsDesc=Onde instalar o MetroSP?
ptbr.SelectComponentsLabel2=Aparecem só os programas encontrados neste computador. Desmarque o que não quiser.
ptbr.FinishedHeadingLabel=MetroSP instalado
ptbr.FinishedLabel=Abra o programa e procure a aba MetroSP (no Navisworks, Tool Add-ins 1 › Exportar para o Iris). Na primeira vez, o programa pode perguntar se confia no MetroSP: escolha "Sempre carregar".

; Um tipo só (personalizado): tudo o que foi encontrado vem marcado e a caixa de tipo de instalação não aparece.
[Types]
Name: "escolha"; Description: "Escolher"; Flags: iscustom

[Components]
Name: "revit"; Description: "Revit (MetroSP {#RevitVersion})"; Types: escolha; Check: AlgumRevit
Name: "revit\r2022"; Description: "Revit 2022"; Types: escolha; Check: RevitInstalado('2022')
Name: "revit\r2023"; Description: "Revit 2023"; Types: escolha; Check: RevitInstalado('2023')
Name: "revit\r2024"; Description: "Revit 2024"; Types: escolha; Check: RevitInstalado('2024')
Name: "revit\r2025"; Description: "Revit 2025"; Types: escolha; Check: RevitInstalado('2025')
Name: "revit\r2026"; Description: "Revit 2026"; Types: escolha; Check: RevitInstalado('2026')
Name: "revit\r2027"; Description: "Revit 2027"; Types: escolha; Check: RevitInstalado('2027')
Name: "civil3d"; Description: "Civil 3D 2025, 2026 e 2027 (MetroSP {#Civil3DVersion})"; Types: escolha; Check: AlgumCivil3D
#ifdef ComNavisworks
Name: "navisworks"; Description: "Navisworks 2025, 2026 e 2027: Exportar para o Iris ({#NavisworksVersion})"; Types: escolha; Check: AlgumNavisworks
#endif

[Files]
Source: "{#Revit}\2022\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2022"; Components: revit\r2022; Flags: recursesubdirs ignoreversion
Source: "{#Revit}\2023\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2023"; Components: revit\r2023; Flags: recursesubdirs ignoreversion
Source: "{#Revit}\2024\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2024"; Components: revit\r2024; Flags: recursesubdirs ignoreversion
Source: "{#Revit}\2025\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2025"; Components: revit\r2025; Flags: recursesubdirs ignoreversion
Source: "{#Revit}\2026\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2026"; Components: revit\r2026; Flags: recursesubdirs ignoreversion
Source: "{#Revit}\2027\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2027"; Components: revit\r2027; Flags: recursesubdirs ignoreversion
; Civil 3D: um bundle só; o Civil 3D 2025/2026 carrega Contents\R25 e o 2027 carrega Contents\R26 (PackageContents.xml).
Source: "{#Civil3D}\PackageContents.xml"; DestDir: "{#BundleCivil}"; Components: civil3d; Flags: ignoreversion
Source: "{#Civil3D}\Contents\R25\MetroSP.dll"; DestDir: "{#BundleCivil}\Contents\R25"; Components: civil3d; Flags: ignoreversion
Source: "{#Civil3D}\Contents\R26\MetroSP.dll"; DestDir: "{#BundleCivil}\Contents\R26"; Components: civil3d; Flags: ignoreversion
Source: "{#Civil3D}\Contents\Padrao\*"; DestDir: "{#BundleCivil}\Contents\Padrao"; Components: civil3d; Flags: recursesubdirs ignoreversion
; Configuração do usuário (Forma/APS): só na primeira instalação, para não apagar o que já foi preenchido.
Source: "{#Civil3D}\Contents\MetroSP.settings.json"; DestDir: "{#BundleCivil}\Contents"; Components: civil3d; Flags: onlyifdoesntexist uninsneveruninstall
#ifdef ComNavisworks
; Navisworks: bundle com uma DLL por versão (cada uma compilada contra a API daquela versão).
Source: "navisworks\PackageContents.xml"; DestDir: "{#BundleNavis}"; Components: navisworks; Flags: ignoreversion
Source: "{#Navis}\*"; DestDir: "{#BundleNavis}\Contents"; Components: navisworks; Flags: recursesubdirs ignoreversion
#endif

[Code]
function RevitInstalado(Versao: String): Boolean;
begin
  Result := DirExists(ExpandConstant('{commonpf64}\Autodesk\Revit ') + Versao);
end;

function AlgumRevit: Boolean;
begin
  Result := RevitInstalado('2022') or RevitInstalado('2023') or RevitInstalado('2024') or
    RevitInstalado('2025') or RevitInstalado('2026') or RevitInstalado('2027');
end;

// Civil 3D: pasta C3D dentro da pasta do AutoCAD do ano ou, se não achar, o registro da série
// (2025 = R25.0, 2026 = R25.1, 2027 = R26.0) com um produto "Civil 3D".
function Civil3DNoRegistro(Serie: String): Boolean;
var
  Base, Produto: String;
  Chaves: TArrayOfString;
  I: Integer;
begin
  Result := False;
  Base := 'SOFTWARE\Autodesk\AutoCAD\' + Serie;
  if not RegGetSubkeyNames(HKLM64, Base, Chaves) then Exit;
  for I := 0 to GetArrayLength(Chaves) - 1 do
    if RegQueryStringValue(HKLM64, Base + '\' + Chaves[I], 'ProductName', Produto) and (Pos('Civil 3D', Produto) > 0) then
    begin
      Result := True;
      Exit;
    end;
end;

function Civil3DInstalado(Ano, Serie: String): Boolean;
begin
  Result := DirExists(ExpandConstant('{commonpf64}\Autodesk\AutoCAD ') + Ano + '\C3D') or Civil3DNoRegistro(Serie);
end;

function AlgumCivil3D: Boolean;
begin
  Result := Civil3DInstalado('2025', 'R25.0') or Civil3DInstalado('2026', 'R25.1') or Civil3DInstalado('2027', 'R26.0');
end;

function NavisworksInstalado(Ano: String): Boolean;
begin
  Result := DirExists(ExpandConstant('{commonpf64}\Autodesk\Navisworks Manage ') + Ano) or
    DirExists(ExpandConstant('{commonpf64}\Autodesk\Navisworks Simulate ') + Ano);
end;

function AlgumNavisworks: Boolean;
begin
#ifdef ComNavisworks
  Result := NavisworksInstalado('2025') or NavisworksInstalado('2026') or NavisworksInstalado('2027');
#else
  Result := False;
#endif
end;

// Sem nenhum programa compatível, avisa e não abre o assistente.
function InitializeSetup: Boolean;
begin
  Result := True;
  if AlgumRevit or AlgumCivil3D or AlgumNavisworks then Exit;
  MsgBox('Não encontramos neste computador o Revit (2022 a 2027), o Civil 3D (2025 a 2027) nem o Navisworks (2025 a 2027).',
    mbInformation, MB_OK);
  Result := False;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpSelectComponents) and (WizardSelectedComponents(False) = '') then
  begin
    MsgBox('Marque pelo menos um programa para instalar o MetroSP.', mbInformation, MB_OK);
    Result := False;
  end;
end;

// Instaladores antigos (só Civil 3D e só Revit 2022-2024): desinstala antes, sem perguntar, para não ficarem
// dois "MetroSP" em Aplicativos instalados. O MetroSP.settings.json do usuário fica (uninsneveruninstall).
procedure DesinstalarAntigo(Chave: String);
var
  Desinstalar: String;
  Codigo, Espera: Integer;
begin
  if not RegQueryStringValue(HKCU, Chave, 'UninstallString', Desinstalar) then Exit;
  Exec(RemoveQuotes(Desinstalar), '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, Codigo);
  // O desinstalador do Inno continua numa cópia temporária: espera ele terminar (até 15 s).
  Espera := 0;
  while RegKeyExists(HKCU, Chave) and (Espera < 60) do
  begin
    Sleep(250);
    Espera := Espera + 1;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  DesinstalarAntigo('Software\Microsoft\Windows\CurrentVersion\Uninstall\{6E2B9C47-3F18-4A55-8D0B-71C4E2A9F3D8}_is1');
  DesinstalarAntigo('Software\Microsoft\Windows\CurrentVersion\Uninstall\{3C7E9B14-62A8-4F0D-B5C3-9D1E8A4F2B67}_is1');
end;

// Linha laranja embaixo do cabeçalho das páginas, como nas janelas do MetroSP.
procedure InitializeWizard;
var
  Linha: TBitmapImage;
begin
  Linha := TBitmapImage.Create(WizardForm);
  Linha.Parent := WizardForm.MainPanel;
  Linha.Align := alBottom;
  Linha.Height := ScaleY(3);
  Linha.BackColor := $0099FF;
  WizardForm.Bevel1.Visible := False;
end;
