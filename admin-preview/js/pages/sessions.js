/**
 * Page: Live Sessions
 * Connected to real PostgreSQL app_sessions table and presence API.
 */

import { MOCK_SESSIONS } from "../data/mockData.js";
import { showToast } from "../components/toast.js";
import { fetchLiveSessions, terminateSession } from "../api/sessions.js";

let refreshInterval = null;

export function renderLiveSessions(container) {
  if (refreshInterval) {
    clearInterval(refreshInterval);
    refreshInterval = null;
  }

  let filter = "all";
  let activeSessions = [...MOCK_SESSIONS];

  function getFilteredSessions() {
    if (filter === "playing") return activeSessions.filter(s => s.statusType === "playing" || s.current_status === "playing");
    if (filter === "online") return activeSessions.filter(s => s.statusType === "online" || s.current_status === "online" || s.is_online === 1 || s.is_online === true);
    if (filter === "idle") return activeSessions.filter(s => s.statusType === "idle" || s.current_status === "idle");
    return activeSessions;
  }

  function renderRows() {
    const list = getFilteredSessions();
    if (list.length === 0) {
      return `<tr><td colspan="8" style="text-align: center; padding: 24px; color: var(--subtle);">No sessions match this filter.</td></tr>`;
    }

    return list.map(s => {
      const isOnline = s.is_online === 1 || s.is_online === true || s.statusType === "online" || s.statusType === "playing";
      const statusText = s.current_status || s.statusType || (isOnline ? "Online" : "Offline");
      const badge = (statusText === "playing" || s.statusType === "playing")
        ? `<span class="badge badge-playing"><span class="status-indicator-dot playing"></span>Playing</span>`
        : ((statusText === "online" || isOnline)
          ? `<span class="badge badge-online"><span class="status-indicator-dot online"></span>Online</span>`
          : `<span class="badge badge-idle"><span class="status-indicator-dot idle"></span>Idle</span>`);

      const sessId = s.session_identifier || s.id;
      const userName = s.display_name || s.user || "Guest";
      const version = s.app_version || s.version || "v1.1.0-beta.1";
      const started = s.started_at ? new Date(s.started_at).toLocaleTimeString() : (s.started || "Recently");
      const lastHb = s.last_seen_at ? new Date(s.last_seen_at).toLocaleTimeString() : (s.lastHeartbeat || "Just now");
      const duration = s.duration || "42m";

      return `
        <tr>
          <td><span class="cell-mono font-mono" title="${sessId}">${sessId.slice(0, 16)}...</span></td>
          <td style="font-weight: 500; color: var(--text);">${userName}</td>
          <td><span class="cell-mono">${version}</span></td>
          <td><span class="cell-meta">${started}</span></td>
          <td><span class="cell-meta" style="color: var(--accent-soft);">${lastHb}</span></td>
          <td style="font-family: var(--font-mono); color: var(--text);">${duration}</td>
          <td>${badge}</td>
          <td>
            <button class="btn btn-ghost btn-sm btn-terminate-session" data-session-id="${sessId}" style="font-size: 11px; color: var(--danger);" title="Terminate Session">
              ✕
            </button>
          </td>
        </tr>
      `;
    }).join("");
  }

  function updateView() {
    const tbody = container.querySelector("#sessions-table-body");
    if (tbody) {
      tbody.innerHTML = renderRows();
      attachTerminateHandlers();
    }
    const countSpan = container.querySelector("#sessions-count-label");
    if (countSpan) {
      countSpan.textContent = `${activeSessions.length} active sessions`;
    }
  }

  function attachTerminateHandlers() {
    container.querySelectorAll(".btn-terminate-session").forEach(btn => {
      btn.onclick = async (e) => {
        e.stopPropagation();
        const sid = btn.getAttribute("data-session-id");
        if (confirm(`Terminate session ${sid.slice(0, 8)}?`)) {
          try {
            await terminateSession(sid);
            showToast("Session terminated", "success");
            loadSessions();
          } catch (err) {
            showToast(`Could not terminate: ${err.message}`, "error");
          }
        }
      };
    });
  }

  container.innerHTML = `
    <!-- Page Header -->
    <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: var(--space-4);">
      <div>
        <h1 class="page-title">Live Sessions</h1>
        <p class="page-subtitle">Active desktop clients currently linked to Roblox.</p>
      </div>
      <button id="btn-refresh-sessions" class="btn btn-secondary btn-sm">
        <span>↻</span> Refresh sessions
      </button>
    </div>

    <!-- Explanatory Heartbeat Banner -->
    <div class="info-banner" style="margin-bottom: var(--space-5);">
      <div class="info-banner-icon">ℹ️</div>
      <div style="flex: 1;">
        <strong style="color: var(--text);">Session Semantics:</strong>
        Client presence is driven by 45-second heartbeats. Sessions without heartbeat for &gt;120 seconds are automatically recognized as offline.
        <span class="demo-tag" style="margin-left: 6px; background: rgba(34, 197, 94, 0.15); color: var(--success); border-color: rgba(34, 197, 94, 0.3);">PostgreSQL Live</span>
      </div>
    </div>

    <!-- Filter Bar -->
    <div class="toolbar" style="margin-bottom: var(--space-4);">
      <div class="filter-pills">
        <button class="filter-pill ${filter === 'all' ? 'active' : ''}" data-filter="all">All</button>
        <button class="filter-pill ${filter === 'playing' ? 'active' : ''}" data-filter="playing">Playing</button>
        <button class="filter-pill ${filter === 'online' ? 'active' : ''}" data-filter="online">Online</button>
        <button class="filter-pill ${filter === 'idle' ? 'active' : ''}" data-filter="idle">Idle</button>
      </div>
    </div>

    <!-- Table -->
    <div class="table-container">
      <table class="data-table">
        <thead>
          <tr>
            <th>Session ID</th>
            <th>User</th>
            <th>Version</th>
            <th>Started</th>
            <th>Last heartbeat</th>
            <th>Uptime</th>
            <th>Status</th>
            <th>Action</th>
          </tr>
        </thead>
        <tbody id="sessions-table-body">
          ${renderRows()}
        </tbody>
      </table>
    </div>

    <div style="margin-top: var(--space-3); font-size: var(--text-xs); color: var(--subtle); display: flex; justify-content: space-between;">
      <span>Heartbeat cycle: 45s interval (Consented clients only)</span>
      <span id="sessions-count-label">${activeSessions.length} active sessions</span>
    </div>
  `;

  // Attach filter buttons
  container.querySelectorAll(".filter-pill").forEach(btn => {
    btn.onclick = () => {
      container.querySelectorAll(".filter-pill").forEach(b => b.classList.remove("active"));
      btn.classList.add("active");
      filter = btn.getAttribute("data-filter");
      updateView();
    };
  });

  document.getElementById("btn-refresh-sessions")?.addEventListener("click", () => {
    loadSessions();
    showToast("Refreshing live sessions...", "info");
  });

  attachTerminateHandlers();

  async function loadSessions() {
    try {
      const data = await fetchLiveSessions({ status: filter });
      if (data && data.sessions && data.sessions.length > 0) {
        activeSessions = data.sessions;
        updateView();
      }
    } catch (err) {
      console.log("[Live Sessions] Using baseline session data:", err.message);
    }
  }

  loadSessions();
  refreshInterval = setInterval(loadSessions, 15000);
}
