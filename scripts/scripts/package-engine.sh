#!/usr/bin/env bash
# Packages uxplay.exe plus every MSYS2/GStreamer runtime DLL it needs into a
# portable bundle. Runs inside the MSYS2 shell on a GitHub Actions runner.
set -euo pipefail

EXE="$1"          # path to built uxplay.exe
OUT="$2"          # output bundle directory
VERSION="$3"      # engine version string written to VERSION.txt

PREFIX="${MSYSTEM_PREFIX:-/ucrt64}"
PLUGINS="app audioconvert audioresample autodetect coreelements d3d11 d3d12 level libav playback videoconvertscale videoparsersbad volume wasapi2"

mkdir -p "$OUT/lib/gstreamer-1.0"
cp "$EXE" "$OUT/uxplay.exe"

for p in $PLUGINS; do
  f="$PREFIX/lib/gstreamer-1.0/libgst$p.dll"
  if [ -f "$f" ]; then
    cp "$f" "$OUT/lib/gstreamer-1.0/"
  else
    echo "::warning::GStreamer plugin not found: $f"
  fi
done

# Copy every MSYS2 DLL that uxplay.exe or any bundled plugin links against,
# resolving transitively (two passes cover deps of deps).
for pass in 1 2 3; do
  {
    ldd "$OUT/uxplay.exe" 2>/dev/null | awk '/=>/ {print $3}'
    for f in "$OUT"/lib/gstreamer-1.0/*.dll "$OUT"/*.dll; do
      [ -f "$f" ] && ldd "$f" 2>/dev/null | awk '/=>/ {print $3}'
    done
  } | grep -i "^$PREFIX" | sort -u | while read -r dep; do
    base="$(basename "$dep")"
    [ -f "$OUT/$base" ] || cp "$dep" "$OUT/"
  done
done

cat > "$OUT/VERSION.txt" <<VER
AirGlass engine bundle
UxPlay (upstream FDH2/UxPlay): $VERSION
Built by GitHub Actions on $(date -u +%Y-%m-%d) from unmodified source.
UxPlay is GPLv3: source at https://github.com/FDH2/UxPlay
VER
cp "$(dirname "$0")/../engine-licenses/"*.txt "$OUT/" 2>/dev/null || true

echo "Bundle contents:"
ls "$OUT" | head -50
echo "DLL count: $(ls "$OUT"/*.dll | wc -l), plugins: $(ls "$OUT"/lib/gstreamer-1.0/*.dll | wc -l)"
