<#
    Prépare le logo d'Ex-libris pour en faire des icônes.

        .\faire-logo.ps1

    Part de logo-ExLibris.png — le visuel fourni — et produit la source unique
    des icônes des deux applications :

        installateur\logo-exlibris.png   (fond transparent, recadré, centré)

    ─────────────────────────────────────────────────────────────────────────
    CE QUE CE SCRIPT FAIT, ET POURQUOI

    Le fichier d'origine est en RGB, donc SANS transparence : son fond blanc
    est de la peinture, pas du vide. Posé tel quel en icône, il donnerait un
    carré blanc dans la barre des tâches et une tuile blanche sur le téléphone,
    quel que soit le fond derrière.

    Une version antérieure de ce script DESSINAIT le logo — un E de Georgia et
    une feuille en courbes de Bézier. Le visuel fourni est meilleur : la lettre
    y est un vrai caractère à empattements et la feuille s'inscrit dans la
    lettre au lieu de se poser à côté. Le dessin a donc été remplacé par cette
    préparation, qui n'invente rien.
#>

[CmdletBinding()]
param(
    [string]$Source      = (Join-Path $PSScriptRoot '..\logo-ExLibris.png'),
    [string]$Destination = (Join-Path $PSScriptRoot 'logo-exlibris.png'),
    [int]$Cote = 1024,

    # Quelle part du carré la marque occupe.
    #
    # 0,86 pour un logo ou une icône Windows, qui s'affichent tels quels.
    # 0,58 pour l'avant-plan d'une icône Android : son masque rogne les bords —
    # rond sur certains téléphones, en goutte sur d'autres — et une marque qui
    # remplit le carré s'y retrouve amputée de ses angles.
    [double]$Remplissage = 0.86
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# ⚠️ Résolus depuis le dossier courant de POWERSHELL, dit explicitement.
# `Set-Location` ne déplace pas le répertoire courant de .NET, dont se sert la
# surcharge à un seul argument de GetFullPath : un chemin relatif écrivait
# ailleurs, en silence, avec un message de réussite. Voir faire-icone.ps1.
$Source      = [System.IO.Path]::GetFullPath($Source, $PWD.Path)
$Destination = [System.IO.Path]::GetFullPath($Destination, $PWD.Path)

if (-not (Test-Path $Source)) { throw "Visuel introuvable : $Source" }

$origine = [System.Drawing.Image]::FromFile($Source)
$l = $origine.Width; $h = $origine.Height

$travail = New-Object System.Drawing.Bitmap($l, $h,
    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

$gt = [System.Drawing.Graphics]::FromImage($travail)
$gt.DrawImage($origine, 0, 0, $l, $h)
$gt.Dispose()
$origine.Dispose()

# ─────────────────────────────────────────────────────────────────────────
#  1. Rendre le fond transparent — par PROPAGATION depuis les bords.
#
#  ⚠️ Et non « tout ce qui est blanc devient transparent ».
#
#  La nervure de la feuille est blanche elle aussi, et le blanc du fond n'a
#  rien à voir avec elle : une règle sur la seule couleur la percerait, et sur
#  une barre des tâches sombre on verrait le noir au travers d'une feuille
#  censée être dessinée. On part donc des bords et on n'efface que ce qui
#  communique avec eux.
#
#  Les contrepoinçons du E — les blancs entre ses barres — s'ouvrent vers la
#  droite : ils communiquent avec l'extérieur, et deviennent donc transparents.
#  C'est ce qu'on veut : ils doivent laisser voir le fond.
# ─────────────────────────────────────────────────────────────────────────

# Deux seuils, et c'est la correction d'un défaut visible.
#
# Un seul seuil proportionnel laissait un VOILE : le fond du visuel d'origine
# n'est pas d'un blanc parfait, et un pixel à trois unités du blanc gardait une
# opacité de 12 — invisible sur fond clair, nettement perceptible en rectangle
# plus pâle sur une barre des tâches sombre.
#
#   • en deçà de « SeuilFond », le pixel est du fond pur          → alpha 0
#   • entre les deux, c'est du crénelage                          → alpha dosé
#   • au-delà de « SeuilObjet », c'est la lettre ou la feuille    → intact
#
# Trancher net à la place du dosage laisserait un liseré blanc autour de la
# lettre, très visible sur fond sombre : le crénelage doit rester progressif.
$SeuilFond  = 16
$SeuilObjet = 70

# ⚠️ LA BOUCLE DE PIXELS EST EN C#, PAS EN POWERSHELL.
#
# Ce n'est pas de la coquetterie, c'est ce qui rend le script utilisable. Deux
# versions antérieures ont été mesurées sur ce même logo de 1254 × 1254 :
#
#     GetPixel / SetPixel, un pixel à la fois ......... 283 s
#     LockBits, mais boucle PowerShell sur le tableau . 317 s   (PIRE)
#     la même chose en C# compilé à la volée .......... voir la fin du script
#
# La deuxième tentative avait été faite en croyant que GetPixel était le goulet.
# Il ne l'était pas : c'est l'INTERPRÉTEUR PowerShell qui coûte, chaque accès à
# un élément de tableau passant par sa machinerie de types. Déplacer les données
# sans déplacer la boucle n'a donc rien gagné — et en a même coûté.
#
# À cinq minutes par fichier, personne ne rejoue le script quand le logo change ;
# à moins d'une seconde, on le rejoue sans y penser. C'est la différence entre un
# outil et une relique.
Add-Type -TypeDefinition @'
using System;

public static class Detourage
{
    /// <summary>
    /// Rend transparent le fond qui COMMUNIQUE AVEC LES BORDS, et lui seul.
    ///
    /// La distinction est essentielle : la nervure de la feuille est blanche
    /// elle aussi. Une règle sur la seule couleur la percerait, et sur une barre
    /// des tâches sombre on verrait le noir au travers d'une feuille censée être
    /// dessinée. On part donc du pourtour et on ne propage que dans ce qui est
    /// assez clair pour être du fond.
    ///
    /// Les octets sont en B, G, R, A — l'ordre de Format32bppArgb en mémoire.
    /// </summary>
    public static void Detourer(byte[] o, int largeur, int hauteur, int pas,
                                int seuilFond, int seuilObjet)
    {
        bool[] estFond = new bool[largeur * hauteur];

        // Une pile explicite plutôt que la récursivité : sur 1,5 million de
        // pixels, un parcours récursif épuiserait la pile d'appels.
        //
        // ⚠️ LE MARQUAGE SE FAIT À L'EMPILEMENT, PAS AU DÉPILEMENT.
        //
        // C'est ce qui BORNE la pile. En marquant au dépilement, un même pixel
        // est empilé par chacun de ses quatre voisins avant d'être traité une
        // seule fois : la pile déborde alors le tableau dimensionné à un pixel
        // près — « Index was outside the bounds of the array », constaté ici même
        // le 30/08/2026. En marquant à l'empilement, chaque pixel n'y entre
        // qu'une fois, la borne largeur × hauteur est garantie, et on épargne
        // au passage trois visites sur quatre.
        int[] pile = new int[largeur * hauteur];
        int sommet = 0;

        // Amorce sur tout le pourtour.
        for (int x = 0; x < largeur; x++)
        {
            Empiler(o, estFond, pile, ref sommet, x,                           largeur, pas, seuilObjet);
            Empiler(o, estFond, pile, ref sommet, (hauteur - 1) * largeur + x, largeur, pas, seuilObjet);
        }
        for (int y = 0; y < hauteur; y++)
        {
            Empiler(o, estFond, pile, ref sommet, y * largeur,                 largeur, pas, seuilObjet);
            Empiler(o, estFond, pile, ref sommet, y * largeur + largeur - 1,   largeur, pas, seuilObjet);
        }

        while (sommet > 0)
        {
            int i = pile[--sommet];
            int x = i % largeur;
            int y = i / largeur;

            if (x > 0)           Empiler(o, estFond, pile, ref sommet, i - 1,       largeur, pas, seuilObjet);
            if (x < largeur - 1) Empiler(o, estFond, pile, ref sommet, i + 1,       largeur, pas, seuilObjet);
            if (y > 0)           Empiler(o, estFond, pile, ref sommet, i - largeur, largeur, pas, seuilObjet);
            if (y < hauteur - 1) Empiler(o, estFond, pile, ref sommet, i + largeur, largeur, pas, seuilObjet);
        }

        // Deux seuils, et c'est la correction d'un défaut visible. Un seul seuil
        // proportionnel laissait un VOILE : le fond du visuel n'est pas d'un
        // blanc parfait, et un pixel à trois unités du blanc gardait une opacité
        // de 12 — invisible sur fond clair, nettement perceptible en rectangle
        // plus pâle sur une barre des tâches sombre.
        //
        // Trancher net à la place du dosage laisserait un liseré blanc autour de
        // la lettre : le crénelage doit rester progressif.
        for (int y = 0; y < hauteur; y++)
        {
            int ligne = y * pas;
            for (int x = 0; x < largeur; x++)
            {
                if (!estFond[y * largeur + x]) continue;

                int i = ligne + x * 4;
                int d = Distance(o, i);

                o[i + 3] = d <= seuilFond  ? (byte)0
                         : d >= seuilObjet ? (byte)255
                         : (byte)(255 * (d - seuilFond) / (seuilObjet - seuilFond));
            }
        }
    }

    /// <summary>
    /// Retient un pixel comme fond, s'il n'y est pas déjà et s'il est assez
    /// clair pour en être. Le test de couleur est fait ICI, à l'entrée : c'est
    /// ce qui empêche la propagation de traverser la lettre.
    /// </summary>
    private static void Empiler(byte[] o, bool[] estFond, int[] pile, ref int sommet,
                                int i, int largeur, int pas, int seuilObjet)
    {
        if (estFond[i]) return;

        int x = i % largeur;
        int y = i / largeur;
        if (Distance(o, y * pas + x * 4) >= seuilObjet) return;

        estFond[i] = true;
        pile[sommet++] = i;
    }

    /// <summary>Distance au blanc : 0 pour du blanc pur, 255 pour du noir.</summary>
    private static int Distance(byte[] o, int i)
    {
        int b = o[i], v = o[i + 1], r = o[i + 2];
        int min = r < v ? r : v;
        if (b < min) min = b;
        return 255 - min;
    }
}
'@

$chrono = [System.Diagnostics.Stopwatch]::StartNew()

$zone = New-Object System.Drawing.Rectangle(0, 0, $l, $h)
$donnees = $travail.LockBits($zone,
    [System.Drawing.Imaging.ImageLockMode]::ReadWrite,
    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

$octets = New-Object 'byte[]' ($donnees.Stride * $h)
[System.Runtime.InteropServices.Marshal]::Copy($donnees.Scan0, $octets, 0, $octets.Length)

[Detourage]::Detourer($octets, $l, $h, $donnees.Stride, $SeuilFond, $SeuilObjet)

[System.Runtime.InteropServices.Marshal]::Copy($octets, 0, $donnees.Scan0, $octets.Length)
$travail.UnlockBits($donnees)

# ─────────────────────────────────────────────────────────────────────────
#  2. Recadrer sur le contenu, et centrer.
#
#  Le visuel fourni porte de larges marges blanches. Gardées, elles feraient
#  une icône minuscule au milieu d'un carré vide — à 32 px dans une barre des
#  tâches, la marque deviendrait illisible.
# ─────────────────────────────────────────────────────────────────────────
$gauche = $l; $droite = 0; $haut = $h; $bas = 0

for ($y = 0; $y -lt $h; $y++) {
    $ligne = $y * $donnees.Stride
    for ($x = 0; $x -lt $l; $x++) {
        if ($octets[$ligne + $x * 4 + 3] -gt 16) {
            if ($x -lt $gauche) { $gauche = $x }
            if ($x -gt $droite) { $droite = $x }
            if ($y -lt $haut)   { $haut = $y }
            if ($y -gt $bas)    { $bas = $y }
        }
    }
}

$largeur = $droite - $gauche + 1
$hauteur = $bas - $haut + 1

$disponible = $Cote * $Remplissage
$facteur = [Math]::Min($disponible / $largeur, $disponible / $hauteur)

$final = New-Object System.Drawing.Bitmap($Cote, $Cote,
    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

$gf = [System.Drawing.Graphics]::FromImage($final)
$gf.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$gf.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$gf.Clear([System.Drawing.Color]::Transparent)

$gf.DrawImage($travail,
    (New-Object System.Drawing.RectangleF(
        [float](($Cote - $largeur * $facteur) / 2),
        [float](($Cote - $hauteur * $facteur) / 2),
        [float]($largeur * $facteur), [float]($hauteur * $facteur))),
    (New-Object System.Drawing.RectangleF($gauche, $haut, $largeur, $hauteur)),
    [System.Drawing.GraphicsUnit]::Pixel)

$gf.Dispose()
$travail.Dispose()

New-Item -ItemType Directory -Force -Path (Split-Path $Destination) | Out-Null
$final.Save($Destination, [System.Drawing.Imaging.ImageFormat]::Png)
$final.Dispose()

$ko = [math]::Round((Get-Item $Destination).Length / 1KB, 1)
Write-Host "$Destination" -ForegroundColor Green
Write-Host "  $Cote x $Cote — remplissage $Remplissage — $ko Ko — $([math]::Round($chrono.Elapsed.TotalSeconds, 2)) s"
