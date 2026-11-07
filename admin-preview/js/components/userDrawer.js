/**
 * User Detail Slide-Over Drawer
 */

import { showToast } from "./toast.js";

let drawerEl = null;
let backdropEl = null;

export function initUserDrawer() {
  drawerEl = document.getElementById("user-drawer");
  backdropEl = document.getElementById("drawer-backdrop");

  if (backdropEl) {
    backdropEl.addEventListener("click", closeUserDrawer);
  }

  const closeBtn = document.getElementById("drawer-close-btn");
  if (closeBtn) {
    closeBtn.addEventListener("click", closeUserDrawer);
  }
}

export function openUserDrawer(user) {
  if (!drawerEl) initUserDrawer();
  if (!drawerEl) return;

  const contentEl = document.getElementById("drawer-user-content");
  if (!contentEl) return;

  const statusBadgeClass = user.status === "Online" ? "badge-online" : (user.status === "Idle" ? "badge-idle" : "badge-offline");

  const sessionsHtml = user.activeSessions && user.activeSessions.length > 0
    ? user.activeSessions.map(s => `
        <div style="background: var(--surface-soft); padding: 8px 12px; border-radius: var(--radius-sm); border: 1px solid var(--border); margin-bottom: 6px;">
          <div style="display: flex; justify-content: space-between; font-size: 12px; margin-bottom: 2px;">
            <span style="font-family: var(--font-mono); color: var(--accent-soft);">${s.id}</span>
            <span class="badge ${s.status === 'Playing' ? 'badge-playing' : 'badge-online'}">${s.status}</span>
          </div>
          <div style="font-size: 11px; color: var(--subtle); display: flex; justify-content: space-between;">
            <span>${s.client}</span>
            <span>Uptime: ${s.duration}</span>
          </div>
        </div>
      `).join("")
    : `<div style="font-size: 12px; color: var(--subtle); font-style: italic;">No active sessions currently running.</div>`;

  contentEl.innerHTML = `
    <!-- User Identity -->
    <div style="display: flex; align-items: center; gap: 14px; margin-bottom: var(--space-4);">
      <div style="width: 48px; height: 48px; border-radius: 50%; background: linear-gradient(135deg, #7C3AED, #A78BFA); display: flex; align-items: center; justify-content: center; font-size: 20px; font-weight: 600; color: #fff;">
        ${user.name.charAt(0).toUpperCase()}
      </div>
      <div>
        <div style="display: flex; align-items: center; gap: 8px;">
          <h3 style="font-size: 18px; font-weight: 600; color: var(--text);">${user.name}</h3>
          <span class="badge ${statusBadgeClass}">${user.status}</span>
        </div>
        <div style="font-family: var(--font-mono); font-size: 11px; color: var(--subtle); margin-top: 2px;">ID: ${user.id}</div>
      </div>
    </div>

    <!-- Account Details Group -->
    <div>
      <div class="drawer-section-title">Account Information</div>
      <div class="drawer-prop-row">
        <span class="drawer-prop-label">Role</span>
        <span class="drawer-prop-val">${user.role}</span>
      </div>
      <div class="drawer-prop-row">
        <span class="drawer-prop-label">App Version</span>
        <span class="drawer-prop-val font-mono">${user.version}</span>
      </div>
      <div class="drawer-prop-row">
        <span class="drawer-prop-label">First Seen</span>
        <span class="drawer-prop-val">${user.firstSeen}</span>
      </div>
      <div class="drawer-prop-row">
        <span class="drawer-prop-label">Last Active</span>
        <span class="drawer-prop-val">${user.lastSeen}</span>
      </div>
    </div>

    <!-- Telemetry & Consent Group -->
    <div>
      <div class="drawer-section-title">Privacy & Consent (Demo)</div>
      <div style="background: var(--surface-soft); border: 1px solid var(--border); border-radius: var(--radius-md); padding: 10px 12px; font-size: 12px;">
        <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 6px;">
          <span style="color: var(--muted);">Diagnostic crash reports</span>
          <span style="color: ${user.consentTelemetry ? 'var(--success)' : 'var(--subtle)'}; font-weight: 500;">
            ${user.consentTelemetry ? '✓ Consented' : '✕ Opted out'}
          </span>
        </div>
        <div style="display: flex; align-items: center; justify-content: space-between;">
          <span style="color: var(--muted);">In-app playground effects</span>
          <span style="color: ${user.consentEffects ? 'var(--success)' : 'var(--subtle)'}; font-weight: 500;">
            ${user.consentEffects ? '✓ Opted in' : '✕ Opted out'}
          </span>
        </div>
      </div>
    </div>

    <!-- Sessions Group -->
    <div>
      <div class="drawer-section-title">Active Consented Sessions (${user.activeSessions ? user.activeSessions.length : 0})</div>
      ${sessionsHtml}
    </div>

    <!-- Administrative Actions Demo -->
    <div style="margin-top: auto; padding-top: var(--space-4);">
      <div class="drawer-section-title">Administrative Actions</div>
      <div style="display: flex; flex-direction: column; gap: 8px;">
        <button id="btn-demo-suspend" class="btn btn-secondary" style="width: 100%; justify-content: flex-start;">
          <span>🚫</span> Suspend server access (Preview only)
        </button>
        <button id="btn-demo-revoke" class="btn btn-secondary" style="width: 100%; justify-content: flex-start;">
          <span>🔌</span> Revoke active session (Preview only)
        </button>
      </div>
    </div>
  `;

  // Attach action button clicks
  const suspendBtn = document.getElementById("btn-demo-suspend");
  if (suspendBtn) {
    suspendBtn.addEventListener("click", () => {
      showToast("Preview only: Account suspension is not connected to production database.", "warning");
    });
  }

  const revokeBtn = document.getElementById("btn-demo-revoke");
  if (revokeBtn) {
    revokeBtn.addEventListener("click", () => {
      showToast("Preview only: Session revocation is not connected to live clients.", "warning");
    });
  }

  drawerEl.classList.add("open");
  if (backdropEl) backdropEl.classList.add("visible");
}

export function closeUserDrawer() {
  if (drawerEl) drawerEl.classList.remove("open");
  if (backdropEl) backdropEl.classList.remove("visible");
}
