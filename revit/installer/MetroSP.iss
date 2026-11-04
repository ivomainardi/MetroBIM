; Instalador do add-in MetroSP para Revit (Inno Setup 6).
; Instala por usuário, sem precisar de administrador, em %AppData%\Autodesk\Revit\Addins\<versão>.
; Gerado por build.ps1 a partir da pasta dist\.

; A versão vem do build.ps1 (/DAppVersion=...), lida do MetroSP.csproj.
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{8F3A2C61-4B7E-4D29-9E15-6A0C3B7D5E42}
AppName=MetroSP para Revit
AppVersion={#AppVersion}
AppPublisher=Ação BIM
AppComments=Ferramentas para Revit no padrão BIM do Metrô de São Paulo
AppCopyright=Ação BIM
VersionInfoVersion={#AppVersion}
VersionInfoDescription=Instalador do MetroSP para Revit
DefaultDirName={userappdata}\Autodesk\Revit\Addins
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..
OutputBaseFilename=MetroSP-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
UninstallDisplayName=MetroSP para Revit {#AppVersion}
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
ptbr.WelcomeLabel1=MetroSP para Revit {#AppVersion}
ptbr.WelcomeLabel2=Ferramentas da Ação BIM para modelar e entregar no padrão BIM do Metrô de São Paulo: parâmetros, dados, classificação e exportação IFC.%n%nFeche o Revit antes de continuar. O MetroSP é instalado só para o seu usuário, sem precisar de administrador.
ptbr.SelectComponentsDesc=Em quais versões do Revit instalar?
ptbr.SelectComponentsLabel2=Aparecem só as versões do Revit encontradas neste computador.
ptbr.FinishedHeadingLabel=MetroSP instalado
ptbr.FinishedLabel=Abra o Revit e procure a aba MetroSP. Na primeira vez, o Revit pergunta se confia no add-in: escolha "Sempre carregar".

[Components]
Name: "r2025"; Description: "Revit 2025"; Check: RevitInstalado('2025')
Name: "r2026"; Description: "Revit 2026"; Check: RevitInstalado('2026')
Name: "r2027"; Description: "Revit 2027"; Check: RevitInstalado('2027')

[Files]
Source: "..\dist\2025\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2025"; Components: r2025; Flags: recursesubdirs ignoreversion skipifsourcedoesntexist
Source: "..\dist\2026\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2026"; Components: r2026; Flags: recursesubdirs ignoreversion skipifsourcedoesntexist

Source: "..\dist\2027\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2027"; Components: r2027; Flags: recursesubdirs ignoreversion skipifsourcedoesntexist

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
