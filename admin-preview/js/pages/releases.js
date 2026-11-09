import { escapeText, safeHttpsUrl } from "../components/safeText.js";
/**
 * Page: Releases & Deployment History
 * Connected to GitHub Releases API with live assets and release notes.
 */

import { openReleaseNotes, openDraftReleaseModal } from "../components/releaseModal.js";
import { showToast } from "../components/toast.js";
import { fetchReleases } from "../api/releases.js";
import { isPreviewMode } from "../api/client.js";
import { MOCK_RELEASES } from "../data/mockData.js";

export function renderReleases(container) {
  let releaseList = isPreviewMode() ? [...MOCK_RELEASES] : [];

  function renderView() {
    if (!releaseList.length) { container.textContent = "No releases available."; return; }
    const latest = releaseList[0];
    const latestVer = latest.version || latest.tag_name || "Unknown version";
    const latestDate = latest.date || (latest.published_at ? new Date(latest.published_at).toLocaleDateString() : "Date unavailable");
    const latestUrl = safeHttpsUrl(latest.githubUrl || latest.html_url) || "https://github.com/ojeshfrrr/larpiano/releases";

    container.innerHTML = `
      <!-- Header -->
      <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: var(--space-4);">
        <div>
          <h1 class="page-title">Releases</h1>
          <p class="page-subtitle">Windows x64 builds, release notes, and GitHub release assets.</p>
        </div>
        <button id="btn-draft-release-notes" class="btn btn-secondary">
          <span>+</span> Draft release note
        </button>
      </div>

      <!-- Active Release Banner -->
      <div class="card" style="border-color: rgba(167, 139, 250, 0.4); background: linear-gradient(180deg, #181525 0%, #14141B 100%); margin-bottom: var(--space-4);">
        <div style="display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: 12px;">
          <div style="display: flex; align-items: center; gap: 16px;">
            <div style="width: 44px; height: 44px; border-radius: var(--radius-md); background: var(--accent-deep); color: #fff; display: flex; align-items: center; justify-content: center; font-size: 20px;">
              🏷️
            </div>
            <div>
              <div style="display: flex; align-items: center; gap: 8px;">
                <h2 style="font-size: 20px; font-weight: 600; color: var(--text); font-family: var(--font-mono);">${escapeText(latestVer)}</h2>
                <span class="badge badge-channel-beta">${latest.prerelease ? 'Pre-release' : 'Production Build'}</span>
                <span class="badge badge-role-user">Windows x64</span>
              </div>
              <p style="font-size: 13px; color: var(--muted); margin-top: 4px;">
                Published on ${escapeText(latestDate)} · Synced with GitHub API
              </p>
            </div>
          </div>

          <div style="display: flex; gap: 8px;">
            <button id="btn-view-latest-notes" class="btn btn-secondary btn-sm">
              Release highlights
            </button>
            <button id="btn-copy-latest-url" class="btn btn-primary btn-sm" data-url="${escapeText(latestUrl)}">
              Copy GitHub URL
            </button>
          </div>
        </div>
      </div>

      <!-- Releases Table -->
      <div class="table-container">
        <table class="data-table">
          <thead>
            <tr>
              <th>Version</th>
              <th>Channel</th>
              <th>Published date</th>
              <th>Status</th>
              <th>Platform</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            ${releaseList.map((rel, index) => {
              const ver = rel.version || rel.tag_name || "v1.0.0";
              const isPrerelease = rel.prerelease || (rel.channel === "Beta");
              const pubDate = rel.date || (rel.published_at ? new Date(rel.published_at).toLocaleDateString() : "Recent");
              const statusStr = index === 0 ? "Latest Public" : "Archived";
              const url = safeHttpsUrl(rel.githubUrl || rel.html_url) || "https://github.com/ojeshfrrr/larpiano/releases";

              return `
                <tr>
                  <td><span class="cell-mono font-mono" style="font-weight: 600;">${escapeText(ver)}</span></td>
                  <td>
                    <span class="badge ${isPrerelease ? 'badge-channel-beta' : 'badge-channel-stable'}">
                      ${isPrerelease ? 'Beta' : 'Stable'}
                    </span>
                  </td>
                  <td><span class="cell-meta">${escapeText(pubDate)}</span></td>
                  <td>
                    <span class="badge ${index === 0 ? 'badge-online' : 'badge-role-user'}">
                      ${statusStr}
                    </span>
                  </td>
                  <td><span class="cell-meta">${escapeText(rel.platform || "Windows x64 Portable")}</span></td>
                  <td>
                    <div style="display: flex; gap: 6px;">
                      <button class="btn btn-ghost btn-sm btn-table-view-notes" data-ver="${escapeText(ver)}">
                        View notes
                      </button>
                      <button class="btn btn-ghost btn-sm btn-table-copy-url" data-url="${escapeText(url)}" title="Copy URL">
                        📋
                      </button>
                    </div>
                  </td>
                </tr>
              `;
            }).join("")}
          </tbody>
        </table>
      </div>

      <div style="margin-top: var(--space-3); font-size: var(--text-xs); color: var(--subtle); display: flex; justify-content: space-between;">
        <span>Connected to GitHub API repository: ojeshfrrr/larpiano</span>
        <span>${releaseList.length} releases</span>
      </div>
    `;

    document.getElementById("btn-draft-release-notes")?.addEventListener("click", openDraftReleaseModal);

    document.getElementById("btn-view-latest-notes")?.addEventListener("click", () => {
      openReleaseNotes(latest);
    });

    document.getElementById("btn-copy-latest-url")?.addEventListener("click", () => {
      navigator.clipboard?.writeText(latestUrl);
      showToast("GitHub Release URL copied to clipboard!", "success");
    });

    container.querySelectorAll(".btn-table-view-notes").forEach(btn => {
      btn.addEventListener("click", () => {
        const ver = btn.getAttribute("data-ver");
        const rel = releaseList.find(r => (r.version === ver || r.tag_name === ver)) || latest;
        openReleaseNotes(rel);
      });
    });

    container.querySelectorAll(".btn-table-copy-url").forEach(btn => {
      btn.addEventListener("click", () => {
        const u = btn.getAttribute("data-url");
        navigator.clipboard?.writeText(u);
        showToast("Release URL copied!", "success");
      });
    });
  }

  renderView();

  async function loadReleases() {
    try {
      const data = await fetchReleases();
      if (Array.isArray(data) && data.length > 0) {
        releaseList = data;
        renderView();
      } else if (!isPreviewMode()) {
        releaseList = [];
        renderView();
      }
    } catch (err) {
      if (isPreviewMode()) {
        console.log("[Releases] Sandbox preview mode:", err.message);
      } else {
        releaseList = [];
        renderView();
      }
    }
  }

  loadReleases();
}
