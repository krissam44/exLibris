<#
    Fabrique l'icône de l'application à partir d'un PNG.

        .\faire-icone.ps1                          # source et destination par défaut
        .\faire-icone.ps1 -Source autre-logo.png   # depuis un autre visuel

    À rejouer quand le logo change. Le .ico produit est versionné avec le
    projet : la compilation ne le régénère pas, et c'est voulu — fabriquer une
    image à chaque build ferait dépendre le binaire d'un outil graphique.

    ─────────────────────────────────────────────────────────────────────────
    POURQUOI CE SCRIPT PLUTÔT QU'UN CONVERTISSEUR EN LIGNE

    Le .ico qui existait côté Lirevia ne portait qu'UNE image, de 240×256 —
    ni carrée, ni déclinée. Windows réduisait donc lui-même cette grande image
    pour l'afficher en 16×16 dans la barre des tâches, avec le flou que cela
    donne. La plupart des convertisseurs en ligne produisent exactement cela.

    Ici les sept tailles sont calculées séparément, chacune avec un
    rééchantillonnage de qualité.
#>

[CmdletBinding()]
param(
    # Le visuel PRÉPARÉ par faire-logo.ps1 : fond transparent, recadré, centré.
    # Surtout pas le fichier d'origine, dont le fond blanc est de la peinture —
    # l'icône serait alors un carré blanc dans la barre des tâches.
    #
    # (La valeur d'avant pointait sur « lirevia-logo-front-bleu.png », reste de
    # la première vie de ce script.)
    [string]$Source      = (Join-Path $PSScriptRoot 'logo-exlibris.png'),
    [string]$Destination = (Join-Path $PSScriptRoot '..\Exlibris\Assets\exlibris.ico'),

    # Couleur de la plaque posée SOUS la marque, ou « aucune » pour garder le
    # fond transparent. Voir le long commentaire plus bas : sans plaque, le E
    # ardoise disparaît sur une barre des tâches sombre.
    [string]$Plaque = '#FFFFFF',

    # Part de la plaque qu'occupe la marque.
    #
    # 0,86 retenu après comparaison à l'écran avec 0,78 : à 16 px — la taille de
    # la barre des tâches, celle qui décide — la lettre y gagne franchement, et
    # elle ne touche toujours pas les angles arrondis. Plus haut, elle les
    # heurterait et paraîtrait rognée.
    [double]$Remplissage = 0.86
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# ⚠️ Les chemins RELATIFS se résolvent depuis le dossier courant de PowerShell,
# et il faut le dire explicitement.
#
# `Set-Location` déplace PowerShell mais PAS le répertoire courant de .NET, dont
# `[IO.Path]::GetFullPath(chemin)` se sert à une seule argument. Un
# « -Destination Exlibris\Assets\exlibris.ico » lancé depuis le projet écrivait
# donc le fichier sous le dossier où le processus avait démarré — ailleurs,
# silencieusement, avec un message de réussite en prime. Constaté le 30/08/2026.
$Source      = [System.IO.Path]::GetFullPath($Source, $PWD.Path)
$Destination = [System.IO.Path]::GetFullPath($Destination, $PWD.Path)

if (-not (Test-Path $Source)) { throw "Visuel introuvable : $Source" }

$origine = [System.Drawing.Image]::FromFile((Resolve-Path $Source))

# ----- Mise au carré.
#
# Le PNG d'origine fait 600×640 : il n'est PAS carré. Une icône doit l'être,
# sinon Windows l'étire pour remplir un cadre carré et la lettre sort déformée.
# On la pose donc CENTRÉE sur un carré transparent, sans autre redimensionnement
# que proportionnel.
$cote = [Math]::Max($origine.Width, $origine.Height)

$carre = New-Object System.Drawing.Bitmap(
    $cote, $cote, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

$g = [System.Drawing.Graphics]::FromImage($carre)
$g.Clear([System.Drawing.Color]::Transparent)
$g.DrawImage($origine,
    [int](($cote - $origine.Width) / 2),
    [int](($cote - $origine.Height) / 2),
    $origine.Width, $origine.Height)
$g.Dispose()
$origine.Dispose()

# ----- La PLAQUE claire sous la marque.
#
# ⚠️ C'EST LA CORRECTION D'UN DÉFAUT CONSTATÉ, pas une préférence de style.
#
# Le E est en bleu ardoise. Posé sur un fond transparent, il disparaît sur une
# barre des tâches sombre — vérifié le 30/08/2026 sur celle de l'utilisateur :
# seule la feuille verte restait visible, la lettre se fondait dans le noir.
#
# Une icône Windows ne peut pas s'adapter au thème : elle est décodée une fois
# et affichée telle quelle, sur clair comme sur sombre. Il lui faut donc porter
# son propre fond. C'est ce que font Word, Acrobat ou Notepad++ — la couleur de
# la marque se lit sur la plaque, jamais sur le bureau.
#
# Android, lui, n'en a PAS besoin : son masque applique déjà la couleur de tuile
# déclarée dans le csproj. D'où le paramètre — l'avant-plan Android reste
# transparent, l'icône Windows reçoit sa plaque.
#
# Les angles arrondis plutôt qu'un carré franc : un carré plein ressemble à une
# vignette de document. Le rayon est proportionnel, donc identique à toutes les
# tailles ; à 16 px il ne reste presque rien, et c'est très bien ainsi.
if ($Plaque -ne 'aucune')
{
    $avecFond = New-Object System.Drawing.Bitmap(
        $cote, $cote, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

    $gp = [System.Drawing.Graphics]::FromImage($avecFond)
    $gp.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $gp.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $gp.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $gp.Clear([System.Drawing.Color]::Transparent)

    $rayon = [int]($cote * 0.18)
    $chemin = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $rayon * 2
    $chemin.AddArc(0, 0, $d, $d, 180, 90)                       # coin haut gauche
    $chemin.AddArc($cote - $d - 1, 0, $d, $d, 270, 90)          # haut droit
    $chemin.AddArc($cote - $d - 1, $cote - $d - 1, $d, $d, 0, 90)   # bas droit
    $chemin.AddArc(0, $cote - $d - 1, $d, $d, 90, 90)           # bas gauche
    $chemin.CloseFigure()

    $brosse = New-Object System.Drawing.SolidBrush (
        [System.Drawing.ColorTranslator]::FromHtml($Plaque))
    $gp.FillPath($brosse, $chemin)
    $brosse.Dispose(); $chemin.Dispose()

    # La marque REPOSE sur la plaque, elle ne la remplit pas : sans cette marge
    # la lettre toucherait les bords arrondis et paraîtrait rognée.
    $dedans = [int]($cote * $Remplissage)
    $marge  = [int](($cote - $dedans) / 2)
    $gp.DrawImage($carre, $marge, $marge, $dedans, $dedans)
    $gp.Dispose()

    $carre.Dispose()
    $carre = $avecFond
}

# ----- Les tailles que Windows réclame.
#
# 16 dans la barre des tâches, 32 sur le bureau, 48 dans l'explorateur, 256
# pour les grandes vignettes. Les fournir toutes évite que le système
# n'improvise une réduction — c'est en 16×16 que la différence se voit le plus.
$tailles = @(16, 24, 32, 48, 64, 128, 256)
$images = @()

foreach ($t in $tailles) {
    $bmp = New-Object System.Drawing.Bitmap(
        $t, $t, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

    $gr = [System.Drawing.Graphics]::FromImage($bmp)
    $gr.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $gr.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $gr.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $gr.Clear([System.Drawing.Color]::Transparent)
    $gr.DrawImage($carre, 0, 0, $t, $t)
    $gr.Dispose()

    $flux = New-Object System.IO.MemoryStream
    $bmp.Save($flux, [System.Drawing.Imaging.ImageFormat]::Png)
    $images += , @{ Taille = $t; Octets = $flux.ToArray() }
    $flux.Dispose()
    $bmp.Dispose()
}
$carre.Dispose()

# ----- Assemblage du conteneur ICO.
#
# En-tête, puis un répertoire de 16 octets par image, puis les images à la
# suite. Chacune est un PNG tel quel : Windows l'accepte depuis Vista, et c'est
# bien plus compact qu'un BMP 32 bits — 26 Ko contre 254 Ko pour l'ancien
# fichier, qui ne portait pourtant qu'une seule image.
$sortie = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($sortie)

$w.Write([UInt16]0)                  # réservé
$w.Write([UInt16]1)                  # 1 = icône (2 serait un curseur)
$w.Write([UInt16]$images.Count)

$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    # ⚠️ 256 s'écrit 0 : le champ ne fait qu'un octet, et 256 n'y tient pas.
    # Écrire 255 donnerait une icône que Windows rangerait à la mauvaise taille.
    $t = if ($img.Taille -ge 256) { 0 } else { $img.Taille }

    $w.Write([Byte]$t)               # largeur
    $w.Write([Byte]$t)               # hauteur
    $w.Write([Byte]0)                # palette : aucune
    $w.Write([Byte]0)                # réservé
    $w.Write([UInt16]1)              # plans
    $w.Write([UInt16]32)             # bits par pixel
    $w.Write([UInt32]$img.Octets.Length)
    $w.Write([UInt32]$offset)
    $offset += $img.Octets.Length
}
foreach ($img in $images) { $w.Write($img.Octets) }

$w.Flush()
New-Item -ItemType Directory -Force -Path (Split-Path $Destination) | Out-Null

# ⚠️ Le fichier peut être VERROUILLÉ : Visual Studio garde ouvert celui que
# désigne <ApplicationIcon>. L'échec doit alors DIRE ce qu'il faut faire — sans
# ce message, on lit « accès refusé » et on cherche du côté des droits.
try
{
    [System.IO.File]::WriteAllBytes($Destination, $sortie.ToArray())
}
catch [System.IO.IOException]
{
    throw "Impossible d'écrire $Destination : le fichier est ouvert ailleurs. "
        + "Visual Studio garde l'icône de l'application verrouillée — fermez la "
        + "solution, puis relancez ce script."
}
$w.Dispose()

$ko = [math]::Round((Get-Item $Destination).Length / 1KB, 1)
Write-Host "$Destination" -ForegroundColor Green
Write-Host "  $($images.Count) tailles ($($tailles -join ', ')) — $ko Ko"
Write-Host "  Recompilez pour que l'icône entre dans l'exécutable." -ForegroundColor DarkGray
