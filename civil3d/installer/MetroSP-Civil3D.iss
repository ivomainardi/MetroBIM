; Instalador do MetroSP para Civil 3D (Inno Setup 6).
; Instala o bundle por usuário, sem administrador, em %AppData%\Autodesk\ApplicationPlugins\MetroSP.bundle.
; O Civil 3D 2025/2026 carrega Contents\R25 e o 2027 carrega Contents\R26 (PackageContents.xml).
; Gerado por build.ps1 depois de compilar (as DLLs não vão para o Git).

; A versão vem do build.ps1 (/DAppVersion=...), lida do src\MetroSP.csproj.
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{6E2B9C47-3F18-4A55-8D0B-71C4E2A9F3D8}
AppName=MetroSP para Civil 3D
AppVersion={#AppVersion}
AppPublisher=Ação BIM
AppComments=Ferramentas para Civil 3D no padrão BIM do Metrô de São Paulo
AppCopyright=Ação BIM
VersionInfoVersion={#AppVersion}
VersionInfoDescription=Instalador do MetroSP para Civil 3D
DefaultDirName={userappdata}\Autodesk\ApplicationPlugins\MetroSP.bundle
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..
OutputBaseFilename=MetroSP-Civil3D-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
UninstallDisplayName=MetroSP para Civil 3D {#AppVersion}
UninstallDisplayIcon={uninstallexe}
CloseApplications=no
; Identidade visual: branco, laranja Ação BIM (#FF9900) e preto, com as logos da Ação BIM e do Metrô (igual ao Revit).
WizardStyle=modern
WizardImageFile=marca\lateral100.bmp,marca\lateral200.bmp
WizardSmallImageFile=marca\pequena100.bmp,marca\pequena200.bmp
SetupIconFile=marca\metrosp.ico
WizardSizePercent=110

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Messages]
ptbr.WelcomeLabel1=MetroSP para Civil 3D {#AppVersion}
ptbr.WelcomeLabel2=Ferramentas da Ação BIM para modelar e entregar no padrão BIM do Metrô de São Paulo: exportação IFC 4.3, property sets, dados e classificação.%n%nServe para o Civil 3D 2025, 2026 e 2027. Feche o Civil 3D antes de continuar. O MetroSP é instalado só para o seu usuário, sem precisar de administrador.
ptbr.FinishedHeadingLabel=MetroSP instalado
ptbr.FinishedLabel=Abra o Civil 3D e procure a aba MetroSP. Se o Civil 3D perguntar se pode carregar o MetroSP, escolha "Sempre carregar".

[Files]
Source: "..\MetroSP.bundle\PackageContents.xml"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\MetroSP.bundle\Contents\R25\MetroSP.dll"; DestDir: "{app}\Contents\R25"; Flags: ignoreversion
Source: "..\MetroSP.bundle\Contents\R26\MetroSP.dll"; DestDir: "{app}\Contents\R26"; Flags: ignoreversion
Source: "..\MetroSP.bundle\Contents\Padrao\*"; DestDir: "{app}\Contents\Padrao"; Flags: recursesubdirs ignoreversion
; Configuração do usuário (Forma/APS): só na primeira instalação, para não apagar o que já foi preenchido.
Source: "..\MetroSP.bundle\Contents\MetroSP.settings.json"; DestDir: "{app}\Contents"; Flags: onlyifdoesntexist uninsneveruninstall

[Code]
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
