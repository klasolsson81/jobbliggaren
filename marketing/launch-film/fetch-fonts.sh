#!/usr/bin/env bash
# Fetch the two brand faces (SIL OFL) from npm into assets/fonts/.
set -euo pipefail
cd "$(dirname "$0")"
tmp=$(mktemp -d); mkdir -p assets/fonts
(cd "$tmp" && npm pack --silent @fontsource-variable/source-sans-3@5.3.1 @fontsource-variable/jetbrains-mono@5.3.0 >/dev/null && for f in *.tgz; do tar xzf "$f"; mv package "${f%.tgz}"; done)
cp "$tmp"/fontsource-variable-source-sans-3-*/files/source-sans-3-latin-wght-normal.woff2 assets/fonts/ss3.woff2
cp "$tmp"/fontsource-variable-source-sans-3-*/files/source-sans-3-latin-ext-wght-normal.woff2 assets/fonts/ss3-ext.woff2
cp "$tmp"/fontsource-variable-jetbrains-mono-*/files/jetbrains-mono-latin-wght-normal.woff2 assets/fonts/jbm.woff2
rm -rf "$tmp"; ls -la assets/fonts
