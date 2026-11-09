# Changelog

All notable changes to **larpiano** will be documented in this file.

## [1.1.0] — 2026-11-09

### Added
- **Strict Opt-in Privacy System:** New accounts default to privacy-first settings (`presence_consent = false`, `telemetry_consent = false`) with zero login/signup interruption and settings controls in the user profile.
- **Server-Side Enforcement:** Backend API rejects unconsented presence heartbeats and telemetry submissions; immediately closes active presence sessions when consent is revoked.
- **Real Admin Analytics Engine:** 24-hour activity curve, DAU/WAU tracking, session durations, and version distribution computed directly from PostgreSQL tables.
- **Cross-Tab Synchronization:** Automated data invalidation across open admin dashboard browser tabs via `BroadcastChannel`.

### Fixed
- **Admin Dashboard Integrity:** Eliminated mock data leakage across all 7 admin pages (Overview, Analytics, Users, Live Sessions, Announcements, Releases, Audit Log).
- **Dynamic Fleet Health Indicator:** Connection banner reflects real backend status (`LIVE API CONNECTED`, `API OFFLINE`) based on live `/api/health` probes.
- **Authentication Error Handling:** Graceful fallback and clear error messaging for non-JSON or HTML responses during backend deployment transitions.

## [1.0.35] — 2026-11-08

### Fixed
- **Overlay App Sections Menu Styling:** Replaced default Windows Aero2 ContextMenu template with custom dark elevated surface and drop shadow, completely removing the light icon column gutter stripe that clipped menu item text.

## [1.0.34] — 2026-11-08

### Added
- **Continuous Background Auto-Detection:** Periodic background scanning that automatically detects and binds to Roblox as soon as it launches or reconnects.
- **Robust Multi-Channel Roblox Identification:** Support for process name matching, window class `WINDOWSCLIENT`, and window title checks without elevated permissions.
- **Multi-Instance Window Cycling:** Interactive target window card in the overlay supporting 1-click re-scan and cycling between multiple Roblox instances.
- **Compact View Target Focus Indicator:** Real-time status dot in the compact pill bar showing Roblox window foreground state.
- **Enhanced Foreground Accuracy:** Win32 root window ancestor traversal preventing focus drops when interacting with inner game UI.

## [1.0.33] — 2026-11-07
- Fix overlay menu and song selection.

## [1.0.32] — 2026-11-07
- Add optional warm-up and overlay tempo slider.

## [1.0.31] — 2026-11-07
- Add MIDI browsing and app navigation to overlay.

## [1.0.30] — 2026-11-07
- Add library song selector to overlay.

## [1.0.29] — 2026-11-08

### Added
- **Complete Visual Reconstruction:**
  - Modern dark music interface with obsidian background and lavender accents.
  - Editorial serif hero header with high-resolution grand piano keys asset.
  - Sidebar navigation with glowing neon waveform curve and version indicator.
  - Interactive piano roll preview with real-time progress cursor and pitch labels.
  - Persistent bottom player bar with transport controls, scrub bar, volume, and overlay toggle.
  - Quick action cards for Play/Pause, Stop, Mapping calibration, and Diagnostics.
- **Authentic Brand Restoration:**
  - Original 256×256 purple icon mark in the application header.
  - Preserved wordmark `larpiano / piano player`.
  - Preserved verbatim community taglines in Hero and Settings/About sections:
    - `"from da people who LARP, for da people who LARP"`
    - `"for da people who larp  ·  for da poeple who believe in larpocracy"`
- **Outer Window Corner Rounding:**
  - 14px outer corner radius on main window frame with complete visual clipping.
  - Windows Desktop Window Manager (DWM) integration via `DwmSetWindowAttribute` (`DWMWA_WINDOW_CORNER_PREFERENCE = 33`, `DWMWCP_ROUND = 2`).
  - Maximized window state handling to dynamically revert to 0px border radius when snapped/maximized.
- **Integrated Floating Overlay:**
  - Non-activating window style (`WS_EX_NOACTIVATE`) allowing clicks without stealing focus from Roblox.
  - Always-on-top floating HUD with subtle glassmorphism (97% opacity) and illuminated purple border glow.
  - Dual-mode operation: Expanded dashboard view and Compact pill mode.
  - Interactive transport controls, speed selector, volume slider, and note roll visualizer.
  - Real-time two-way synchronization with main application `PlaybackEngine` and keyboard input.
- **Quality Assurance & Automated Testing:**
  - Added `Keynote.Overlay.Tests` suite with 31 tests.
  - Non-activating window behavior and click-through verification tests.
  - Automated STA screenshot capture tests covering all 9 application views.
  - Total automated test count increased to 210 passing tests.
- **Song Catalog & Mapping Extensions:**
  - Added extended prepared song mappings for modern arrangements.
  - Extended download provider endpoint validation.

### Changed
- Refactored UI resource dictionaries into modular themes (`LarpianoColors.xaml`, `LarpianoTypography.xaml`, `LarpianoControls.xaml`).
- Upgraded target framework and dependencies for .NET 10.
- Enhanced playback cancellation safety to ensure immediate key release on stop or focus loss.

---

## [1.0.28] — 2026-11-07
- Add mapped After Dark arrangement.
- Add mapped Pretty When You Cry arrangement.
- Add mapped Cruel World arrangement.
- Add mapped God Knows I Tried arrangement.
- Add mapped Video Games arrangement.
- Fix creator-hosted Google Drive downloads.

## [1.0.27] — 2026-11-07
- Add The Weeknd ft. Future - Double Fantasy.
- Add Lana Del Rey arrangements (Lolita, Dealer, Summertime Sadness).

## [1.0.0] — 2026-11-03
- Initial stable release of larpiano.
