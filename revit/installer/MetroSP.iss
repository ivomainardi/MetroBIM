; Instalador do add-in MetroSP para Revit (Inno Setup 6).
; Instala por usuário, sem precisar de administrador, em %AppData%\Autodesk\Revit\Addins\<versão>.
; Gerado por build.ps1 a partir da pasta dist\.

; A versão vem do build.ps1 (/DAppVersion=...), lida do MetroSP.csproj.
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

; Revit 2022 a 2024 (.NET Framework 4.8) têm instalador próprio (build.ps1 passa /DRevit2022a2024), separado do de 2025-2027.
#ifdef Revit2022a2024
  #define Nome "MetroSP para Revit 2022 a 2024"
  #define Saida "MetroSP-Revit2022-2024-Setup-"
  #define Dist "..\dist\revit2022-2024"
#else
  #define Nome "MetroSP para Revit"
  #define Saida "MetroSP-Setup-"
  #define Dist "..\dist\revit"
#endif

[Setup]
; Mesmo AppId do antigo instalador do Revit 2022: a nova versão atualiza a que já estiver instalada.
#ifdef Revit2022a2024
AppId={{3C7E9B14-62A8-4F0D-B5C3-9D1E8A4F2B67}
#else
AppId={{8F3A2C61-4B7E-4D29-9E15-6A0C3B7D5E42}
#endif
AppName={#Nome}
AppVersion={#AppVersion}
AppPublisher=Ação BIM
AppComments=Ferramentas para Revit no padrão BIM do Metrô de São Paulo
AppCopyright=Ação BIM
VersionInfoVersion={#AppVersion}
VersionInfoDescription=Instalador do {#Nome}
DefaultDirName={userappdata}\Autodesk\Revit\Addins
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..
OutputBaseFilename={#Saida}{#AppVersion}
Compression=lzma2
SolidCompression=yes
UninstallDisplayName={#Nome} {#AppVersion}
UninstallDisplayIcon={uninstallexe}
; Identidade visual: branco, laranja Ação BIM (#FF9900) e preto, com as logos da Ação BIM e do Metrô.
WizardStyle=modern
WizardImageFile=marca\lateral100.bmp,marca\lateral200.bmp
WizardSmallImageFile=marca\pequena100.bmp,marca\pequena200.bmp
SetupIconFile=marca\metrosp.ico
WizardSizePercent=110

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Messages]
ptbr.WelcomeLabel1={#Nome} {#AppVersion}
ptbr.WelcomeLabel2=Ferramentas da Ação BIM para modelar e entregar no padrão BIM do Metrô de São Paulo: parâmetros, dados, classificação e exportação IFC.%n%nFeche o Revit antes de continuar. O MetroSP é instalado só para o seu usuário, sem precisar de administrador.
ptbr.SelectComponentsDesc=Em quais versões do Revit instalar?
ptbr.SelectComponentsLabel2=Aparecem só as versões do Revit encontradas neste computador.
ptbr.FinishedHeadingLabel=MetroSP instalado
ptbr.FinishedLabel=Abra o Revit e procure a aba MetroSP. Na primeira vez, o Revit pergunta se confia no add-in: escolha "Sempre carregar".

[Components]
#ifdef Revit2022a2024
Name: "r2022"; Description: "Revit 2022"; Check: RevitInstalado('2022')
Name: "r2023"; Description: "Revit 2023"; Check: RevitInstalado('2023')
Name: "r2024"; Description: "Revit 2024"; Check: RevitInstalado('2024')
#else
Name: "r2025"; Description: "Revit 2025"; Check: RevitInstalado('2025')
Name: "r2026"; Description: "Revit 2026"; Check: RevitInstalado('2026')
Name: "r2027"; Description: "Revit 2027"; Check: RevitInstalado('2027')
#endif

[Files]
#ifdef Revit2022a2024
Source: "{#Dist}\2022\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2022"; Components: r2022; Flags: recursesubdirs ignoreversion skipifsourcedoesntexist
Source: "{#Dist}\2023\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2023"; Components: r2023; Flags: recursesubdirs ignoreversion skipifsourcedoesntexist
Source: "{#Dist}\2024\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2024"; Components: r2024; Flags: recursesubdirs ignoreversion skipifsourcedoesntexist
#else
Source: "{#Dist}\2025\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2025"; Components: r2025; Flags: recursesubdirs ignoreversion skipifsourcedoesntexist
Source: "{#Dist}\2026\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2026"; Components: r2026; Flags: recursesubdirs ignoreversion skipifsourcedoesntexist
Source: "{#Dist}\2027\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2027"; Components: r2027; Flags: recursesubdirs ignoreversion skipifsourcedoesntexist
#endif

[Code]
function RevitInstalado(Versao: String): Boolean;
begin
  Result := DirExists(ExpandConstant('{commonpf64}\Autodesk\Revit ') + Versao);
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
