import { fetchOverview } from "../api/overview.js";

let refreshTimer;

export function renderOverview(container) {
  if (refreshTimer) clearInterval(refreshTimer);
  container.replaceChildren();
  const title = document.createElement("h1");
  title.className = "page-title";
  title.textContent = "Control Center";
  const content = document.createElement("div");
  content.textContent = "Loading overview…";
  container.append(title, content);

  async function load() {
    try {
      const data = await fetchOverview();
      if (!container.isConnected) { clearInterval(refreshTimer); return; }
      content.replaceChildren();
      const metrics = document.createElement("p");
      const m = data.metrics || {};
      metrics.textContent = `Online: ${Number(m.onlineNow) || 0} · Accounts: ${Number(m.totalAccounts) || 0} · Active sessions: ${Number(m.activeSessions) || 0}`;
      content.append(metrics);
      const history = document.createElement("p");
      history.textContent = "Historical activity is not available yet.";
      content.append(history);
      const heading = document.createElement("h2");
      heading.textContent = "Active sessions";
      content.append(heading);
      const players = Array.isArray(data.active_players) ? data.active_players : [];
      if (!players.length) {
        const empty = document.createElement("p"); empty.textContent = "No active sessions."; content.append(empty);
      }
      for (const player of players) {
        const row = document.createElement("p");
        row.textContent = `${player.display_name || "Unknown"} · ${player.status || "unknown"} · ${player.app_version || "version unavailable"}`;
        content.append(row);
      }
      const eventsHeading = document.createElement("h2"); eventsHeading.textContent = "Recent audit events"; content.append(eventsHeading);
      const events = Array.isArray(data.recent_events) ? data.recent_events : [];
      if (!events.length) { const empty = document.createElement("p"); empty.textContent = "No audit events."; content.append(empty); }
      for (const event of events) {
        const row = document.createElement("p"); row.textContent = `${event.action || "Event"} · ${event.target || ""} · ${event.created_at || ""}`; content.append(row);
      }
    } catch (error) {
      content.textContent = `Could not load the overview: ${error.message}. Try again.`;
    }
  }
  load();
  refreshTimer = setInterval(load, 20000);
}
