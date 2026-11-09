import { escapeText, safeHttpsUrl } from "./safeText.js";
/**
 * Release Notes & Draft Modal Component
 */

import { showToast } from "./toast.js";

let modalEl = null;

export function initReleaseModal() {
  modalEl = document.getElementById("release-modal");

  const closeBtn = document.getElementById("modal-rel-close-btn");
  if (closeBtn) closeBtn.addEventListener("click", closeReleaseModal);

  const doneBtn = document.getElementById("modal-rel-done-btn");
  if (doneBtn) doneBtn.addEventListener("click", closeReleaseModal);
}

export function openReleaseNotes(release) {
  if (!modalEl) initReleaseModal();
  if (!modalEl) return;

  const titleEl = document.getElementById("modal-rel-title");
  const bodyEl = document.getElementById("modal-rel-body");

  if (titleEl) titleEl.textContent = `Release Notes — ${release.version}`;
  if (bodyEl) {
    bodyEl.innerHTML = `
      <div style="display: flex; gap: 8px; margin-bottom: 14px;">
        <span class="badge ${release.channel === 'Beta' ? 'badge-channel-beta' : 'badge-channel-stable'}">${escapeText(release.channel)} Channel</span>
        <span class="badge badge-role-user">${escapeText(release.platform)}</span>
        <span style="font-size: 12px; color: var(--subtle); margin-left: auto;">Published: ${escapeText(release.date)}</span>
      </div>
      
      <div style="background: var(--surface-soft); border: 1px solid var(--border); border-radius: var(--radius-md); padding: 14px; margin-bottom: 16px;">
        <h4 style="font-size: 13px; color: var(--accent-soft); margin-bottom: 8px;">Highlights</h4>
        <p style="font-size: 13px; color: var(--text); line-height: 1.5;">${escapeText(release.notes)}</p>
      </div>

      <div style="display: flex; flex-direction: column; gap: 8px;">
        <label class="form-label">GitHub Release URL</label>
        <div style="display: flex; gap: 8px;">
          <input type="text" class="form-input" readonly value="${escapeText(safeHttpsUrl(release.githubUrl))}" style="flex: 1; font-family: var(--font-mono); font-size: 12px;">
          <button id="modal-copy-url-btn" class="btn btn-secondary">Copy</button>
        </div>
      </div>
    `;

    const copyBtn = document.getElementById("modal-copy-url-btn");
    if (copyBtn) {
      copyBtn.addEventListener("click", () => {
        navigator.clipboard.writeText(safeHttpsUrl(release.githubUrl)).then(() => {
          showToast("GitHub URL copied to clipboard", "success");
        }).catch(() => {
          showToast("URL copied: " + release.githubUrl, "info");
        });
      });
    }
  }

  modalEl.classList.add("open");
}

export function openDraftReleaseModal() {
  if (!modalEl) initReleaseModal();
  if (!modalEl) return;

  const titleEl = document.getElementById("modal-rel-title");
  const bodyEl = document.getElementById("modal-rel-body");

  if (titleEl) titleEl.textContent = "Draft Release Notes (Preview)";
  if (bodyEl) {
    bodyEl.innerHTML = `
      <div class="form-group">
        <label class="form-label">Target Semantic Version</label>
        <input type="text" class="form-input" value="v1.1.0" placeholder="e.g. v1.1.0">
      </div>
      <div class="form-group">
        <label class="form-label">Distribution Channel</label>
        <select class="form-select">
          <option>Beta (Windows x64)</option>
          <option>Stable (Windows x64)</option>
        </select>
      </div>
      <div class="form-group">
        <label class="form-label">Release Highlights & Changelog (Markdown)</label>
        <textarea class="form-textarea" placeholder="## What's Changed&#10;- Improved overlay hotkey responsiveness&#10;- Multi-client Roblox cycling polish"></textarea>
      </div>
      <div style="font-size: 11px; color: var(--subtle); margin-top: 4px;">
        Note: Creating release notes in this admin prototype is for preview only. Does not publish tags to GitHub.
      </div>
    `;
  }

  modalEl.classList.add("open");
}

export function closeReleaseModal() {
  if (modalEl) modalEl.classList.remove("open");
}
