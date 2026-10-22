# Instala o MetroSP para todas as versões do Revit encontradas neste computador.
# Uso: clique duas vezes em Instalar.bat (ou rode este script). -Desinstalar remove.
param([switch]$Desinstalar)

$here = $PSScriptRoot
$instalados = 0
foreach ($dir in Get-ChildItem $here -Directory | Where-Object { $_.Name -match '^\d{4}$' }) {
    $v = $dir.Name
    if (-not (Test-Path "$env:ProgramFiles\Autodesk\Revit $v")) { continue }

    $addins = "$env:APPDATA\Autodesk\Revit\Addins\$v"
    if ($Desinstalar) {
        Remove-Item "$addins\MetroSP.addin", "$addins\MetroSP" -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "Removido do Revit $v"
    } else {
        New-Item $addins -ItemType Directory -Force | Out-Null
        Copy-Item "$($dir.FullName)\*" $addins -Recurse -Force
        Write-Host "Instalado no Revit $v"
    }
    $instalados++
}
if ($instalados -eq 0) { Write-Host 'Nenhuma versão compatível do Revit (2025-2027) encontrada.' -ForegroundColor Yellow }
Write-Host 'Feche e abra o Revit para carregar a aba MetroSP.'
