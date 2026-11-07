/**
 * Page: Admin Playground (Experimental Opt-in Visual Effects)
 * Connected to live backend playground_events and desktop client broadcaster.
 */

import { PLAYGROUND_EFFECTS } from "../data/mockData.js";
import { runPlaygroundEffect } from "../components/playgroundRunner.js";
import { triggerPlaygroundEffect } from "../api/playground.js";
import { showToast } from "../components/toast.js";

export function renderPlayground(container) {
  container.innerHTML = `
    <!-- Header -->
    <div style="margin-bottom: var(--space-4);">
      <h1 class="page-title">Admin Playground</h1>
      <p class="page-subtitle" style="color: var(--accent-soft);">Test ephemeral visual effects and toast alerts on local consenting clients.</p>
    </div>

    <!-- Safety & Boundaries Banner -->
    <div class="info-banner" style="margin-bottom: var(--space-5);">
      <div class="info-banner-icon">🛡️</div>
      <div style="flex: 1;">
        <strong style="color: var(--text);">Fleet Telemetry &amp; Opt-in Sandbox:</strong>
        Effects triggered here preview in this browser tab and broadcast to consenting desktop clients via the PostgreSQL <span class="cell-mono font-mono" style="font-size: 11px;">playground_events</span> table. Effects auto-expire in 30 seconds and never touch game memory or system files.
      </div>
    </div>

    <!-- 6 Effect Cards Grid -->
    <div class="playground-grid">
      ${PLAYGROUND_EFFECTS.map(effect => `
        <div class="playground-card" id="card-effect-${effect.id}">
          <div class="playground-card-top">
            <div class="playground-card-icon">${effect.icon}</div>
            <div>
              <div class="playground-card-title">${effect.name}</div>
              <p class="playground-card-desc">${effect.desc}</p>
            </div>
          </div>
          <div class="playground-card-footer">
            <span class="playground-duration">Duration: ${effect.duration}</span>
            <div style="display: flex; gap: 6px;">
              <button class="btn btn-secondary btn-sm btn-preview-effect" data-id="${effect.id}">
                Preview tab
              </button>
              <button class="btn btn-primary btn-sm btn-broadcast-effect" data-id="${effect.id}" title="Broadcast effect to consenting client fleet">
                Broadcast ⚡
              </button>
            </div>
          </div>
        </div>
      `).join("")}
    </div>

    <!-- Production Safety Rules Panel -->
    <div class="card" style="margin-top: var(--space-6); background: var(--surface-soft);">
      <div class="card-header">
        <div>
          <h3 class="card-title" style="font-size: 14px;">Client Safety Rules &amp; Limits</h3>
          <p class="card-subtitle">Mandatory restrictions on in-game visual effects</p>
        </div>
      </div>
      <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 12px; font-size: 12px; color: var(--muted);">
        <div>✓ Explicit opt-in required in app settings</div>
        <div>✓ Hard expiration maximum of 30 seconds</div>
        <div>✓ Instant client-side dismissal button (Esc)</div>
        <div>✓ Rate-limited to 1 effect per 10 minutes</div>
        <div>✓ Zero remote process or keystroke capabilities</div>
        <div>✓ Audited in immutable admin logs</div>
      </div>
    </div>
  `;

  // Attach preview effect buttons
  container.querySelectorAll(".btn-preview-effect").forEach(btn => {
    btn.addEventListener("click", () => {
      const id = btn.getAttribute("data-id");
      runPlaygroundEffect(id);
    });
  });

  // Attach broadcast effect buttons
  container.querySelectorAll(".btn-broadcast-effect").forEach(btn => {
    btn.addEventListener("click", async () => {
      const id = btn.getAttribute("data-id");
      runPlaygroundEffect(id);
      try {
        await triggerPlaygroundEffect({ effectType: id, durationSeconds: 30 });
        showToast(`Effect "${id}" broadcast to connected client fleet!`, "success");
      } catch (err) {
        showToast(`Local preview running (API broadcast: ${err.message})`, "info");
      }
    });
  });
}
