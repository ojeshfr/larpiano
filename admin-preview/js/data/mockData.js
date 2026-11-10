/**
 * LARPIANO CONTROL CENTER — MOCK DATA MODULE
 * 
 * IMPORTANT: All data in this file is FICTIONAL DEMO DATA.
 * It is clearly labeled as sample data and does not connect to live Larpiano services.
 */

export const DEMO_METRICS = {
  onlineNow: 24,
  onlineDelta: "14 playing · 10 idle",
  onlineSparkline: [14, 16, 15, 18, 20, 19, 22, 24],

  totalAccounts: 1482,
  accountsDelta: "+38 this week · 0 flags",
  accountsSparkline: [1420, 1435, 1444, 1452, 1468, 1475, 1482],

  activeSessions: 37,
  sessionsDelta: "18,420 notes/min",
  sessionsSparkline: [20, 22, 25, 28, 27, 31, 35, 37],

  appVersion: "v1.1.0-beta.1",
  versionShare: "72.4% beta · 27.6% stable",
  versionDonutPercentage: 72.4
};

export const HOURLY_ACTIVITY_24H = [
  { time: "00:00", count: 8 },
  { time: "01:00", count: 6 },
  { time: "02:00", count: 5 },
  { time: "03:00", count: 4 },
  { time: "04:00", count: 6 },
  { time: "05:00", count: 8 },
  { time: "06:00", count: 12 },
  { time: "07:00", count: 15 },
  { time: "08:00", count: 19 },
  { time: "09:00", count: 21 },
  { time: "10:00", count: 23 },
  { time: "11:00", count: 22 },
  { time: "12:00", count: 24 },
  { time: "13:00", count: 26 },
  { time: "14:00", count: 25 },
  { time: "15:00", count: 27 },
  { time: "16:00", count: 30 },
  { time: "17:00", count: 32 },
  { time: "18:00", count: 35 },
  { time: "19:00", count: 38 },
  { time: "19:40", count: 24 }, // active sample cursor
  { time: "20:00", count: 31 },
  { time: "21:00", count: 27 },
  { time: "22:00", count: 20 },
  { time: "23:00", count: 14 }
];

export const RECENT_EVENTS = [
  {
    id: "evt_1",
    type: "live",
    tag: "LIVE",
    tagClass: "badge-playing",
    title: "virtuoso99 started Rush E",
    subject: "180 BPM · 88-Key Grand Preset · 14ms latency",
    time: "1m ago"
  },
  {
    id: "evt_2",
    type: "announcement",
    tag: "ALERT",
    tagClass: "badge-online",
    title: "Broadcast: v1.1.0-beta.1 is now live",
    subject: "Pushed to 37 connected Windows overlay clients",
    time: "8m ago"
  },
  {
    id: "evt_3",
    type: "session",
    tag: "GAME",
    tagClass: "badge-channel-beta",
    title: "Auto-focus linked for client 7e4c...9d2f",
    subject: "PianoGameWindow.exe (PID 14208) · Win32 Hooked",
    time: "16m ago"
  },
  {
    id: "evt_4",
    type: "catalog",
    tag: "CATALOG",
    tagClass: "badge-role-user",
    title: "izumi imported Chopin Nocturne Op. 9 No. 2",
    subject: "Downloaded from Project 83 Remapped Catalog",
    time: "22m ago"
  },
  {
    id: "evt_5",
    type: "release",
    tag: "RELEASE",
    tagClass: "badge-channel-stable",
    title: "Stable build published: v1.0.35",
    subject: "SHA256 verified · Windows x64 Portable ZIP",
    time: "3h ago"
  }
];

export const MOCK_USERS = [
  {
    id: "usr_8f4a12b",
    name: "izumi",
    role: "User",
    status: "Online",
    playingStatus: "Playing",
    activeSong: "Chopin — Nocturne Op. 9 No. 2",
    pianoLayout: "88-Key Grand",
    tempo: "1.00×",
    version: "v1.1.0-beta.1",
    lastSeen: "2m ago",
    sessionsCount: 1,
    firstSeen: "Nov 1, 2026",
    consentTelemetry: true,
    consentEffects: true,
    activeSessions: [
      { id: "a2b1f6e0-91c2", client: "Windows x64 (Game Focus)", duration: "21m", status: "Playing" }
    ]
  },
  {
    id: "usr_9921bc4",
    name: "purplekeys",
    role: "User",
    status: "Online",
    playingStatus: "Playing",
    activeSong: "Interstellar — Main Theme",
    pianoLayout: "Virtual Piano 61-Key",
    tempo: "1.25×",
    version: "v1.1.0-beta.1",
    lastSeen: "4m ago",
    sessionsCount: 2,
    firstSeen: "Oct 28, 2026",
    consentTelemetry: true,
    consentEffects: false,
    activeSessions: [
      { id: "7e4c9d2f-45a1", client: "Windows x64 (Overlay Pinned)", duration: "32m", status: "Playing" }
    ]
  },
  {
    id: "usr_3310fa8",
    name: "virtuosopianist",
    role: "User",
    status: "Idle",
    playingStatus: "Idle",
    activeSong: "In Lobby (Selecting MIDI)",
    pianoLayout: "AutoPiano Layout",
    tempo: "1.00×",
    version: "v1.0.9",
    lastSeen: "18m ago",
    sessionsCount: 1,
    firstSeen: "Oct 15, 2026",
    consentTelemetry: true,
    consentEffects: false,
    activeSessions: [
      { id: "bc6e7c44-aa02", client: "Windows x64", duration: "47m", status: "Idle" }
    ]
  },
  {
    id: "usr_48d197e",
    name: "cheesynator",
    role: "Tester",
    status: "Online",
    playingStatus: "Playing",
    activeSong: "Golden Hour (JVKE)",
    pianoLayout: "88-Key Grand",
    tempo: "1.00×",
    version: "v1.1.0-beta.1",
    lastSeen: "5m ago",
    sessionsCount: 1,
    firstSeen: "Oct 20, 2026",
    consentTelemetry: true,
    consentEffects: true,
    activeSessions: [
      { id: "49fc2ca1-817b", client: "Windows x64 (Overlay Active)", duration: "11m", status: "Playing" }
    ]
  },
  {
    id: "usr_77a2810",
    name: "virtuoso99",
    role: "Tester",
    status: "Online",
    playingStatus: "Playing",
    activeSong: "Rush E (Black MIDI Solo)",
    pianoLayout: "88-Key Grand (High BPM)",
    tempo: "1.50×",
    version: "v1.1.0-beta.1",
    lastSeen: "1m ago",
    sessionsCount: 1,
    firstSeen: "Oct 12, 2026",
    consentTelemetry: true,
    consentEffects: true,
    activeSessions: [
      { id: "8b31d044-64fe", client: "Windows x64 (Game Focus)", duration: "74m", status: "Playing" }
    ]
  },
  {
    id: "usr_6631ad9",
    name: "lane",
    role: "User",
    status: "Online",
    playingStatus: "Online",
    activeSong: "Beethoven — Moonlight Sonata",
    pianoLayout: "Virtual Piano 61-Key",
    tempo: "1.00×",
    version: "v1.1.0-beta.1",
    lastSeen: "6m ago",
    sessionsCount: 1,
    firstSeen: "Nov 5, 2026",
    consentTelemetry: true,
    consentEffects: true,
    activeSessions: [
      { id: "f1a9e022-b91c", client: "Windows x64", duration: "6m", status: "Online" }
    ]
  },
  {
    id: "usr_10283fa",
    name: "voidkeys",
    role: "User",
    status: "Offline",
    version: "v1.0.8",
    lastSeen: "2h ago",
    sessionsCount: 0,
    firstSeen: "Nov 3, 2026",
    consentTelemetry: false,
    consentEffects: false,
    activeSessions: []
  },
  {
    id: "usr_2245bca",
    name: "celeste_key",
    role: "User",
    status: "Idle",
    playingStatus: "Idle",
    activeSong: "Paused · Measure 34",
    pianoLayout: "88-Key Standard",
    tempo: "1.00×",
    version: "v1.1.0-beta.1",
    lastSeen: "14m ago",
    sessionsCount: 1,
    firstSeen: "Nov 2, 2026",
    consentTelemetry: true,
    consentEffects: false,
    activeSessions: [
      { id: "3c9a117b-91cc", client: "Windows x64", duration: "18m", status: "Idle" }
    ]
  },
  {
    id: "usr_0019ff2",
    name: "midi_wizard",
    role: "Admin",
    status: "Online",
    playingStatus: "Online",
    activeSong: "Diagnostic Loopback Test",
    pianoLayout: "All Channels Unlocked",
    tempo: "1.00×",
    version: "v1.1.0-beta.1",
    lastSeen: "Just now",
    sessionsCount: 1,
    firstSeen: "Oct 1, 2026",
    consentTelemetry: true,
    consentEffects: true,
    activeSessions: [
      { id: "5d840ee1-0419", client: "Windows x64 (Control Panel)", duration: "160m", status: "Online" }
    ]
  },
  {
    id: "usr_551493b",
    name: "chopin_fan",
    role: "User",
    status: "Suspended",
    version: "v1.0.8",
    lastSeen: "3d ago",
    sessionsCount: 0,
    firstSeen: "Oct 18, 2026",
    consentTelemetry: false,
    consentEffects: false,
    activeSessions: []
  }
];

export const MOCK_SESSIONS = [
  {
    id: "7e4c9d2f-45a1",
    user: "purplekeys",
    version: "v1.1.0-beta.1",
    started: "32m ago",
    lastHeartbeat: "5s ago",
    duration: "32m",
    status: "Playing",
    statusType: "playing"
  },
  {
    id: "a2b1f6e0-91c2",
    user: "izumi",
    version: "v1.1.0-beta.1",
    started: "21m ago",
    lastHeartbeat: "2s ago",
    duration: "21m",
    status: "Online",
    statusType: "online"
  },
  {
    id: "49fc2ca1-817b",
    user: "cheesynator",
    version: "v1.1.0-beta.1",
    started: "11m ago",
    lastHeartbeat: "1s ago",
    duration: "11m",
    status: "Playing",
    statusType: "playing"
  },
  {
    id: "bc6e7c44-aa02",
    user: "virtuosopianist",
    version: "v1.0.9",
    started: "47m ago",
    lastHeartbeat: "14s ago",
    duration: "47m",
    status: "Idle",
    statusType: "idle"
  },
  {
    id: "f1a9e022-b91c",
    user: "lane",
    version: "v1.1.0-beta.1",
    started: "6m ago",
    lastHeartbeat: "3s ago",
    duration: "6m",
    status: "Online",
    statusType: "online"
  },
  {
    id: "8b31d044-64fe",
    user: "virtuoso99",
    version: "v1.1.0-beta.1",
    started: "1h 14m ago",
    lastHeartbeat: "4s ago",
    duration: "74m",
    status: "Playing",
    statusType: "playing"
  },
  {
    id: "3c9a117b-91cc",
    user: "celeste_key",
    version: "v1.1.0-beta.1",
    started: "18m ago",
    lastHeartbeat: "8s ago",
    duration: "18m",
    status: "Idle",
    statusType: "idle"
  },
  {
    id: "5d840ee1-0419",
    user: "midi_wizard",
    version: "v1.1.0-beta.1",
    started: "2h 40m ago",
    lastHeartbeat: "2s ago",
    duration: "160m",
    status: "Online",
    statusType: "online"
  }
];

export const MOCK_ANALYTICS = {
  dau: 312,
  dauDelta: "+12%",
  wau: 1128,
  wauDelta: "+18%",
  avgSession: "18m 24s",
  avgSessionDelta: "+6%",

  dailyUsers7d: [
    { label: "Nov 2", count: 240 },
    { label: "Nov 3", count: 260 },
    { label: "Nov 4", count: 275 },
    { label: "Nov 5", count: 290 },
    { label: "Nov 6", count: 310 },
    { label: "Nov 7", count: 325 },
    { label: "Nov 8", count: 312 }
  ],

  dailyUsers30d: [
    { label: "W1", count: 1420 },
    { label: "W2", count: 1650 },
    { label: "W3", count: 1890 },
    { label: "W4", count: 2180 }
  ],

  versionDistribution: [
    { version: "v1.1.0-beta.1", share: 72.4, color: "#8B5CF6" },
    { version: "v1.0.9", share: 18.7, color: "#6366F1" },
    { version: "v1.0.8", share: 6.1, color: "#A78BFA" },
    { version: "Other", share: 2.8, color: "#71717A" }
  ]
};

export const MOCK_ANNOUNCEMENTS = [
  {
    id: "anc_1",
    title: "v1.1.0-beta.1 is now live",
    message: "v1.1.0 released. Compact floating HUD, cleaner Aero2 context menus, and auto-focus for games. Download the ZIP from Portways.",
    audience: "Global",
    status: "Active",
    created: "Nov 8, 2026",
    expires: "No expiry"
  },
  {
    id: "anc_2",
    title: "Catalog maintenance scheduled",
    message: "Scheduled maintenance on Nov 10 (18:00 UTC). Rebuilding song search indexes on Project 83. Local files and cached songs remain available.",
    audience: "Global",
    status: "Scheduled",
    created: "Nov 10, 2026",
    expires: "Nov 10, 19:00"
  },
  {
    id: "anc_3",
    title: "Multi-client overlay testing",
    message: "Multi-instance game testing is open. Hit Ctrl+Alt+Tab in the floating overlay to cycle between active game windows.",
    audience: "Beta testers",
    status: "Active",
    created: "Nov 6, 2026",
    expires: "Nov 20, 2026"
  },
  {
    id: "anc_4",
    title: "High BPM note-drop feedback",
    message: "Testing key spam on 300+ BPM tracks. If the game drops inputs on your keyboard layout, post your diagnostic logs in Discord.",
    audience: "Beta testers",
    status: "Expired",
    created: "Nov 1, 2026",
    expires: "Nov 5, 2026"
  }
];

export const MOCK_RELEASES = [
  {
    version: "v1.1.0-beta.1",
    channel: "Beta",
    date: "Nov 8, 2026",
    status: "Latest Beta",
    platform: "Windows x64",
    githubUrl: "https://github.com/xhakkurw233/larpiano/releases/tag/v1.1.0-beta.1",
    notes: "Redesigned dark UI, compact floating HUD for virtual pianos, background window detection, and 223 passing unit tests."
  },
  {
    version: "v1.0.35",
    channel: "Stable",
    date: "Nov 7, 2026",
    status: "Published",
    platform: "Windows x64",
    githubUrl: "https://github.com/xhakkurw233/larpiano/releases/tag/v1.0.35",
    notes: "Fixed white gutter stripe in overlay context menus, added multi-instance game cycling, and fixed background focus polling."
  },
  {
    version: "v1.0.34",
    channel: "Stable",
    date: "Nov 7, 2026",
    status: "Published",
    platform: "Windows x64",
    githubUrl: "https://github.com/xhakkurw233/larpiano/releases/tag/v1.0.34",
    notes: "Hardened game window focus checks and continuous background polling."
  },
  {
    version: "v1.0.28",
    channel: "Stable",
    date: "Nov 4, 2026",
    status: "Archived",
    platform: "Windows x64",
    githubUrl: "https://github.com/xhakkurw233/larpiano/releases/tag/v1.0.28",
    notes: "First public release with MIDI extraction and local song library."
  }
];

export const PLAYGROUND_EFFECTS = [
  {
    id: "toast",
    name: "Send toast",
    icon: "⚡",
    desc: "Dispatches an in-game toast alert over the active game window.",
    duration: "5s",
    category: "Notification"
  },
  {
    id: "theme",
    name: "Theme takeover",
    icon: "🎨",
    desc: "Temporarily shifts the overlay accent color to lavender for 8 seconds.",
    duration: "8s",
    category: "Visual"
  },
  {
    id: "confetti",
    name: "Confetti",
    icon: "🎉",
    desc: "Triggers a 5-second confetti particle burst across the screen.",
    duration: "5s",
    category: "Celebration"
  },
  {
    id: "vip",
    name: "VIP badge",
    icon: "💎",
    desc: "Pins a temporary VIP star badge next to the user's name.",
    duration: "15s",
    category: "Cosmetic"
  },
  {
    id: "piano",
    name: "Piano celebration",
    icon: "🎹",
    desc: "Animates a 12-key piano run along the bottom edge of the screen.",
    duration: "8s",
    category: "Animation"
  },
  {
    id: "custom",
    name: "Custom effect",
    icon: "✨",
    desc: "Pulses a subtle lavender gradient wave across the screen.",
    duration: "10s",
    category: "Experimental"
  }
];

export const MOCK_AUDIT_LOGS = [
  {
    id: "aud_1",
    time: "2026-11-08 02:14:02",
    actor: "Admin (ojesh)",
    action: "Ran local effect preview: Theme takeover",
    target: "Local prototype (Theme takeover)",
    result: "Success",
    details: "Tested 8s lavender takeover in browser preview"
  },
  {
    id: "aud_2",
    time: "2026-11-08 01:52:10",
    actor: "System",
    action: "Indexed release v1.1.0-beta.1 (Windows x64)",
    target: "v1.1.0-beta.1",
    result: "Success",
    details: "Verified SHA256 checksum and ZIP distribution asset"
  },
  {
    id: "aud_3",
    time: "2026-11-08 01:48:33",
    actor: "Admin (ojesh)",
    action: "Published announcement: v1.1.0-beta.1 is now live",
    target: "v1.1.0-beta.1 is now live",
    result: "Success",
    details: "Pushed to global channel with no expiry"
  },
  {
    id: "aud_4",
    time: "2026-11-08 01:10:15",
    actor: "Admin (ojesh)",
    action: "Inspected user drawer for usr_9921bc4 (purplekeys)",
    target: "usr_9921bc4 (purplekeys)",
    result: "Success",
    details: "Verified client version v1.1.0-beta.1 and active session status"
  },
  {
    id: "aud_5",
    time: "2026-11-08 00:34:40",
    actor: "Admin (ojesh)",
    action: "Console session authenticated",
    target: "Control Center Session",
    result: "Success",
    details: "Logged in as ojesh (Super Admin) via developer token"
  },
  {
    id: "aud_6",
    time: "2026-11-07 23:22:18",
    actor: "Admin (ojesh)",
    action: "Simulated suspension check on usr_551493b (chopin_fan)",
    target: "usr_551493b (chopin_fan)",
    result: "Previewed",
    details: "Tested account suspension drawer flow (offline simulation)"
  },
  {
    id: "aud_7",
    time: "2026-11-07 22:15:00",
    actor: "System",
    action: "Purged 3 stale sessions (>10m inactivity)",
    target: "Stale session daemon",
    result: "Success",
    details: "Garbage collection cleared orphaned heartbeat records"
  }
];

export const MOCK_NOTIFICATIONS = [
  { id: "notif_1", title: "Player surge", desc: "+6 concurrent players linked to virtual pianos in the last hour.", time: "12m ago", unread: true },
  { id: "notif_2", title: "Build verified", desc: "SHA256 matches official release for v1.1.0-beta.1.", time: "45m ago", unread: true },
  { id: "notif_3", title: "Song catalog online", desc: "42ms API response time on Project 83.", time: "2h ago", unread: false }
];
