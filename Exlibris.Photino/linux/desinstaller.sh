#!/usr/bin/env bash
# Retire Ex-libris (application, lanceur, icône).
#
# ⚠️ La bibliothèque N'EST PAS supprimée : ~/.local/share/Exlibris (exlibris.db
# et couvertures/) reste en place, comme sous Windows. Une désinstallation faite
# pour réinstaller proprement ne doit pas effacer deux mille fiches sans retour ;
# qui veut vraiment tout effacer sait supprimer ce dossier.
set -euo pipefail

DONNEES="${XDG_DATA_HOME:-$HOME/.local/share}"
CIBLE="$HOME/.local/opt/exlibris"

if pgrep -x Exlibris >/dev/null; then
  echo "Ex-libris est encore ouvert : fermez-le avant de le désinstaller."
  exit 1
fi

rm -f "$DONNEES/applications/exlibris.desktop"
rm -f "$DONNEES/icons/hicolor/256x256/apps/exlibris.png"
rm -rf "$CIBLE"

command -v update-desktop-database >/dev/null && update-desktop-database -q "$DONNEES/applications" || true

echo "Ex-libris est désinstallé."
echo "Votre bibliothèque est conservée dans $DONNEES/Exlibris."
