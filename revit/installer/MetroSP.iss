; Instalador do add-in MetroSP para Revit (Inno Setup 6).
; Instala por usuário, sem precisar de administrador, em %AppData%\Autodesk\Revit\Addins\<versão>.
; Gerado por build.ps1 a partir da pasta dist\.

#define AppVersion "0.2.2"

[Setup]
AppId={{8F3A2C61-4B7E-4D29-9E15-6A0C3B7D5E42}
AppName=MetroSP para Revit
AppVersion={#AppVersion}
AppPublisher=MetroSP
DefaultDirName={userappdata}\Autodesk\Revit\Addins
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..
OutputBaseFilename=MetroSP-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
UninstallDisplayName=MetroSP para Revit

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

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
