; Instalador único do MetroSP (Inno Setup 6): Revit 2025-2027 e Civil 3D 2025-2027 num .exe só.
; Instala por usuário, sem precisar de administrador:
;   Revit em %AppData%\Autodesk\Revit\Addins\<versão> (pastas 2025, 2026 e 2027 do pacote da release do MetroSP_Revit);
;   Civil 3D em %AppData%\Autodesk\ApplicationPlugins\MetroSP.bundle (bundle deste repositório, R25 e R26).
; Gerado pelo workflow "Instalador único" do MetroSP_Revit (o token do Actions daqui não enxerga a release privada do Revit), que passa:
;   /DRevitVersion=0.3.4 /DCivil3DVersion=0.12.0 /DRevitDist=<pasta com 2025, 2026, 2027> /DCivil3D=<MetroSP.bundle>
; Revit 2022 a 2024 continuam com o instalador separado da release do MetroSP_Revit.

#ifndef RevitVersion
  #define RevitVersion "0.0.0"
#endif
#ifndef Civil3DVersion
  #define Civil3DVersion "0.0.0"
#endif
#ifndef RevitDist
  #define RevitDist "..\dist\revit"
#endif
#ifndef Civil3D
  #define Civil3D "..\MetroSP.bundle"
#endif
#define Nome "MetroSP para Revit e Civil 3D"
#define BundleCivil "{userappdata}\Autodesk\ApplicationPlugins\MetroSP.bundle"

[Setup]
; Mesmo AppId do instalador do Revit: atualiza o MetroSP do Revit que já estiver instalado.
AppId={{8F3A2C61-4B7E-4D29-9E15-6A0C3B7D5E42}
AppName={#Nome}
AppVersion={#RevitVersion}
AppVerName={#Nome} (Revit {#RevitVersion} · Civil 3D {#Civil3DVersion})
AppPublisher=Ação BIM
AppComments=Ferramentas para Revit e Civil 3D no padrão BIM do Metrô de São Paulo
AppCopyright=Ação BIM
VersionInfoVersion={#RevitVersion}
VersionInfoDescription=Instalador do {#Nome}
DefaultDirName={userappdata}\Autodesk\Revit\Addins
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=lowest
OutputDir=..
OutputBaseFilename=MetroSP-Setup-Revit{#RevitVersion}-Civil3D{#Civil3DVersion}
Compression=lzma2
SolidCompression=yes
UninstallDisplayName={#Nome}
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
ptbr.WelcomeLabel1=MetroSP para Revit e Civil 3D
ptbr.WelcomeLabel2=Ferramentas da Ação BIM para modelar e entregar no padrão BIM do Metrô de São Paulo, para o Revit ({#RevitVersion}) e o Civil 3D ({#Civil3DVersion}).%n%nFeche o Revit e o Civil 3D antes de continuar. O MetroSP é instalado só para o seu usuário, sem precisar de administrador.%n%nRevit 2022 a 2024: use o instalador separado MetroSP-Revit2022-2024.
ptbr.SelectComponentsDesc=Onde instalar o MetroSP?
ptbr.SelectComponentsLabel2=Aparecem só os programas encontrados neste computador. Desmarque o que não quiser.
ptbr.FinishedHeadingLabel=MetroSP instalado
ptbr.FinishedLabel=Abra o Revit ou o Civil 3D e procure a aba MetroSP. Na primeira vez, o programa pergunta se confia no MetroSP: escolha "Sempre carregar".

; Um tipo só (personalizado): tudo o que foi encontrado vem marcado e a caixa de tipo de instalação não aparece.
[Types]
Name: "escolha"; Description: "Escolher"; Flags: iscustom

[Components]
Name: "r2025"; Description: "Revit 2025"; Types: escolha; Check: RevitInstalado('2025')
Name: "r2026"; Description: "Revit 2026"; Types: escolha; Check: RevitInstalado('2026')
Name: "r2027"; Description: "Revit 2027"; Types: escolha; Check: RevitInstalado('2027')
Name: "civil3d"; Description: "Civil 3D (2025, 2026 e 2027)"; Types: escolha; Check: AlgumCivil3D

[Files]
Source: "{#RevitDist}\2025\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2025"; Components: r2025; Flags: recursesubdirs ignoreversion skipifsourcedoesntexist
Source: "{#RevitDist}\2026\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2026"; Components: r2026; Flags: recursesubdirs ignoreversion skipifsourcedoesntexist
Source: "{#RevitDist}\2027\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2027"; Components: r2027; Flags: recursesubdirs ignoreversion skipifsourcedoesntexist
; Civil 3D: um bundle só; o Civil 3D 2025/2026 carrega Contents\R25 e o 2027 carrega Contents\R26 (PackageContents.xml).
Source: "{#Civil3D}\PackageContents.xml"; DestDir: "{#BundleCivil}"; Components: civil3d; Flags: ignoreversion
Source: "{#Civil3D}\Contents\R25\MetroSP.dll"; DestDir: "{#BundleCivil}\Contents\R25"; Components: civil3d; Flags: ignoreversion
Source: "{#Civil3D}\Contents\R26\MetroSP.dll"; DestDir: "{#BundleCivil}\Contents\R26"; Components: civil3d; Flags: ignoreversion
Source: "{#Civil3D}\Contents\Padrao\*"; DestDir: "{#BundleCivil}\Contents\Padrao"; Components: civil3d; Flags: recursesubdirs ignoreversion
; Configuração do usuário (Forma/APS): só na primeira instalação, para não apagar o que já foi preenchido.
Source: "{#Civil3D}\Contents\MetroSP.settings.json"; DestDir: "{#BundleCivil}\Contents"; Components: civil3d; Flags: onlyifdoesntexist uninsneveruninstall

[Code]
function RevitInstalado(Versao: String): Boolean;
begin
  Result := DirExists(ExpandConstant('{commonpf64}\Autodesk\Revit ') + Versao);
end;

function AlgumRevit: Boolean;
begin
  Result := RevitInstalado('2025') or RevitInstalado('2026') or RevitInstalado('2027');
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

// Sem nenhum programa compatível, avisa e não abre o assistente.
function InitializeSetup: Boolean;
begin
  Result := True;
  if AlgumRevit or AlgumCivil3D then Exit;
  MsgBox('Não encontramos neste computador o Revit nem o Civil 3D 2025, 2026 ou 2027.' + #13#10#13#10 +
    'Para o Revit 2022 a 2024, use o instalador MetroSP-Revit2022-2024.', mbInformation, MB_OK);
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

// Quem tinha o instalador só do Civil 3D: desinstala ele antes (sem perguntar), para não ficarem
// dois "MetroSP" em Aplicativos instalados. O MetroSP.settings.json do usuário fica (uninsneveruninstall).
const
  ChaveCivilAntigo = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{6E2B9C47-3F18-4A55-8D0B-71C4E2A9F3D8}_is1';

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Desinstalar: String;
  Codigo, Espera: Integer;
begin
  Result := '';
  if not WizardIsComponentSelected('civil3d') then Exit;
  if not RegQueryStringValue(HKCU, ChaveCivilAntigo, 'UninstallString', Desinstalar) then Exit;
  Exec(RemoveQuotes(Desinstalar), '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, Codigo);
  // O desinstalador do Inno continua numa cópia temporária: espera ele terminar (até 15 s).
  Espera := 0;
  while RegKeyExists(HKCU, ChaveCivilAntigo) and (Espera < 60) do
  begin
    Sleep(250);
    Espera := Espera + 1;
  end;
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
