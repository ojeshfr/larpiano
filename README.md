# larpiano

Windows MIDI-to-keyboard piano player for Roblox pianos — *from da people who LARP, for da people who LARP*.

**larpiano** plays MIDI files on virtual pianos in Roblox by dispatching physical Windows keyboard scan codes. It includes an authentic dark desktop UI, integrated non-activating floating overlay, song library, online MIDI discovery, audio-to-MIDI extraction, and customizable mapping profiles.

Website: https://larpiano.portways.app/  
GitHub Releases: https://github.com/ojeshfrrr/larpiano/releases

---

## Public Beta: v1.1.0-beta.1

### Key Highlights
- **Reconstructed Dark Desktop Interface:** Obsidian theme with editorial typography, lavender accents, 14px DWM-rounded corners, and grand piano keys backdrop.
- **Authentic Brand Restoration:** Genuine larpiano logo and verbatim community taglines (*"from da people who LARP, for da people who LARP"* and *"for da people who larp · for da poeple who believe in larpocracy"*).
- **Non-Activating Floating Overlay:** Floats above Roblox without stealing focus (`WS_EX_NOACTIVATE`). Includes both an Expanded view (note roll visualizer, track metadata, quick action cards) and a Compact pill view (slim transport bar).
- **Comprehensive Quality Assurance:** 210 passing automated tests covering core playback, scheduling, overlay state sync, non-activating window interaction, and STA screenshot capture.

---

## Download

Download the self-contained Windows x64 ZIP from [GitHub Releases](https://github.com/ojeshfrrr/larpiano/releases/tag/v1.1.0-beta.1):

1. Download `larpiano-v1.1.0-beta.1-windows-x64.zip`.
2. Extract the archive to any folder.
3. Run `Keynote.App.exe`. No separate .NET runtime installation is required.

---

## Quick Start

1. Start Roblox and sit at your piano in the game.
2. In larpiano, select your Roblox game window on the **Player** screen.
3. Pick a song from **My Library**, search via **Discover**, or click **Browse MIDI** to import a local file.
4. Verify or adjust the mapping under the **Mapping** tab for your specific piano layout.
5. Focus your Roblox window and press **Ctrl + Alt + F8** to start playback.

---

## Shortcuts & Controls

| Shortcut | Action | Description |
|---|---|---|
| **Ctrl + Alt + F8** | Play / Pause / Resume | Begins playback when the target Roblox window is active, or toggles pause/resume |
| **Ctrl + Alt + F9** | Emergency Stop | Instantly halts playback and releases all currently pressed keys |
| **Overlay Toggle** | Show / Hide Overlay | Toggles the non-activating floating overlay on screen |

Shortcuts and modifier keys can be remapped under the **Settings** tab.

---

## Building from Source

Requirements: .NET 10 SDK (Windows x64).

```powershell
# Restore, build and run tests
dotnet restore Keynote.sln
dotnet build Keynote.sln -c Release
dotnet test Keynote.sln -c Release

# Run the application
dotnet run --project Keynote.App/Keynote.App.csproj -c Release

# Publish a self-contained single directory distribution
dotnet publish Keynote.App/Keynote.App.csproj -c Release -r win-x64 --self-contained true -o publish
```

---

## Current Limitations & Notes

- **Verified Mapping Required:** Virtual pianos across Roblox games feature differing keyboard assignments (e.g. 61-key vs. 88-key layouts). Manually verify your piano layout in the Mapping tab before playing.
- **Input Delivery:** Windows `SendInput` delivers physical scan codes to the foreground window. Playback automatically pauses if Roblox loses foreground focus.
- **Opennan Transcription:** Online audio-to-MIDI transcription relies on the Opennan service and requires active network connectivity.
