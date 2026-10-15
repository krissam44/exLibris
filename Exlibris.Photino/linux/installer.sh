#!/usr/bin/env bash
# Installe Ex-libris pour l'utilisateur courant, sans sudo :
#   application → ~/.local/opt/exlibris
#   lanceur     → ~/.local/share/applications/exlibris.desktop (menu, recherche)
#   icône       → ~/.local/share/icons/hicolor/256x256/apps/exlibris.png
#
# À lancer depuis le dossier publié, SANS chmod préalable :
#   bash installer.sh
# (« bash script » n'a pas besoin du droit d'exécution, perdu à la copie depuis Windows.)
#
# ⚠️ LA BIBLIOTHÈQUE N'EST PAS ICI. Elle vit dans ~/.local/share/Exlibris (la
# base exlibris.db et le dossier couvertures/), que ce script ne touche jamais :
# c'est pour cela que l'application s'installe dans un AUTRE dossier. Une
# réinstallation ou une désinstallation laisse la bibliothèque intacte.
set -euo pipefail

SOURCE="$(cd "$(dirname "$0")" && pwd)"
DONNEES="${XDG_DATA_HOME:-$HOME/.local/share}"
CIBLE="$HOME/.local/opt/exlibris"
LANCEUR="$DONNEES/applications/exlibris.desktop"
ICONE="$DONNEES/icons/hicolor/256x256/apps/exlibris.png"

if [[ $EUID -eq 0 ]]; then
  echo "Ne pas lancer en root : l'installation est propre à l'utilisateur, et une"
  echo "application graphique lancée en root n'a pas accès à la session."
  exit 1
fi

if [[ ! -f "$SOURCE/Exlibris" || ! -f "$SOURCE/PhotinoX.Native.so" ]]; then
  echo "Ce script doit rester dans le dossier publié (Exlibris introuvable à côté)."
  exit 1
fi

# --- Prérequis système -------------------------------------------------------
# PhotinoX.Native 5.3 exige glibc >= 2.34 et WebKitGTK 4.1 (Ubuntu 22.04+,
# Debian 12+, Zorin OS 17+) ; Photino.Native 4.0.x, avant lui, voulait 2.38.
# Vérifié AVANT toute copie : un échec ici ne laisse rien derrière.
if ldd "$SOURCE/PhotinoX.Native.so" 2>&1 | grep -q "version \`GLIBC"; then
  echo "Système trop ancien pour Ex-libris : $(ldd --version | head -1)"
  echo "Il faut glibc >= 2.34 (Ubuntu 22.04+, Debian 12+, Zorin OS 17+)."
  exit 1
fi

manquantes=$(ldd "$SOURCE/PhotinoX.Native.so" | awk '/=> not found/ {print $1}')
if [[ -n "$manquantes" ]]; then
  echo "Bibliothèques manquantes :"
  echo "$manquantes" | sed 's/^/  /'
  paquets=()
  echo "$manquantes" | grep -q webkit2gtk && paquets+=(libwebkit2gtk-4.1-0)
  echo "$manquantes" | grep -q libnotify  && paquets+=(libnotify4)
  echo "$manquantes" | grep -qE 'gtk-3|gdk-3' && paquets+=(libgtk-3-0t64)
  ((${#paquets[@]})) && echo "À installer :  sudo apt install -y ${paquets[*]}"
  echo "(libgtk-3-0 au lieu de libgtk-3-0t64 sur une base antérieure à Ubuntu 24.04)"
  exit 1
fi

# --- Copie de l'application --------------------------------------------------
# Les réglages LOCAUX (clé Syncfusion, adresse de Lirevia) diffèrent d'un poste
# à l'autre : un appsettings.Local.json déjà présent est CONSERVÉ, comme le fait
# l'installateur Windows (onlyifdoesntexist). Une mise à jour qui l'écraserait
# effacerait un réglage que personne n'a noté ailleurs.
reglages=""
if [[ -f "$CIBLE/appsettings.Local.json" ]]; then
  reglages="$(mktemp)"
  cp "$CIBLE/appsettings.Local.json" "$reglages"
fi

rm -rf "$CIBLE"
mkdir -p "$CIBLE"
tar -C "$SOURCE" -cf - . | tar -C "$CIBLE" -xf -

if [[ -n "$reglages" ]]; then
  cp "$reglages" "$CIBLE/appsettings.Local.json"
  rm -f "$reglages"
fi

chmod +x "$CIBLE/Exlibris" "$CIBLE/installer.sh" "$CIBLE/desinstaller.sh"

# --- Lanceur et icône --------------------------------------------------------
mkdir -p "$(dirname "$LANCEUR")" "$(dirname "$ICONE")"
cp "$SOURCE/Assets/exlibris-256.png" "$ICONE"
sed "s|@DOSSIER@|$CIBLE|g" "$SOURCE/exlibris.desktop" > "$LANCEUR"
chmod +x "$LANCEUR"

command -v update-desktop-database >/dev/null && update-desktop-database -q "$(dirname "$LANCEUR")" || true
command -v gtk-update-icon-cache  >/dev/null && gtk-update-icon-cache -q -t "$DONNEES/icons/hicolor" || true

echo "Ex-libris installé dans $CIBLE"
echo "Il apparaît dans le menu des applications sous « Ex-libris »."
echo "Votre bibliothèque reste dans $DONNEES/Exlibris (à sauvegarder : exlibris.db ET couvertures/)."
echo "Désinstallation : bash \"$CIBLE/desinstaller.sh\""
