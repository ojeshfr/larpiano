# larpiano v1.0.35 — Release Notes

Welcome to **larpiano v1.0.35** — *from da people who LARP, for da people who LARP*.

This milestone introduces a complete visual reconstruction of the desktop application, full authentic brand restoration, and a native non-activating floating overlay designed specifically for distraction-free piano playback in-game.

---

## What's New in v1.0.29

### 1. Reconstructed Cinematic Desktop Interface
- **Modern Dark Music Aesthetic:** Deep obsidian canvases (`#0D0D14` / `#12121A`) with soft lavender and purple accents (`#A78BFA` / `#C4B5FD`).
- **Editorial Typography:** Editorial serif headings paired with clean, highly readable interface typography.
- **Hero Keys & Waveform Glow:** High-resolution grand piano keys hero backdrop with radial fog fading and neon lavender ambient glow curves.
- **14px Window Corner Rounding:** Native Windows Desktop Window Manager (DWM) rounded corners (`DWMWA_WINDOW_CORNER_PREFERENCE = 33`, `DWMWCP_ROUND = 2`) with WPF visual border clipping and dynamic square corner handling when maximized.
- **Persistent Bottom Player Bar:** Complete player transport controls, volume slider, track metadata, and quick toggle for the floating overlay.

### 2. Authentic Brand Restoration & Verbatim Taglines
- **Original Logo & Wordmark:** The authentic 256×256 purple icon mark and `larpiano / piano player` header descriptor are restored in high fidelity.
- **Community Taglines Preserved Verbatim:** Both original taglines are featured prominently without spelling or grammatical modification:
  - `"from da people who LARP, for da people who LARP"`
  - `"for da people who larp  ·  for da poeple who believe in larpocracy"`

### 3. Integrated Floating Overlay (Expanded & Compact Modes)
- **Non-Activating Interaction (`WS_EX_NOACTIVATE`):** Click and drag controls without stealing foreground focus from the active virtual piano window.
- **Always-on-Top:** Floats effortlessly above game windows with customizable glass opacity (~97%) and illuminated purple glow borders.
- **Dual Visual Modes:**
  - **Expanded Mode:** Full note roll visualizer with pitch labels (C2–C6), timeline timestamps, song metadata, mapping status, and quick action cards.
  - **Compact Pill Mode:** Ultra-slim transport bar featuring play/pause, stop, playback speed selector, volume, and progress scrubber.
- **Bi-Directional State Synchronization:** Changes in the main application instantly reflect in the overlay and vice versa.

### 4. Extended Mapping & Engine Enhancements
- Added extended pre-configured mappings for popular piano arrangements.
- Enhanced playback cancellation safety and immediate key release guards.
- Expanded online discovery and library caching.

### 5. Automated Quality Assurance & Verification
- **222 Automated Tests Passing:**
  - 182 core unit and integration tests across MIDI importing, scheduling, note mapping, and catalog providers.
  - 40 overlay and UI automation tests, including non-activating window hit tests and STA snapshot captures across all 9 application views.

---

## System Requirements

- **Operating System:** Windows 10 (version 1809 or higher) or Windows 11 (recommended for native DWM rounded corners).
- **Architecture:** x64 (64-bit).
- **Runtime:** Completely self-contained. No external .NET runtime or SDK installation required.

---

## Installation & Quick Start

1. Download `larpiano-windows-x64.zip` from GitHub Releases.
2. Extract all files to a local folder of your choice (e.g. `C:\larpiano` or `D:\tools\larpiano`).
3. Run `Keynote.App.exe`.
4. Open your game and take a seat at your target piano.
5. In larpiano, select your game window from the dropdown.
6. Choose a song from **My Library**, **Discover**, or **Browse MIDI**.
7. Confirm or verify the mapping profile for your piano under the **Mapping** tab.
8. Focus your game window and press **Ctrl + Alt + F8** to start playback.

---

## Default Shortcuts

| Shortcut | Function | Description |
|---|---|---|
| **Ctrl + Alt + F8** | Play / Pause / Resume | Starts playback when the game window is focused, or toggles pause/resume |
| **Ctrl + Alt + F9** | Emergency Stop | Immediately stops playback and releases all held piano keys |
| **Overlay Toggle** | Show / Hide Overlay | Toggle floating overlay via the bottom bar icon or main screen button |

Shortcuts can be customized in the **Settings** tab.

---

## Known Limitations

- **Piano Mapping Verification:** Because virtual piano games utilize differing key mappings (e.g. 61-key vs 88-key layouts), users must verify key assignments before playing complex arrangements.
- **Audio Capture / Synthetic Input:** Native Windows scan codes are dispatched via `SendInput`. Anticheat systems or elevated game instances may require running larpiano as Administrator if input is not received.
- **Opennan Extraction:** Audio-to-MIDI transcription requires an active internet connection and is subject to provider rate limits.
