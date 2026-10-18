<p align="center">
  <img src="assets/banner.svg" alt="AirGlass banner" width="100%"/>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11-0a0e1a?style=flat-square&logo=windows&logoColor=4f8dff" alt="Windows 10/11"/>
  <img src="https://img.shields.io/badge/.NET-8-0a0e1a?style=flat-square&logo=dotnet&logoColor=4f8dff" alt=".NET 8"/>
  <img src="https://img.shields.io/badge/license-MIT-0a0e1a?style=flat-square" alt="MIT"/>
  <img src="https://img.shields.io/badge/engine-GPLv3-0a0e1a?style=flat-square" alt="Engine GPLv3"/>
</p>

<p align="center"><a href="https://mjanthony819.github.io/airglass/"><b>Download page</b></a> &middot; <a href="../../releases/latest"><b>Latest release</b></a></p>

# AirGlass — iPhone mirroring for Windows

View your iPhone or iPad screen on a Windows 10/11 PC over AirPlay. Free and
open source (app: MIT, engine: GPLv3).

AirGlass is a Windows control panel around [UxPlay](https://github.com/FDH2/UxPlay),
the open-source AirPlay receiver. AirGlass builds the engine from unmodified
upstream source in its own CI pipeline (see below), downloads it on first run,
and gives you a simple window to start/stop mirroring, pick the AirPlay name,
resolution and latency mode, watch the connection status, and read the engine
log.

> **Status: early release.** The app and installer are built and unit-tested
> (16/16 tests) and the engine bundle passes a CI smoke test on a Windows
> runner, but the full flow has **not yet been tested with a real iPhone on
> real Windows hardware** — see "Honest limitations" before reporting issues.

## Install

1. Download
   [`AirGlass-Setup-1.0.0.exe`](https://github.com/mjanthony819/airglass/releases/download/v1.0.0/AirGlass-Setup-1.0.0.exe)
   (from the [latest release](../../releases/latest)) and run it.
2. Windows SmartScreen may warn because the installer is not
   code-signed; click **More info → Run anyway**.
3. Start AirGlass and click **Download engine** (one-time, ~80 MB). The app
   verifies the download's SHA-256 against the checksum published in the
   `engine-latest` release before installing it.
4. The first time the engine runs, allow it through Windows Firewall on your
   private network when prompted.

## Use

1. Make sure the iPhone and the PC are on the **same Wi-Fi network**.
2. Click **Start mirroring**. The status changes to *Waiting for iPhone*.
3. On the iPhone: **Control Centre → Screen Mirroring → choose your PC's
   AirPlay name** (default "AirGlass").
4. To stop: click **Stop**, or right-click the tray icon → **Exit**.

Settings (saved between runs): AirPlay name, resolution (720p/1080p/1440p),
Smooth vs Low-latency mode, optional 4-digit PIN, base port, and
"Start AirGlass with Windows".

## How it works

```
iPhone  --AirPlay (mDNS discovery + H.264/AAC over TCP/UDP)-->  uxplay.exe (engine)
                                                                   ^  launched, configured
                                                                   |  and watched by
                                                              AirGlass.exe (this app, C# WinForms)
```

- **Discovery**: the engine announces `_airplay._tcp` over mDNS using UxPlay's
  built-in mDNS responder — no Apple Bonjour install needed.
- **Streaming**: the iPhone encodes its screen as H.264 video + AAC audio and
  sends it to the engine, which decodes with GStreamer and shows it in a
  window.
- **View-only**: iOS does not allow third-party software to inject touch or
  input, so control from the PC is not possible without extra hardware. See
  the manual for the full explanation.

## Repository layout

- `src/AirGlass` — the WinForms app (.NET 8).
- `src/AirGlass.Core` — testable logic: argument building, settings, engine
  process management, engine download/verify, status parsing.
- `tests/AirGlass.Core.Tests` — xUnit tests (run on any OS).
- `installer/AirGlass.nsi` — NSIS script for the per-user installer (no admin
  needed).
- `.github/workflows/build-engine.yml` + `scripts/package-engine.sh` — builds
  `uxplay.exe` from pinned, unmodified UxPlay source on a Windows runner,
  bundles every DLL it needs, smoke-tests that the server actually listens,
  and publishes the `engine-latest` prerelease that the app downloads.

## Build from source

Prerequisites: .NET 8 SDK; NSIS 3 for the installer.

```bash
dotnet test                                  # unit tests (Linux/macOS/Windows)
dotnet publish src/AirGlass -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -o publish
makensis -DPUBLISH_DIR=publish -DVERSION=1.0.0 installer/AirGlass.nsi
```

The engine is built by the **Build engine** workflow (Actions tab → Run
workflow). It pins `FDH2/UxPlay@4e95a9e` (master, internal-mDNS build, no
Bonjour dependency).

## Honest limitations

- **Untested end-to-end on real hardware** as of 19 Oct 2026: the engine
  passes a CI smoke test (starts and listens on a real Windows runner) and
  the app is unit-tested, but no physical iPhone↔Windows session has been run
  yet. Treat first use as a field test; the log panel plus "Copy log" exists
  for exactly that.
- **View-only** — no touch/keyboard control of the iPhone (Apple blocks input
  injection on non-jailbroken iOS; this is a platform rule, not an app bug).
- **DRM apps** (Netflix and similar) show a black window — an AirPlay rule on
  the iPhone side, same for every receiver.
- Expect roughly **100–300 ms of latency** and up to 1080p; quality depends on
  Wi-Fi.
- The installer is **not code-signed**, so SmartScreen warns on first run.
- If the bundled engine misbehaves on your machine, a known-good alternative
  engine is [leapbtw/uxplay-windows](https://github.com/leapbtw/uxplay-windows)
  (its own tray app; not required by AirGlass).

## Licenses

AirGlass (C# code): MIT. Engine (UxPlay): GPLv3 — it is built from unmodified
source and downloaded separately; see [NOTICE.md](NOTICE.md) for the full
attribution and license breakdown.
