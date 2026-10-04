<#
    Ex-libris (Photino) — publication Windows et Linux, puis installateur Windows.

        .\publier-photino.ps1              # tout
        .\publier-photino.ps1 -SansSetup   # publications seulement

    Produit :
        publish\photino\win-x64\                      l'application Windows (dossier)
        publish\photino\Exlibris-linux-x64.tar.gz     l'application Linux, à copier sur le poste
        installateur\Installer-Exlibris-Photino-*.exe l'installateur Windows

    Sous Linux, sur le poste : extraire l'archive dans un dossier, puis
        bash installer.sh

    Pendant de publier.ps1 (WPF), qui reste en place : les deux versions
    s'installent côte à côte (voir installateur\ExlibrisPhotino.iss).
#>

[CmdletBinding()]
param(
    [switch]$SansSetup
)

$ErrorActionPreference = 'Stop'
$racine = $PSScriptRoot
$projet = Join-Path $racine 'Exlibris.Photino\Exlibris.Photino.csproj'
$sortie = Join-Path $racine 'publish\photino'

# --- Les tests d'abord : même règle que publier.ps1.
Write-Host "`n=== Tests ===" -ForegroundColor Cyan
dotnet test "$racine\Exlibris.Tests\Exlibris.Tests.csproj" --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Des tests échouent : publication annulée." }

# --- Publications
#
#   --self-contained : le poste n'a pas besoin de .NET.
#   DebugType=none   : pas de .pdb.
#
# ⚠️ PAS de PublishSingleFile, contrairement au WPF : Photino lit wwwroot (pages,
# styles, scripts) sur le DISQUE, à côté de l'exécutable. Un fichier unique
# démarrerait sur une fenêtre blanche.
# ⚠️ PAS de PublishTrimmed ni d'AOT : Blazor, MudBlazor, Syncfusion et Dapper
# trouvent leurs types par réflexion.
# Les thèmes Syncfusion inutilisés sont retirés par la cible
# ElaguerThemesSyncfusion du projet.
Remove-Item $sortie -Recurse -Force -ErrorAction SilentlyContinue

foreach ($rid in 'win-x64', 'linux-x64') {
    Write-Host "`n=== Publication $rid ===" -ForegroundColor Cyan
    dotnet publish $projet -c Release -r $rid --self-contained true `
        -p:DebugType=none -o (Join-Path $sortie $rid) --nologo
    if ($LASTEXITCODE -ne 0) { throw "La publication $rid a échoué." }

    $dossier = Join-Path $sortie $rid
    $mo = [math]::Round((Get-ChildItem $dossier -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
    Write-Host "  $rid : $mo Mo" -ForegroundColor Green

    # Même contrôle que publier.ps1 : sans ce fichier, ni licence Syncfusion
    # (bannière d'évaluation) ni accès à Lirevia — et rien ne le signale.
    if (-not (Test-Path (Join-Path $dossier 'appsettings.Local.json'))) {
        Write-Warning "appsettings.Local.json absent de la publication $rid : ni licence Syncfusion, ni accès à Lirevia."
    }
}

# --- Archive Linux
#
# UNE archive, jamais une copie fichier par fichier (une copie interrompue a
# déjà livré des scripts tronqués ailleurs). tar de Windows ne pose pas le
# droit d'exécution : installer.sh s'en charge, et se lance par « bash ».
$archive = Join-Path $sortie 'Exlibris-linux-x64.tar.gz'
tar -czf $archive -C (Join-Path $sortie 'linux-x64') .
if ($LASTEXITCODE -ne 0) { throw "La création de l'archive Linux a échoué." }
Write-Host "  $archive ($([math]::Round((Get-Item $archive).Length / 1MB, 1)) Mo)" -ForegroundColor Green

if ($SansSetup) { Write-Host "`nTerminé (sans installateur)." -ForegroundColor Green; return }

# --- Installateur Windows (mêmes emplacements possibles d'Inno Setup que publier.ps1)
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Warning "Inno Setup introuvable. Installez-le puis relancez :"
    Write-Warning "    winget install JRSoftware.InnoSetup"
    return
}

Write-Host "`n=== Installateur Windows ===" -ForegroundColor Cyan
& $iscc "$racine\installateur\ExlibrisPhotino.iss"
if ($LASTEXITCODE -ne 0) { throw "La compilation de l'installateur a échoué." }

Get-ChildItem "$racine\installateur\Installer-Exlibris-Photino-*.exe" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1 |
    ForEach-Object {
        Write-Host "`n  $($_.FullName)  ($([math]::Round($_.Length / 1MB, 1)) Mo)" -ForegroundColor Green
    }
