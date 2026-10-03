<#
    Ex-libris — publication et fabrication de l'installateur.

        .\publier.ps1              # publie puis construit l'installateur
        .\publier.ps1 -SansSetup   # publie seulement

    Produit :
        publish\compresse\Exlibris.exe        l'application, en un seul fichier
        installateur\Installer-Exlibris-*.exe l'installateur à distribuer

    Prérequis, une fois pour toutes :
        winget install JRSoftware.InnoSetup

    La démarche complète, et le pourquoi de chaque réglage :
        docs\publication.md
#>

[CmdletBinding()]
param(
    [switch]$SansSetup
)

$ErrorActionPreference = 'Stop'
$racine = $PSScriptRoot

# --- Les tests d'abord.
#
# Publier une version qui ne passe pas ses propres tests n'a pas de sens : sur
# trois postes, la correction demande de refaire la tournée. Le coût est de
# quelques secondes, à comparer à un déplacement.
Write-Host "`n=== Tests ===" -ForegroundColor Cyan
dotnet test "$racine\Exlibris.slnx" --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Des tests échouent : publication annulée." }

# --- Publication
#
#   --self-contained    : le poste cible n'a pas besoin d'un .NET installé.
#                         C'est ce qui rend l'application donnable telle quelle.
#   PublishSingleFile   : un seul .exe, sans dossier de DLL à côté.
#   IncludeNativeLibraries... : sans lui, e_sqlite3.dll resterait DEHORS du
#                         fichier unique — et l'application planterait à la
#                         première requête, sur un poste où elle est absente.
#   EnableCompression   : 149 Mo -> 66 Mo, mesuré le 30/08/2026. Le prix est un
#                         démarrage un peu plus lent, la décompression se faisant
#                         au lancement.
#   DebugType=none      : sinon les .pdb partent avec, pour rien.
#
# ⚠️ PAS DE PublishTrimmed. Le rognage supprime ce qu'il croit inutilisé, or
# Syncfusion, Dapper et WPF trouvent l'essentiel de leurs types par réflexion :
# l'application se compile, se publie, et échoue à l'ouverture d'un écran — sur
# le poste de quelqu'un d'autre, avec un message incompréhensible.
Write-Host "`n=== Publication ===" -ForegroundColor Cyan
$sortie = Join-Path $racine 'publish\compresse'

dotnet publish "$racine\Exlibris\Exlibris.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $sortie --nologo
if ($LASTEXITCODE -ne 0) { throw "La publication a échoué." }

$exe = Join-Path $sortie 'Exlibris.exe'
$taille = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "  Exlibris.exe : $taille Mo" -ForegroundColor Green

# ⚠️ Contrôle explicite : ce fichier porte la clé Syncfusion et l'adresse de
# Lirevia. Publié sans lui, l'application démarre quand même — avec la bannière
# d'évaluation, et sans jamais interroger le catalogue. Un défaut qu'on ne
# remarque qu'une fois chez l'utilisateur.
if (-not (Test-Path (Join-Path $sortie 'appsettings.Local.json'))) {
    Write-Warning "appsettings.Local.json absent de la publication : ni licence Syncfusion, ni accès à Lirevia."
}

if ($SansSetup) { Write-Host "`nTerminé (sans installateur)." -ForegroundColor Green; return }

# --- Installateur
#
# Trois emplacements, parce que winget installe Inno Setup LÀ OÙ IL PEUT :
# sans élévation il le pose dans les programmes de l'utilisateur, avec
# élévation dans Program Files. Ne chercher qu'à un seul endroit fait échouer
# le script sur une machine où l'outil est pourtant bien présent.
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

Write-Host "`n=== Installateur ===" -ForegroundColor Cyan
& $iscc "$racine\installateur\Exlibris.iss"
if ($LASTEXITCODE -ne 0) { throw "La compilation de l'installateur a échoué." }

Get-ChildItem "$racine\installateur\Installer-Exlibris-*.exe" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1 |
    ForEach-Object {
        $mo = [math]::Round($_.Length / 1MB, 1)
        Write-Host "`n  $($_.FullName)  ($mo Mo)" -ForegroundColor Green
    }
