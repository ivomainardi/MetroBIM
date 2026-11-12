# Compila e instala o plugin "Exportar para o Iris" em cada Navisworks (Manage ou Simulate) 2025, 2026 e 2027 instalado.
# Uso (PowerShell, nesta pasta):  .\instalar.ps1            ou só algumas versões:  .\instalar.ps1 -Versoes 2026
# Precisa do .NET SDK 8 ou mais novo (winget install Microsoft.DotNet.SDK.8). Feche o Navisworks antes.
# Instala em <Navisworks>\Plugins\IrisExport\ (pede administrador).
param([string[]]$Versoes = @('2025', '2026', '2027'))
$ErrorActionPreference = 'Stop'
# o Navisworks lê os plugins da pasta Plugins da instalação (em Arquivos de Programas): precisa de administrador
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) {
  Write-Host "Pedindo permissão de administrador para instalar na pasta do Navisworks..."
  Start-Process powershell -Verb RunAs -ArgumentList "-NoExit -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Versoes $($Versoes -join ',')"
  exit
}
$proj = Join-Path $PSScriptRoot 'IrisExport\IrisExport.csproj'
$feitas = 0; $achadas = 0
foreach ($v in $Versoes) {
  foreach ($p in 'Manage', 'Simulate') {
    $dir = Join-Path $env:ProgramW6432 "Autodesk\Navisworks $p $v"
    if (-not (Test-Path (Join-Path $dir 'Autodesk.Navisworks.Timeliner.dll'))) { continue }
    $achadas++
    Write-Host "Navisworks $p ${v}: compilando..."
    Remove-Item -Recurse -Force (Join-Path $PSScriptRoot 'IrisExport\obj') -ErrorAction SilentlyContinue
    dotnet build $proj -c Release "-p:NavisVersion=$v" "-p:NavisProduct=$p" "-p:NavisDir=$dir" -v q
    if ($LASTEXITCODE -ne 0) { Write-Warning "A compilação falhou para o Navisworks $p $v (veja o erro acima)"; continue }
    $dest = Join-Path $dir "Plugins\IrisExport"
    Remove-Item -Recurse -Force (Join-Path $env:APPDATA "Autodesk Navisworks $p $v\Plugins\IrisExport") -ErrorAction SilentlyContinue      # local antigo, que o Navisworks não lê
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item (Join-Path $PSScriptRoot "IrisExport\bin\$v\IrisExport.dll") $dest -Force
    Copy-Item (Join-Path $PSScriptRoot "IrisExport\bin\$v\iris_*.png") $dest -Force      # ícones do botão
    Write-Host "  instalado em $dest"
    $feitas++
  }
}
if ($achadas -eq 0) { Write-Host "Nenhum Navisworks $($Versoes -join ', ') encontrado em $env:ProgramW6432\Autodesk." }
elseif ($feitas -eq 0) { Write-Host "Nenhuma versão foi instalada: veja os erros acima." }
else { Write-Host "Pronto. Abra o Navisworks: aba Tool Add-ins 1 > Exportar para o Iris." }
