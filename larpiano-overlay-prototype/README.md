# Larpiano Floating Overlay Prototype (Demo)

This is an **isolated visual and interaction prototype** for the Larpiano piano-player companion overlay. It is built strictly as an independent WPF application targeting `.NET 8.0-windows` and does not interact with or modify the existing Larpiano / Keynote application or Game game processes.

---

## 1. Safety & Isolation Guarantee

* **Zero modification to existing application:** All existing project files, solutions (`Keynote.sln`), source code (`Keynote.App`, `Keynote.Core`, etc.), assets, settings, and publish outputs remain 100% untouched.
* **Completely mock data:** Songs, playback timings, notes, piano roll, target window, and keyboard mappings are purely local simulation state.
* **No game interaction:** No input injection, no memory inspection, and no process hooks into Game.
* **Isolated persistence:** Prototype user settings are saved exclusively to `%LOCALAPPDATA%\LarpianoOverlayPrototype\settings.json`.

---

## 2. Modes & Features

### Expanded Mode (Reference Design — ~920 × 440 px)
* **Header:**
  * Piano key logo mark + `larpiano` branding + `Overlay` badge.
  * Pin toggle (Always-On-Top), Settings / Opacity popover trigger, Compact mode toggle, Close button.
  * Non-activating drag region across the header.
* **Song & Target Summary:**
  * Vector artwork thumbnail (abstract cosmic landscape with purple mountain and moon).
  * Song Title: *"Cornfield Chase"* (with Next Track toggle to *"Interstellar Main Theme"*).
  * Metadata: *4 tracks • 1,127 notes • 2:02*.
  * Status Pill: Glowing waveform icon + *Playing* / *Paused* / *Ready* / *Stopped*.
  * Target Status Card: Game icon + *"Game: Demo Window (Mock)"* + *"• Demo foreground"*.
  * Mapping Status Card: Keyboard icon + *"Demo mapping • not connected"*.
* **Piano-Roll Preview:**
  * Pitch lanes *C6, C5, C4, C3, C2* with subtle divider lines.
  * Rich distribution of lavender note blocks matching Cornfield Chase soundtrack motifs.
  * Luminous vertical playhead synced with playback progress.
  * Interactive seek: click or drag on the piano roll to seek anywhere.
* **Transport Controls:**
  * Previous Track (`|<<`), Glowing Play/Pause pill (`||` / `>`), Next Track (`>>|`), Stop (`■`).
  * Time indicator: `0:26 / 2:02`.
  * Lavender progress slider with glowing thumb.
  * Speed selector dropdown (`0.75×`, `1.00×`, `1.25×`, `1.50×`).
  * Preview volume slider (labeled `Preview volume (demo)`).
* **Bottom Shortcut Row:**
  * `Ctrl + Alt + F8` — Play / Pause (demo trigger).
  * `Ctrl + Alt + F9` — Emergency Stop (demo trigger).
  * `Mapping` — opens Demo Keyboard Mapping popover.
  * `Diagnostics` — opens Non-Activating overlay diagnostics popover.
  * Chevron button `^` to collapse into Compact mode.

### Compact Mode (Slim Floating Bar — ~760 × 96 px)
* Single-row glass bar showing:
  1. Piano logo + `larpiano`.
  2. Song title + elapsed / total time (`Cornfield Chase • 0:26 / 2:02`).
  3. Interactive progress slider.
  4. Glowing Play/Pause button.
  5. Stop button.
  6. Cycle Speed button (`1.00×` → `1.25×` → `1.50×` → `0.75×`).
  7. Pin button (Always-on-top).
  8. Expand button (switches back to Expanded view).
  9. Close button.

---

## 3. Non-Activating Window Behavior

To ensure the underlying Game piano game maintains foreground keyboard focus while the player clicks overlay buttons or moves the overlay:
1. **Extended Win32 Styles:**
   * `WS_EX_NOACTIVATE (0x08000000)`: Prevents Windows from bringing the overlay to the foreground on window creation or clicks.
   * `WS_EX_TOOLWINDOW (0x00000080)`: Keeps it floating above standard applications without occupying a full taskbar slot.
   * `WS_EX_TOPMOST (0x00000008)`: Keeps the overlay above games when pinned.
2. **`WM_MOUSEACTIVATE` Interception:**
   * The window procedure intercepts `WM_MOUSEACTIVATE` and returns `MA_NOACTIVATE (3)`. This tells Windows to pass the mouse click to the overlay's controls without activating the window.
3. **Focus Prevention (`Focusable="False"`):**
   * All WPF buttons, sliders, and controls are styled with `Focusable="False"`, preventing WPF from triggering internal keyboard focus changes or Win32 `SetFocus` calls that could take foreground focus away from the game.
4. **Non-Activating Window Dragging & Resizing:**
   * Window dragging does **not** use `Window.DragMove()` (which sends `WM_NCLBUTTONDOWN` that forces Windows to activate). Instead, dragging calculates cursor deltas and updates window coordinates directly, keeping the game in the foreground throughout drag operations.

---

## 4. Multi-Monitor & Persistence

* Window position (`Left`, `Top`), dimensions (`ExpandedWidth`, `ExpandedHeight`), mode (`IsCompact`), pin state (`IsPinned`), opacity, speed, and volume are saved on shutdown or mode toggle to `%LOCALAPPDATA%\LarpianoOverlayPrototype\settings.json`.
* On startup, the prototype queries `SystemParameters.VirtualScreen*` bounds across all attached monitors. If coordinates fall outside the visible virtual desktop (e.g. if an external display was disconnected), it safely recenters on the primary display.

---

## 5. How to Run

### Published Standalone Executable:
Double-click or run:
```powershell
D:\projects\larpiano\larpiano-overlay-prototype\publish\Larpiano.OverlayPrototype.exe
```

### From Source via .NET CLI:
```powershell
dotnet run --project D:\projects\larpiano\larpiano-overlay-prototype\src\Larpiano.OverlayPrototype\Larpiano.OverlayPrototype.csproj
```
