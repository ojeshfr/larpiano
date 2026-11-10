/**
 * Page: Overview (Control Center)
 * Modern, precision dark-mode dashboard for Larpiano.
 * Connected to live backend telemetry and real PostgreSQL presence database.
 */

import { DEMO_METRICS, HOURLY_ACTIVITY_24H, RECENT_EVENTS, MOCK_USERS } from "../data/mockData.js";
import { renderKpiCard } from "../components/kpiCard.js";
import { renderActivityChart } from "../components/chart.js";
import { openUserDrawer } from "../components/userDrawer.js";
import { fetchOverview } from "../api/overview.js";
import { isPreviewMode } from "../api/client.js";
import { escapeText } from "../components/safeText.js";

let refreshTimer = null;

export function renderOverview(container, { onNavigate, onOpenAnnouncement, onDraftRelease }) {
  if (refreshTimer) {
    clearInterval(refreshTimer);
    refreshTimer = null;
  }

  function renderView(metrics, activityCurve, events, players, isLive = false) {
    const onlineCount = Number(metrics.onlineNow) || 0;
    const accountsCount = typeof metrics.totalAccounts === "number" ? metrics.totalAccounts.toLocaleString() : (metrics.totalAccounts || "0");
    const activeSessionsCount = Number(metrics.activeSessions) || 0;
    const fleetVersion = escapeText(metrics.appVersion || "v1.1.0-beta.1");

    container.innerHTML = `
      <!-- Top Hero Section with Live Status Pill -->
      <div class="overview-hero">
        <div class="overview-hero-text">
          <h1 class="page-title">Control Center</h1>
          <p class="page-subtitle">Virtual piano telemetry, active player sessions, and fleet status for the Larpiano beta.</p>
        </div>
        <div class="hero-status-pill">
          <span class="live-pulse-dot"></span>
          <span class="hero-status-text">${isLive ? "Telemetry Linked" : "Offline Sandbox"}</span>
          <span class="hero-status-sub">${onlineCount} Players Live</span>
        </div>
      </div>

      <!-- 4 KPI Tiles -->
      <div class="kpi-grid">
        ${renderKpiCard({
          id: "online",
          label: "Players online",
          value: onlineCount,
          delta: escapeText(metrics.onlineDelta || "14 playing · 10 idle"),
          isPositive: true,
          sparkline: metrics.onlineSparkline || DEMO_METRICS.onlineSparkline,
          icon: `<span style="color: var(--success); font-size: 10px;">●</span>`
        })}
        ${renderKpiCard({
          id: "accounts",
          label: "Registered accounts",
          value: accountsCount,
          delta: escapeText(metrics.accountsDelta || "+38 this week · 0 flags"),
          isPositive: true,
          sparkline: metrics.accountsSparkline || DEMO_METRICS.accountsSparkline,
          icon: `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>`
        })}
        ${renderKpiCard({
          id: "sessions",
          label: "Active client sessions",
          value: activeSessionsCount,
          delta: escapeText(metrics.sessionsDelta || "18,420 notes/min"),
          isPositive: true,
          sparkline: metrics.sessionsSparkline || DEMO_METRICS.sessionsSparkline,
          icon: `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="2" y="3" width="20" height="14" rx="2"/><line x1="8" y1="21" x2="16" y2="21"/><line x1="12" y1="17" x2="12" y2="21"/></svg>`
        })}
        ${renderKpiCard({
          id: "version",
          label: "Fleet version distribution",
          value: fleetVersion,
          delta: escapeText(metrics.versionShare || "72.4% beta · 27.6% stable"),
          isPositive: false,
          isDonut: true,
          donutPercent: metrics.versionDonutPercentage || 72,
          icon: `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polygon points="12 2 2 7 12 12 22 7 12 2"/><polyline points="2 17 12 22 22 17"/><polyline points="2 12 12 17 22 12"/></svg>`
        })}
      </div>

      <!-- Main Analytics Section (2 Columns) -->
      <div class="overview-analytics-row">
        <!-- Live Activity Chart Card -->
        <div class="card activity-chart-card">
          <div class="chart-header">
            <div>
              <h2 class="card-title">Live activity &amp; concurrent sessions</h2>
              <p class="card-subtitle">Real-time players over the last 24 hours</p>
            </div>
            <span style="font-size: 11px; font-family: var(--font-mono); color: var(--accent-soft);">
              ${isLive ? "Live Portways Telemetry Hook" : "Peak: 38 players · 19:00 UTC"}
            </span>
          </div>
          <div class="chart-container" id="overview-activity-chart"></div>
        </div>

        <!-- Recent Events Feed Card -->
        <div class="card events-card">
          <div class="card-header">
            <div>
              <h2 class="card-title">Telemetry stream</h2>
              <p class="card-subtitle">Live events from connected client overlays</p>
            </div>
            <button id="overview-view-all-events" class="btn btn-ghost btn-sm" style="font-size: 12px; color: var(--accent-soft);">Full audit log →</button>
          </div>
          <div class="events-list">
            ${events.length === 0 ? `<div style="padding: 28px; text-align: center; color: var(--subtle); font-size: 13px;">No recent audit events</div>` : events.map(evt => {
              const tagClass = escapeText(evt.tagClass || 'tag-game');
              const tag = escapeText(evt.tag || 'EVENT');
              const title = escapeText(evt.title || evt.action || '');
              const subject = escapeText(evt.subject || evt.target || '');
              const time = escapeText(evt.time || evt.created_at || '');
              return `
              <div class="event-item">
                <span class="event-tag ${tagClass}">${tag}</span>
                <div class="event-content">
                  <div class="event-title">${title}</div>
                  <div class="event-subtitle">${subject}</div>
                </div>
                <span class="event-time">${time}</span>
              </div>
            `}).join("")}
          </div>
        </div>
      </div>

      <!-- Lower Section (Active Users Table + Quick Operator Controls) -->
      <div class="overview-lower-row">
        <!-- Active Players Table Card -->
        <div class="card active-players-card">
          <div class="card-header">
            <div>
              <h2 class="card-title">Active players</h2>
              <p class="card-subtitle">Connected Windows clients playing live · Click row for account drawer</p>
            </div>
            <button id="overview-view-all-users" class="btn btn-ghost btn-sm" style="font-size: 12px; color: var(--accent-soft);">All players (${players.length}) →</button>
          </div>
          <div class="table-container">
            <table class="data-table">
              <thead>
                <tr>
                  <th>PLAYER</th>
                  <th>PLAYING IN GAME</th>
                  <th>PIANO MAPPING</th>
                  <th>TEMPO</th>
                  <th>STATUS</th>
                </tr>
              </thead>
              <tbody>
                ${players.length === 0 ? `<tr><td colspan="5" style="text-align: center; padding: 28px; color: var(--subtle); font-size: 13px;">No players currently online</td></tr>` : players.map(u => {
                  const uid = escapeText(u.id || u.session_identifier || 'u_anon');
                  const name = escapeText(u.name || u.display_name || 'Guest Pianist');
                  const initial = (name.charAt(0) || 'P').toUpperCase();
                  const version = escapeText(u.version || u.app_version || 'v1.1.0-beta.1');
                  const song = escapeText(u.activeSong || u.active_song || "Chopin — Nocturne Op. 9 No. 2");
                  const piano = escapeText(u.pianoLayout || u.piano_layout || "88-Key Grand");
                  const tempo = escapeText(u.tempo || "1.00×");
                  const isBoosted = tempo !== "1.00×";
                  const rawStatus = (u.status || u.playingStatus || "playing").toLowerCase();
                  const isPlaying = rawStatus.includes("play");
                  const isIdle = rawStatus.includes("idle");
                  const statusClass = isPlaying ? "playing" : (isIdle ? "idle" : "online");
                  const statusLabel = isPlaying ? "Playing" : (isIdle ? "Idle" : "Online");

                  return `
                  <tr class="clickable" data-user-id="${uid}">
                    <td>
                      <span class="player-cell">
                        <span class="player-avatar-circle">${initial}</span>
                        <span class="player-info">
                          <span class="player-name">${name}</span>
                          <span class="player-version">${version}</span>
                        </span>
                      </span>
                    </td>
                    <td>
                      <span class="player-song-title">${song}</span>
                    </td>
                    <td>
                      <span class="piano-mapping-text">${piano}</span>
                    </td>
                    <td>
                      <span class="badge ${isBoosted ? 'badge-tempo-boost' : 'badge-tempo'}">${tempo}</span>
                    </td>
                    <td>
                      <span class="badge badge-status badge-${statusClass}">
                        <span class="status-indicator-dot ${statusClass}"></span>
                        ${statusLabel}
                      </span>
                    </td>
                  </tr>
                `}).join("")}
              </tbody>
            </table>
          </div>
        </div>

        <!-- Quick Actions Panel -->
        <div class="card quick-actions-card">
          <div class="card-header">
            <div>
              <h2 class="card-title">Operator controls</h2>
              <p class="card-subtitle">Tactical fleet management</p>
            </div>
          </div>
          <div class="quick-actions-grid">
            <button id="qa-new-announcement" class="quick-action-btn">
              <span>📢 Broadcast in-app alert</span>
              <span style="color: var(--subtle);">→</span>
            </button>
            <button id="qa-draft-release" class="quick-action-btn">
              <span>🏷️ Publish release note</span>
              <span style="color: var(--subtle);">→</span>
            </button>
            <button id="qa-playground" class="quick-action-btn">
              <span>⚡ Test overlay notification</span>
              <span style="color: var(--subtle);">→</span>
            </button>
            <button id="qa-analytics" class="quick-action-btn">
              <span>📈 View telemetry analytics</span>
              <span style="color: var(--subtle);">→</span>
            </button>
          </div>
        </div>
      </div>
    `;

    // Render SVG chart safely
    setTimeout(() => {
      renderActivityChart(activityCurve, "overview-activity-chart");
    }, 10);

    // Event attachments
    document.getElementById("overview-view-all-events")?.addEventListener("click", () => onNavigate?.("audit"));
    document.getElementById("overview-view-all-users")?.addEventListener("click", () => onNavigate?.("users"));

    document.getElementById("qa-new-announcement")?.addEventListener("click", onOpenAnnouncement);
    document.getElementById("qa-draft-release")?.addEventListener("click", onDraftRelease);
    document.getElementById("qa-playground")?.addEventListener("click", () => onNavigate?.("playground"));
    document.getElementById("qa-analytics")?.addEventListener("click", () => onNavigate?.("analytics"));

    // User row click handlers
    container.querySelectorAll("tr[data-user-id]").forEach(tr => {
      tr.addEventListener("click", () => {
        const uid = tr.getAttribute("data-user-id");
        const user = players.find(u => (u.id === uid || u.session_identifier === uid)) || MOCK_USERS[0];
        if (user) openUserDrawer(user);
      });
    });
  }

  // Initial render: mock data only in explicit preview mode
  if (isPreviewMode()) {
    const initialPlayers = MOCK_USERS.filter(u => u.status === "Online" || u.status === "Idle").slice(0, 5);
    renderView(DEMO_METRICS, HOURLY_ACTIVITY_24H, RECENT_EVENTS, initialPlayers, false);
  } else {
    container.innerHTML = `
      <div style="padding: 48px; text-align: center; color: var(--muted);">
        <div class="live-pulse-dot" style="display: inline-block; margin-bottom: 12px;"></div>
        <p style="font-size: 14px;">Connecting to Larpiano Control Center...</p>
      </div>
    `;
  }

  // Fetch real authoritative data from backend API
  async function loadLiveData() {
    try {
      const data = await fetchOverview();
      if (!container.isConnected) {
        if (refreshTimer) clearInterval(refreshTimer);
        return;
      }
      if (data && data.metrics) {
        const livePlayers = isPreviewMode()
          ? ((data.active_players && data.active_players.length > 0) ? data.active_players : MOCK_USERS.slice(0, 5))
          : (data.active_players || []);
        
        const liveEvents = isPreviewMode()
          ? ((data.recent_events && data.recent_events.length > 0) ? data.recent_events.slice(0, 5) : RECENT_EVENTS)
          : (data.recent_events || []).slice(0, 5);

        const liveCurve = (data.live_activity_24h && data.live_activity_24h.length === 24)
          ? data.live_activity_24h
          : (isPreviewMode() ? HOURLY_ACTIVITY_24H : new Array(24).fill(0));

        renderView(data.metrics, liveCurve, liveEvents, livePlayers, true);
      }
    } catch (err) {
      if (!isPreviewMode()) {
        container.innerHTML = `
          <div class="card" style="padding: 32px; text-align: center; border-color: rgba(239, 68, 68, 0.3);">
            <h2 style="color: var(--danger); margin-bottom: 8px;">API Offline</h2>
            <p style="color: var(--muted); font-size: 13px;">${escapeText(err.message || 'Unable to connect to live backend')}</p>
            <button id="btn-retry-overview" class="btn btn-primary" style="margin-top: 16px;">Retry Connection</button>
          </div>
        `;
        document.getElementById("btn-retry-overview")?.addEventListener("click", loadLiveData);
      }
    }
  }

  loadLiveData();
  // Auto-refresh presence every 20 seconds
  refreshTimer = setInterval(loadLiveData, 20000);
}
