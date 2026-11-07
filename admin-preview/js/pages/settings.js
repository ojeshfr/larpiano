/**
 * Page: Settings & Administration Preferences
 */

import { showToast } from "../components/toast.js";

export function renderSettings(container) {
  const isCompact = document.body.classList.contains("density-compact");

  container.innerHTML = `
    <!-- Header -->
    <div style="margin-bottom: var(--space-4);">
      <h1 class="page-title">Settings</h1>
      <p class="page-subtitle">Admin profile, data retention limits, and Portways API status.</p>
    </div>

    <div class="settings-grid">
      <!-- Admin Profile Card -->
      <div class="settings-card">
        <div class="settings-group-header">
          <h2 class="card-title">Admin Profile</h2>
          <p class="card-subtitle">Active administrator identity (Demo session)</p>
        </div>
        <div class="settings-row">
          <div class="settings-info">
            <div class="settings-label">Display Name</div>
            <div class="settings-desc">Visible on administrative actions and audit logs</div>
          </div>
          <div style="font-weight: 500; color: var(--text);">ojesh (Owner)</div>
        </div>
        <div class="settings-row">
          <div class="settings-info">
            <div class="settings-label">Security Role</div>
            <div class="settings-desc">Access tier for the Control Center</div>
          </div>
          <span class="badge badge-role-admin">Super Admin</span>
        </div>
        <div class="settings-row">
          <div class="settings-info">
            <div class="settings-label">Admin Session Lifetime</div>
            <div class="settings-desc">Automatic timeout for control console tokens</div>
          </div>
          <div style="color: var(--muted); font-size: 13px;">12 hours remaining</div>
        </div>
      </div>

      <!-- Appearance Card -->
      <div class="settings-card">
        <div class="settings-group-header">
          <h2 class="card-title">Appearance &amp; Density</h2>
          <p class="card-subtitle">Theme &amp; layout density preferences</p>
        </div>
        <div class="settings-row">
          <div class="settings-info">
            <div class="settings-label">Color Theme</div>
            <div class="settings-desc">Controlled near-black charcoal with restrained lavender accent</div>
          </div>
          <span class="badge badge-channel-beta">Dark Charcoal (Default)</span>
        </div>
        <div class="settings-row">
          <div class="settings-info">
            <div class="settings-label">Table &amp; Grid Density</div>
            <div class="settings-desc">Adjust vertical padding and data compactness across pages</div>
          </div>
          <div style="display: flex; gap: 6px;">
            <button id="btn-density-comfortable" class="btn btn-sm ${!isCompact ? 'btn-primary' : 'btn-secondary'}">
              Comfortable
            </button>
            <button id="btn-density-compact" class="btn btn-sm ${isCompact ? 'btn-primary' : 'btn-secondary'}">
              Compact
            </button>
          </div>
        </div>
      </div>

      <!-- Telemetry & Retention Policies -->
      <div class="settings-card">
        <div class="settings-group-header">
          <h2 class="card-title">Privacy &amp; Retention Limits</h2>
          <p class="card-subtitle">Strict boundaries on data collected from connected desktop clients</p>
        </div>
        <div class="settings-row">
          <div class="settings-info">
            <div class="settings-label">Audio &amp; Keystroke Capture</div>
            <div class="settings-desc">Zero keystroke or audio capture. Keypresses are sent via SendInput and never recorded.</div>
          </div>
          <span class="badge" style="background: rgba(85, 201, 154, 0.15); color: var(--success);">Zero Capture (Strict)</span>
        </div>
        <div class="settings-row">
          <div class="settings-info">
            <div class="settings-label">Heartbeat Retention</div>
            <div class="settings-desc">Ephemeral presence records purged automatically after 24 hours</div>
          </div>
          <div style="color: var(--muted); font-size: 13px;">24 hours</div>
        </div>
        <div class="settings-row">
          <div class="settings-info">
            <div class="settings-label">Aggregated Metrics</div>
            <div class="settings-desc">Hourly aggregate counts with zero user IDs or personal data attached</div>
          </div>
          <div style="color: var(--muted); font-size: 13px;">90 days rolling window</div>
        </div>
      </div>

      <!-- Platform Status -->
      <div class="settings-card">
        <div class="settings-group-header">
          <h2 class="card-title">Platform Services (Status Reference)</h2>
          <p class="card-subtitle">Internal service connectivity and API availability</p>
        </div>
        <div class="settings-row">
          <div class="settings-info">
            <div class="settings-label">Portways Project 83 (Song Catalog API)</div>
            <div class="settings-desc">Public online MIDI catalogue service</div>
          </div>
          <span class="badge badge-online"><span class="status-indicator-dot online"></span>Healthy</span>
        </div>
        <div class="settings-row">
          <div class="settings-info">
            <div class="settings-label">GitHub Release Check Service</div>
            <div class="settings-desc">Client update checking endpoint</div>
          </div>
          <span class="badge badge-online"><span class="status-indicator-dot online"></span>Operational</span>
        </div>
      </div>
    </div>
  `;

  // Attach density buttons
  document.getElementById("btn-density-comfortable")?.addEventListener("click", () => {
    document.body.classList.remove("density-compact");
    renderSettings(container);
    showToast("Density set to Comfortable", "info");
  });

  document.getElementById("btn-density-compact")?.addEventListener("click", () => {
    document.body.classList.add("density-compact");
    renderSettings(container);
    showToast("Density set to Compact", "info");
  });
}
