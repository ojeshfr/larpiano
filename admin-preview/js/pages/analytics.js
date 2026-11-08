import { fetchAnalytics } from "../api/analytics.js";
export function renderAnalytics(container) {
  container.replaceChildren();
  const title = document.createElement("h1"); title.textContent = "Analytics"; title.className = "page-title";
  const body = document.createElement("p"); body.textContent = "Loading analytics…";
  container.append(title, body);
  fetchAnalytics().then(m => {
    body.textContent = `Online now: ${Number(m.onlineNow) || 0} · Accounts: ${Number(m.totalAccounts) || 0}. Historical analytics are not available yet.`;
  }).catch(e => { body.textContent = `Could not load analytics: ${e.message}. Try again.`; });
}
