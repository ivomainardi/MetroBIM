# Compila o MetroSP para Civil 3D (R25 e R26) e gera o instalador.
#   .\build.ps1
# Requisitos: SDK .NET 8 e 10. O .exe sai se o Inno Setup 6 estiver instalado; o .zip sai sempre.

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$proj = Join-Path $root 'src\MetroSP.csproj'

dotnet build $proj -c Release --no-incremental
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar' }

[xml]$csproj = Get-Content $proj
$versao = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version

# Pacote .zip (bundle + instalar.bat), para quem não usa o .exe
$zip = Join-Path $root "metrosp-civil3d-$versao.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $root 'MetroSP.bundle'), (Join-Path $root 'instalar.bat') -DestinationPath $zip
Write-Host "Pacote: $zip" -ForegroundColor Green

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($iscc) {
    & $iscc "/DAppVersion=$versao" (Join-Path $root 'installer\MetroSP-Civil3D.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Falha no Inno Setup' }
} else {
    Write-Host 'Inno Setup 6 não encontrado: gerado só o .zip.' -ForegroundColor Yellow
}
