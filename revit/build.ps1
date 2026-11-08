# Compila o add-in para cada versão do Revit e gera os instaladores.
#   .\build.ps1                      -> 2022, 2025, 2026, 2027
#   .\build.ps1 -Versions 2025       -> só uma versão
# Dois pacotes, sempre separados:
#   MetroSP-instalador.zip / MetroSP-Setup-<versão>.exe                  -> Revit 2025, 2026 e 2027
#   MetroSP-Revit2022-instalador.zip / MetroSP-Revit2022-Setup-<versão>.exe -> Revit 2022 (.NET Framework 4.8)
param([string[]]$Versions = @('2022', '2025', '2026', '2027'))

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$proj = Join-Path $root 'src\MetroSP\MetroSP.csproj'

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }

# dist\revit\<versão> (2025+) e dist\revit2022\2022
function Pasta([string]$v) { if ($v -eq '2022') { 'revit2022' } else { 'revit' } }

foreach ($v in $Versions) {
    Write-Host "=== Revit $v ===" -ForegroundColor Cyan
    $base = Join-Path $dist (Pasta $v)
    $out = Join-Path $base "$v\MetroSP"
    dotnet build $proj -c Release --no-incremental -p:RevitVersion=$v -o $out
    if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar para Revit $v" }
    Copy-Item (Join-Path $root 'src\MetroSP\MetroSP.addin') (Join-Path $base $v)
    Remove-Item (Join-Path $out 'MetroSP.addin') -ErrorAction SilentlyContinue
}

[xml]$csproj = Get-Content $proj
$versao = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { Write-Host 'Inno Setup 6 não encontrado: gerados só os .zip.' -ForegroundColor Yellow }

foreach ($pacote in @(
    @{ Pasta = 'revit';     Zip = 'MetroSP-instalador.zip';           Iss = @() },
    @{ Pasta = 'revit2022'; Zip = 'MetroSP-Revit2022-instalador.zip'; Iss = @('/DRevit2022') }
)) {
    $base = Join-Path $dist $pacote.Pasta
    if (-not (Test-Path $base)) { continue }

    # Pacote .zip com script de instalação (não precisa de Inno Setup)
    Copy-Item (Join-Path $root 'installer\Instalar.ps1') $base
    Copy-Item (Join-Path $root 'installer\Instalar.bat') $base
    $zip = Join-Path $root $pacote.Zip
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path (Join-Path $base '*') -DestinationPath $zip
    Write-Host "Pacote: $zip" -ForegroundColor Green

    # Instalador .exe, se o Inno Setup 6 estiver instalado
    if ($iscc) {
        & $iscc "/DAppVersion=$versao" @($pacote.Iss) (Join-Path $root 'installer\MetroSP.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Falha no Inno Setup' }
    }
}
