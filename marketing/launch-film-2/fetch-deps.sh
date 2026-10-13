#!/usr/bin/env bash
# Fetch what the film needs but does not version: three.js r170 (MIT) and the brand faces (SIL OFL) from npm,
# and the VSCO-2 Community Edition orchestral samples (CC0) used by the score.
set -euo pipefail
cd "$(dirname "$0")"
tmp=$(mktemp -d); mkdir -p assets/fonts lib/jsm
(cd "$tmp" && npm pack --silent three@0.170.0 @fontsource-variable/source-sans-3@5.3.1 @fontsource-variable/jetbrains-mono@5.3.0 >/dev/null && for f in *.tgz; do tar xzf "$f"; mv package "${f%.tgz}"; done)
cp "$tmp"/three-0.170.0/build/three.module.js lib/
for d in postprocessing shaders math utils; do cp -r "$tmp"/three-0.170.0/examples/jsm/$d lib/jsm/; done
cp "$tmp"/fontsource-variable-source-sans-3-*/files/source-sans-3-latin-wght-normal.woff2 assets/fonts/ss3.woff2
cp "$tmp"/fontsource-variable-source-sans-3-*/files/source-sans-3-latin-ext-wght-normal.woff2 assets/fonts/ss3-ext.woff2
cp "$tmp"/fontsource-variable-jetbrains-mono-*/files/jetbrains-mono-latin-wght-normal.woff2 assets/fonts/jbm.woff2
rm -rf "$tmp"
# samples: a sparse checkout of only the instruments the score plays (~2.3 GB); set VSCO to reuse a copy
VSCO_DIR=${VSCO:-vsco-2-ce}
if [ ! -d "$VSCO_DIR/.git" ]; then
  git clone --depth 1 --filter=blob:none --sparse https://github.com/sgossner/VSCO-2-CE "$VSCO_DIR"
  git -C "$VSCO_DIR" sparse-checkout set --no-cone \
    "/Strings/Violin Section/susVib/" "/Strings/Viola Section/susvib/" "/Strings/Cello Section/susvib/" "/Strings/Cello Section/spic/" \
    "/Strings/Solo Contrabass/SusVib/" "/Strings/Solo Contrabass/Pizz/" "/Strings/Harp/" "/Brass/F Horn/sus/" "/Brass/Tenor Trombone/sus/" \
    "/Brass/Trumpet/sus/" "/Brass/Trumpet/stac/" "/Brass/Tuba/sus/" "/Percussion/Glock/" "/Percussion/Marimba/" "/Percussion/Timpani/" \
    "/Keys/Upright Piano/" "/VSCO 1 Percussion/varMetal/Cymbals/" "/VSCO 1 Percussion/drums/bass/"
fi
echo "deps ready (samples in $VSCO_DIR)"
