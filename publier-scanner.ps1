<#
    Ex-libris Scanner — fabrication de l'APK à distribuer.

        .\publier-scanner.ps1               # incrémente la version, publie
        .\publier-scanner.ps1 -SansTests    # saute les tests
        .\publier-scanner.ps1 -MemeVersion  # republie sans incrémenter

    Produit :
        publish\android\Exlibris-Scanner-0.1.0-3.apk

    Prérequis, une fois pour toutes :
        1. keytool -genkeypair -v -keystore exlibris.keystore -alias exlibris `
                   -keyalg RSA -keysize 2048 -validity 10000
        2. copier Exlibris.Scanner\signature.example.props en signature.props
           et y renseigner chemin, alias et mots de passe.

    ─────────────────────────────────────────────────────────────────────────
    POURQUOI UN SCRIPT PLUTÔT QU'UNE LIGNE DE COMMANDE

    La commande complète porte six paramètres, dont deux mots de passe. Tapée à
    la main, elle les dépose dans l'historique du terminal — que personne ne
    nettoie — et il suffit d'oublier « AndroidPackageFormat » pour produire un
    .aab que le destinataire ne peut pas installer, sans le moindre message.

    Le script écarte aussi l'oubli le plus coûteux : incrémenter le numéro de
    version. Android REFUSE d'installer par-dessus une version dont le numéro
    n'a pas augmenté, et le refus est muet côté téléphone — « application non
    installée », sans dire pourquoi.
#>

[CmdletBinding()]
param(
    [switch]$SansTests,
    [switch]$MemeVersion
)

$ErrorActionPreference = 'Stop'
$racine    = $PSScriptRoot
$projet    = Join-Path $racine 'Exlibris.Scanner\Exlibris.Scanner.csproj'
$signature = Join-Path $racine 'Exlibris.Scanner\signature.props'
$sortie    = Join-Path $racine 'publish\android'

# ---------------------------------------------------------------------------
#  1. Ce qu'il faut avoir avant de commencer.
#
#  Les vérifications sont EN TÊTE et leurs messages disent quoi faire. Sans
#  elles, l'absence de signature se manifeste par une erreur de MSBuild au bout
#  de plusieurs minutes de compilation Android, dans un mur de sortie.
# ---------------------------------------------------------------------------
if (-not (Test-Path $signature))
{
    throw @"
Signature absente : $signature

  1. Créez la clé, une fois pour toutes :
       keytool -genkeypair -v -keystore exlibris.keystore -alias exlibris ``
               -keyalg RSA -keysize 2048 -validity 10000

  2. Copiez le modèle et renseignez-le :
       copy Exlibris.Scanner\signature.example.props Exlibris.Scanner\signature.props

Le fichier .keystore est IRREMPLAÇABLE : sauvegardez-le hors du projet.
"@
}

# ⚠️ LA LECTURE EST PROTÉGÉE, ET LE FICHIER N'EST JAMAIS RÉAFFICHÉ.
#
# Un « [xml]$contenu » qui échoue lève une erreur de conversion PowerShell dont
# le message CONTIENT LA VALEUR CONVERTIE — donc le fichier entier, mots de
# passe compris, déversés dans le terminal et dans son historique. Constaté le
# 30/08/2026 sur un mot de passe portant une esperluette.
#
# Le message ci-dessous nomme donc le défaut sans jamais montrer le contenu.
try
{
    $xml = [xml](Get-Content $signature -Raw)
}
catch
{
    throw @"
$signature n'est pas un XML valide.

La cause la plus fréquente : un mot de passe contenant un caractère réservé.
Dans un fichier XML il faut écrire

    &   ->  &amp;          <   ->  &lt;          >   ->  &gt;

Les autres — # ! $ @ et le reste — s'écrivent tels quels, sans guillemets.
Un pour-cent suivi de deux caractères 0-9 ou A-F doit s'écrire %25 (MSBuild le
décoderait sinon).

Corrigez le fichier et relancez. Son contenu n'est pas reproduit ici,
volontairement.
"@
}

# Le magasin est relu ici parce qu'un chemin périmé — disque externe débranché,
# dossier renommé — donne sinon une erreur de signature à la toute fin, après
# la compilation complète.
$magasin = $xml.Project.PropertyGroup.AndroidSigningKeyStore
if (-not (Test-Path $magasin))
{
    throw "Magasin de clés introuvable : $magasin`n" +
          "Corrigez AndroidSigningKeyStore dans $signature."
}

# ---------------------------------------------------------------------------
#  2. Les tests.
#
#  Le scanner partage Exlibris.Shared avec l'application de bureau — dont le
#  format du fichier d'inventaire, seul lien entre le téléphone et le poste. Un
#  désaccord sur ce format ne se voit pas à l'import : il produit des rangements
#  tronqués, découverts des semaines plus tard devant une étagère.
# ---------------------------------------------------------------------------
if (-not $SansTests)
{
    Write-Host "`n=== Tests ===" -ForegroundColor Cyan
    dotnet test (Join-Path $racine 'Exlibris.Tests\Exlibris.Tests.csproj') --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Des tests échouent : publication annulée." }
}

# ---------------------------------------------------------------------------
#  3. Le numéro de version.
#
#  ⚠️ DEUX NUMÉROS, et un seul compte pour Android.
#
#  ApplicationVersion est le « versionCode » : un entier qui doit STRICTEMENT
#  augmenter. C'est lui qu'Android compare, et lui seul. Le téléphone refuse
#  l'installation par-dessus une version dont le numéro n'a pas monté, en
#  affichant « application non installée » sans autre explication — trois quarts
#  d'heure de recherche pour un entier oublié.
#
#  ApplicationDisplayVersion (0.1.0) n'est qu'une étiquette lisible. Android
#  l'ignore pour cette règle. Elle se change à la main, quand VOUS jugez que la
#  version le mérite : l'incrémenter automatiquement lui ferait perdre son sens.
# ---------------------------------------------------------------------------
$octets  = [System.IO.File]::ReadAllBytes($projet)
$avecBom = $octets.Length -ge 3 -and $octets[0] -eq 0xEF -and $octets[1] -eq 0xBB -and $octets[2] -eq 0xBF
$texte   = Get-Content $projet -Raw

if ($texte -notmatch '<ApplicationVersion>(\d+)</ApplicationVersion>')
{
    throw "ApplicationVersion introuvable dans $projet."
}
$version = [int]$Matches[1]

if (-not $MemeVersion)
{
    $version++
    $texte = $texte -replace '<ApplicationVersion>\d+</ApplicationVersion>',
                             "<ApplicationVersion>$version</ApplicationVersion>"

    # L'encodage d'origine est PRÉSERVÉ, marque d'ordre comprise : la réécrire
    # autrement ferait apparaître le csproj entier comme modifié à chaque
    # publication, et le vrai changement — un chiffre — deviendrait invisible.
    [System.IO.File]::WriteAllText($projet, $texte, (New-Object System.Text.UTF8Encoding $avecBom))
    Write-Host "`nVersion : $($version - 1) -> $version" -ForegroundColor Yellow
}
else
{
    Write-Host "`nVersion inchangée : $version" -ForegroundColor Yellow
    Write-Host "  (une installation par-dessus la précédente sera refusée par le téléphone)" -ForegroundColor DarkGray
}

$etiquette = ([regex]::Match($texte, '<ApplicationDisplayVersion>([^<]+)</ApplicationDisplayVersion>')).Groups[1].Value

# ---------------------------------------------------------------------------
#  4. La publication.
#
#  Aucun mot de passe ici : ils viennent de signature.props, importé par le
#  csproj. Aucun -p:AndroidPackageFormat non plus, pour la même raison — le
#  csproj le fixe à « apk » dès que la signature est présente, ce qui met le
#  réglage hors de portée d'un oubli.
# ---------------------------------------------------------------------------
Write-Host "`n=== Publication Android ===" -ForegroundColor Cyan
Write-Host "  Quelques minutes : la compilation Android est lente." -ForegroundColor DarkGray

dotnet publish $projet -f net10.0-android -c Release
if ($LASTEXITCODE -ne 0) { throw "La publication a échoué." }

# ---------------------------------------------------------------------------
#  5. Récupérer l'APK et le nommer lisiblement.
#
#  Le fichier produit s'appelle « fr.sammut.exlibris.scanner-Signed.apk » —
#  identique d'une version à l'autre. Envoyé tel quel, on ne sait plus laquelle
#  on a transmise, et le destinataire encore moins.
# ---------------------------------------------------------------------------
$publie = Get-ChildItem (Join-Path $racine 'Exlibris.Scanner\bin\Release\net10.0-android') `
              -Recurse -Filter '*-Signed.apk' -ErrorAction SilentlyContinue |
          Sort-Object LastWriteTime -Descending | Select-Object -First 1

if (-not $publie) { throw "APK introuvable après publication." }

# ---------------------------------------------------------------------------
#  ⚠️ LE NOM « -Signed » NE PROUVE RIEN.
#
#  Le SDK Android appelle TOUJOURS sa sortie « …-Signed.apk », qu'il l'ait
#  signée ou non. Constaté le 30/08/2026 : une publication en Release sans
#  magasin de clés a produit un « fr.sammut.exlibris.scanner-Signed.apk » de
#  37 Mo, parfaitement non signé — sans un avertissement.
#
#  Le défaut ne se voit qu'au bout de la chaîne, chez le destinataire, sous la
#  forme d'un « Échec de l'analyse du package » qui ne dit pas pourquoi. Entre
#  les deux : un envoi, une explication au téléphone, et une soirée.
#
#  On vérifie donc les OCTETS, sans outil externe :
#    • v2/v3 → un bloc « APK Sig Block 42 » avant le répertoire central ;
#    • v1    → les fichiers META-INF/MANIFEST.MF et META-INF/*.RSA.
#  L'un des deux suffit ; « aucun des deux » veut dire non signé.
# ---------------------------------------------------------------------------
Add-Type -AssemblyName System.IO.Compression.FileSystem

$flux = [System.IO.File]::OpenRead($publie.FullName)
try
{
    $debut = [Math]::Max(0, $flux.Length - 4MB)
    $flux.Position = $debut
    $queue = New-Object byte[] ($flux.Length - $debut)
    [void]$flux.Read($queue, 0, $queue.Length)
}
finally { $flux.Dispose() }

# La recherche se fait sur une VUE TEXTE des octets, en Latin-1 : cet encodage
# transpose chaque octet en un caractère et un seul, donc « IndexOf » sur la
# chaîne équivaut à une recherche binaire — sans la boucle d'octets, qui en
# PowerShell serait à la fois illisible et lente.
$vue = [System.Text.Encoding]::Latin1.GetString($queue)
$v2  = $vue.Contains('APK Sig Block 42')

$archive = [System.IO.Compression.ZipFile]::OpenRead($publie.FullName)
try   { $v1 = [bool]($archive.Entries | Where-Object { $_.FullName -eq 'META-INF/MANIFEST.MF' }) }
finally { $archive.Dispose() }

if (-not $v2 -and -not $v1)
{
    throw @"
L'APK produit n'est PAS SIGNÉ, malgré son nom « -Signed.apk ».

  $($publie.FullName)

Android refusera de l'installer (« Échec de l'analyse du package »).

Vérifiez que Exlibris.Scanner\signature.props porte bien les quatre valeurs et
que le magasin de clés est lisible, puis republiez.
"@
}

Write-Host ("`nSignature : " + $(if ($v2) { 'schéma v2/v3 présent' } else { 'schéma v1 présent' })) -ForegroundColor Green

New-Item -ItemType Directory -Force -Path $sortie | Out-Null
$final = Join-Path $sortie "Exlibris-Scanner-$etiquette-$version.apk"
Copy-Item $publie.FullName $final -Force

$mo = [math]::Round((Get-Item $final).Length / 1MB, 1)

Write-Host "`n=== Prêt ===" -ForegroundColor Green
Write-Host "  $final"
Write-Host "  $mo Mo — version $etiquette, code $version"

Write-Host "`nPour l'envoyer :" -ForegroundColor Cyan
Write-Host "  ⚠️ Gmail REFUSE les .apk, y compris dans un ZIP — il regarde dedans."
Write-Host "     Déposez le fichier sur Drive / OneDrive / WeTransfer et envoyez le lien."
Write-Host "`nSur le téléphone du destinataire :" -ForegroundColor Cyan
Write-Host "  • autoriser « Installer des applications inconnues » pour le navigateur"
Write-Host "    (réglage par application, pas global) ;"
Write-Host "  • Play Protect avertira « application non analysée » : Installer quand même."
