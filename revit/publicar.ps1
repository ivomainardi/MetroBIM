# Publica uma versão do MetroSP: compila aqui no PC e sobe a release no GitHub.
# O GitHub não compila nada (sem minutos do Actions).
#   .\publicar.ps1
# Requisitos: SDK .NET 8 e 10, GitHub CLI (gh) logado, main local igual à origin/main.

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

[xml]$proj = Get-Content src/MetroSP/MetroSP.csproj
$v = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $v) { throw 'Version não encontrada no MetroSP.csproj' }
Write-Host "Versão: $v" -ForegroundColor Cyan

if (-not (Select-String -Path NOTAS-DA-VERSAO.md -Pattern ([regex]::Escape($v)) -Quiet)) {
    throw "NOTAS-DA-VERSAO.md não menciona a versão $v"
}

git fetch origin main --tags
if ($LASTEXITCODE -ne 0) { throw 'Falha no git fetch' }
if (git status --porcelain) { throw 'Há alterações não commitadas. Faça commit ou descarte antes.' }
$sha = git rev-parse HEAD
if ($sha -ne (git rev-parse origin/main)) { throw 'O HEAD local não é a origin/main. Faça git checkout main e git pull.' }
if (git tag -l "v$v") { throw "A tag v$v já existe: suba a <Version> no MetroSP.csproj." }

$iss = 'installer/MetroSP.iss'
$issOriginal = Get-Content $iss -Raw
try {
    ($issOriginal -replace '#define AppVersion ".*"', "#define AppVersion `"$v`"") | Set-Content $iss -NoNewline
    ./build.ps1
} finally {
    # O .iss volta como estava para não deixar alteração no repositório
    $issOriginal | Set-Content $iss -NoNewline
}

$zip = "MetroSP-$v-instalador.zip"
if (Test-Path $zip) { Remove-Item $zip }
Rename-Item MetroSP-instalador.zip $zip
$arquivos = @($zip) + @(Get-ChildItem "MetroSP-Setup-*.exe" -ErrorAction SilentlyContinue | ForEach-Object Name)

gh release create "v$v" @arquivos --target $sha --title "MetroSP $v" --notes-file NOTAS-DA-VERSAO.md
if ($LASTEXITCODE -ne 0) { throw 'Falha ao criar a release no GitHub' }
Write-Host "Publicado: MetroSP $v" -ForegroundColor Green
