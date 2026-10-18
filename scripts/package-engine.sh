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

collect_deps() {
  # Print absolute paths of DLLs a binary links against (MSYS2 ldd output).
  ldd "$1" 2>/dev/null | awk '/=>/ && $3 ~ /^\// {print $3}' || true
}

# Copy every MSYS2 DLL that uxplay.exe or any bundled plugin links against,
# resolving transitively (three passes cover deps of deps).
for pass in 1 2 3; do
  {
    collect_deps "$OUT/uxplay.exe"
    for f in "$OUT"/lib/gstreamer-1.0/*.dll "$OUT"/*.dll; do
      [ -f "$f" ] || continue
      collect_deps "$f"
    done
    true   # keep the group status clean even if the last glob matched nothing
  } | { grep -i "^$PREFIX" || true; } | sort -u | while read -r dep; do
    base="$(basename "$dep")"
    if [ ! -f "$OUT/$base" ]; then
      cp "$dep" "$OUT/" || echo "::warning::could not copy $dep"
    fi
  done
done

# Sanity: the core runtime libraries must have been collected, or ldd parsing
# broke and the bundle would be useless.
for core in libgstreamer-1.0-0.dll libglib-2.0-0.dll libcrypto-3-x64.dll libplist-2.0.dll; do
  [ -f "$OUT/$core" ] || { echo "::error::core runtime DLL missing from bundle: $core"; exit 1; }
done

cat > "$OUT/VERSION.txt" <<VER
AirGlass engine bundle
UxPlay (upstream FDH2/UxPlay): $VERSION
Built by GitHub Actions on $(date -u +%Y-%m-%d) from unmodified source.
UxPlay is GPLv3: source at https://github.com/FDH2/UxPlay
VER

echo "Bundle contents:"
ls "$OUT" | head -50
echo "DLL count: $(ls "$OUT"/*.dll | wc -l), plugins: $(ls "$OUT"/lib/gstreamer-1.0/*.dll | wc -l)"
