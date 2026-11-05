# Compila o add-in para cada versão do Revit e gera o instalador.
#   .\build.ps1                      -> 2025, 2026, 2027
#   .\build.ps1 -Versions 2025       -> só uma versão
param([string[]]$Versions = @('2025', '2026', '2027'))

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$proj = Join-Path $root 'src\MetroSP\MetroSP.csproj'

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }

foreach ($v in $Versions) {
    Write-Host "=== Revit $v ===" -ForegroundColor Cyan
    $out = Join-Path $dist "$v\MetroSP"
    dotnet build $proj -c Release --no-incremental -p:RevitVersion=$v -o $out
    if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar para Revit $v" }
    Copy-Item (Join-Path $root 'src\MetroSP\MetroSP.addin') (Join-Path $dist $v)
    Remove-Item (Join-Path $out 'MetroSP.addin') -ErrorAction SilentlyContinue
}

# Pacote .zip com script de instalação (não precisa de Inno Setup)
Copy-Item (Join-Path $root 'installer\Instalar.ps1') $dist
Copy-Item (Join-Path $root 'installer\Instalar.bat') $dist
$zip = Join-Path $root 'MetroSP-instalador.zip'
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip
Write-Host "Pacote: $zip" -ForegroundColor Green

# Instalador .exe, se o Inno Setup 6 estiver instalado
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($iscc) {
    [xml]$csproj = Get-Content $proj
    $versao = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
    & $iscc "/DAppVersion=$versao" (Join-Path $root 'installer\MetroSP.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Falha no Inno Setup' }
} else {
    Write-Host 'Inno Setup 6 não encontrado: gerado só o .zip.' -ForegroundColor Yellow
}
