# Publica SimTrade (win-x64 self-contained) y genera el Setup.exe con Inno Setup.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $root "Publicacion"
$dist = Join-Path $root "dist"
$iss = Join-Path $root "installer\SimTrade.iss"
$isccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "No se encontró ISCC.exe (Inno Setup 6). Instálalo con: winget install JRSoftware.InnoSetup"
}

if (Test-Path $out) {
    Remove-Item $out -Recurse -Force
}
New-Item -ItemType Directory -Path $dist -Force | Out-Null

Write-Host "Publicando SimTrade (Release, win-x64, self-contained)..."
dotnet publish (Join-Path $root "SimTrade.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishReadyToRun=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $out
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish falló."
}

# Quitar PDB si quedaron.
Get-ChildItem $out -Filter *.pdb -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force

$buildNumber = "0"
$buildFile = Join-Path $root "build.number"
if (Test-Path $buildFile) {
    $buildNumber = (Get-Content $buildFile -Raw).Trim()
}
$version = "1.0.$buildNumber"
Write-Host "Versión: $version"

Write-Host "Compilando instalador con Inno Setup..."
& $iscc `
    "/DAppVersion=$version" `
    "/DSourceDir=$out" `
    "/DOutputDir=$dist" `
    $iss
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup falló."
}

$setup = Join-Path $dist "SimTrade-Setup-$version.exe"
if (-not (Test-Path $setup)) {
    throw "No se generó el instalador esperado: $setup"
}

$sizeMb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host ""
Write-Host "Listo."
Write-Host "  Carpeta portable : $out"
Write-Host "  Instalador       : $setup ($sizeMb MB)"
Write-Host "  Versión          : $version"
