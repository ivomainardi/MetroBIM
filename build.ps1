# Compila os plugins e monta o instalador único MetroSP-Setup-<versão>.exe.
#   .\build.ps1                          -> tudo (o Navisworks só se ele estiver instalado neste PC ou se vier -Navisworks)
#   .\build.ps1 -Navisworks navisworks-1.0.0.zip   -> usa as DLLs do Navisworks já compiladas (zip ou pasta com 2025, 2026, 2027)
#   .\build.ps1 -SoNavisworks            -> só compila o Navisworks e gera navisworks-<versão>.zip (para anexar à release)
# A versão do pacote é o primeiro "## x.y.z" do NOTAS-DA-VERSAO.md. Cada plugin mantém a versão dele no .csproj.
# Requisitos: SDK .NET 8 e 10 e Inno Setup 6. O Navisworks precisa estar instalado para compilar o plugin dele.
param(
    [string]$Navisworks = '',
    [switch]$SoNavisworks
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'

function VersaoCsproj([string]$caminho) {
    [xml]$x = Get-Content $caminho
    ($x.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
}

$navisProj = Join-Path $root 'navisworks\IrisExport\IrisExport.csproj'
$navisVersao = VersaoCsproj $navisProj

# Navisworks: uma DLL por versão, compilada contra a API do Navisworks instalado (Manage ou Simulate).
function CompilarNavisworks([string]$saida) {
    $feitas = 0
    foreach ($v in '2025', '2026', '2027') {
        foreach ($p in 'Manage', 'Simulate') {
            $dir = Join-Path $env:ProgramW6432 "Autodesk\Navisworks $p $v"
            if (-not (Test-Path (Join-Path $dir 'Autodesk.Navisworks.Timeliner.dll'))) { continue }
            Write-Host "=== Navisworks $p $v ===" -ForegroundColor Cyan
            Remove-Item -Recurse -Force (Join-Path $root 'navisworks\IrisExport\obj') -ErrorAction SilentlyContinue
            $out = Join-Path $saida $v
            dotnet build $navisProj -c Release --no-incremental "-p:NavisVersion=$v" "-p:NavisProduct=$p" "-p:NavisDir=$dir" -o $out -v q
            if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar para o Navisworks $p $v" }
            $feitas++
            break
        }
    }
    return $feitas
}

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory $dist | Out-Null

if ($SoNavisworks) {
    $n = CompilarNavisworks (Join-Path $dist 'navisworks')
    if ($n -eq 0) { throw 'Nenhum Navisworks 2025, 2026 ou 2027 instalado neste PC.' }
    $zip = Join-Path $root "navisworks-$navisVersao.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path (Join-Path $dist 'navisworks\*') -DestinationPath $zip
    Write-Host "Pacote do Navisworks: $zip ($n versões)" -ForegroundColor Green
    return
}

$notas = Get-Content (Join-Path $root 'NOTAS-DA-VERSAO.md') -Raw -Encoding UTF8
$m = [regex]::Match($notas, '(?m)^## (\d+\.\d+\.\d+)')
if (-not $m.Success) { throw 'NOTAS-DA-VERSAO.md sem uma seção "## x.y.z"' }
$versao = $m.Groups[1].Value

# Revit: uma DLL por versão (2022 a 2024 em .NET Framework 4.8, 2025-2026 em .NET 8, 2027 em .NET 10).
$revitProj = Join-Path $root 'revit\src\MetroSP\MetroSP.csproj'
foreach ($v in '2022', '2023', '2024', '2025', '2026', '2027') {
    Write-Host "=== Revit $v ===" -ForegroundColor Cyan
    $out = Join-Path $dist "revit\$v\MetroSP"
    dotnet build $revitProj -c Release --no-incremental -p:RevitVersion=$v -o $out
    if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar para o Revit $v" }
    Copy-Item (Join-Path $root 'revit\src\MetroSP\MetroSP.addin') (Join-Path $dist "revit\$v")
    Remove-Item (Join-Path $out 'MetroSP.addin') -ErrorAction SilentlyContinue
}

# Civil 3D: o csproj compila R25 e R26 dentro do MetroSP.bundle.
Write-Host '=== Civil 3D ===' -ForegroundColor Cyan
$civilProj = Join-Path $root 'civil3d\src\MetroSP.csproj'
dotnet build $civilProj -c Release --no-incremental
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o Civil 3D' }
Copy-Item (Join-Path $root 'civil3d\MetroSP.bundle') (Join-Path $dist 'civil3d\MetroSP.bundle') -Recurse

# Navisworks: DLLs prontas (-Navisworks) ou compiladas aqui; sem nenhuma das duas, o instalador sai sem o Navisworks.
$navisDist = Join-Path $dist 'navisworks'
if ($Navisworks) {
    if ($Navisworks.EndsWith('.zip')) { Expand-Archive $Navisworks -DestinationPath $navisDist }
    else { Copy-Item $Navisworks $navisDist -Recurse }
} elseif ((CompilarNavisworks $navisDist) -eq 0) {
    Write-Host 'Navisworks não encontrado e -Navisworks não informado: o instalador sai SEM o Navisworks.' -ForegroundColor Yellow
}

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 não encontrado (winget install JRSoftware.InnoSetup).' }

& $iscc "/DVersao=$versao" "/DRevitVersion=$(VersaoCsproj $revitProj)" "/DCivil3DVersion=$(VersaoCsproj $civilProj)" `
    "/DNavisworksVersion=$navisVersao" "/DDist=$dist" (Join-Path $root 'instalador\MetroSP.iss')
if ($LASTEXITCODE -ne 0) { throw 'Falha no Inno Setup' }
Write-Host "Instalador: $(Join-Path $root "MetroSP-Setup-$versao.exe")" -ForegroundColor Green
