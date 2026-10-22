# Third-party notices

AirGlass (this repository's C# code) is licensed under the PolyForm Noncommercial License 1.0.0 (see LICENSE).

## UxPlay (mirroring engine)

- Project: https://github.com/FDH2/UxPlay
- License: GNU GPLv3 (or, at the recipient's option, later versions)
- How AirGlass uses it: the `Build engine` GitHub Actions workflow in this
  repository compiles **unmodified** UxPlay source (pinned commit, see the
  workflow file) into `uxplay.exe` and packages it with the MSYS2/GStreamer
  runtime libraries it links against. The AirGlass app downloads that bundle
  from this repository's Releases on first run and launches `uxplay.exe` as a
  separate child process. No UxPlay code is compiled into AirGlass itself.
- Corresponding source: the workflow prints and pins the exact commit; full
  source is at the UxPlay repository above. This satisfies the GPLv3
  corresponding-source requirement for the distributed binary.

## MSYS2 / GStreamer runtime libraries

- Bundled DLLs come from the MSYS2 project (https://www.msys2.org/) packages:
  GStreamer (LGPL-2.1+), GLib (LGPL-2.1+), OpenSSL (Apache-2.0), libplist
  (LGPL-2.1+), FFmpeg/libavcodec via gst-libav (LGPL/GPL as built by MSYS2),
  and the mingw-w64 runtime. Their licenses and sources are available from
  https://packages.msys2.org/ and https://gstreamer.freedesktop.org/.
- The engine bundle ships a VERSION.txt identifying the exact upstream commit
  and build date.

## Inspiration / prior art

- leapbtw/uxplay-windows (https://github.com/leapbtw/uxplay-windows) showed
  that a fully bundled UxPlay runtime works well on Windows; AirGlass uses
  its published plugin list as a reference for what the engine bundle needs.
  AirGlass does not reuse its code.
