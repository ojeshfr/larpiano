import { escapeText } from "../components/safeText.js";
/**
 * Page: Announcements Management & Composer
 * Connected to live backend announcements API.
 */

import { openAnnouncementModal } from "../components/announcementModal.js";
import { showToast } from "../components/toast.js";
import { fetchAnnouncements, deleteAnnouncement, updateAnnouncement } from "../api/announcements.js";
import { isPreviewMode } from "../api/client.js";
import { MOCK_ANNOUNCEMENTS } from "../data/mockData.js";

export function renderAnnouncements(container) {
  let announcementsList = isPreviewMode() ? [...MOCK_ANNOUNCEMENTS] : [];

  function renderCards() {
    if (announcementsList.length === 0) {
      return `<div style="grid-column: 1/-1; padding: 32px; text-align: center; color: var(--subtle);">No announcements created yet. Click "+ New announcement" to broadcast an alert.</div>`;
    }

    return announcementsList.map(anc => {
      const rawStatus = (anc.status || "draft").toLowerCase();
      const statusBadge = (rawStatus === "published" || rawStatus === "active")
        ? `<span class="badge badge-online"><span class="status-indicator-dot online"></span>Active</span>`
        : (rawStatus === "scheduled"
          ? `<span class="badge badge-idle"><span class="status-indicator-dot idle"></span>Scheduled</span>`
          : (rawStatus === "draft"
            ? `<span class="badge badge-role-user">Draft</span>`
            : `<span class="badge badge-offline">Expired / Retracted</span>`));

      const audience = anc.audience || "all";
      const audienceBadge = audience === "all" || audience === "Global"
        ? `<span class="badge badge-channel-stable">Global</span>`
        : `<span class="badge badge-channel-beta">${escapeText(audience)}</span>`;

      const id = anc.id;
      const title = anc.title;
      const message = anc.message;
      const created = anc.created_at ? new Date(anc.created_at).toLocaleString() : (anc.created || "Recently");
      const expires = anc.expires_at ? new Date(anc.expires_at).toLocaleString() : (anc.expires || "No expiry");

      return `
        <div class="announcement-card" data-id="${escapeText(id)}">
          <div class="announcement-icon-badge">📢</div>
          <div class="announcement-main">
            <div class="announcement-top">
              <h3 class="announcement-title">${escapeText(title)}</h3>
              ${audienceBadge}
              ${statusBadge}
            </div>
            <p class="announcement-message">${escapeText(message)}</p>
            <div class="announcement-meta">
              <span>Created: ${escapeText(created)}</span>
              <span>Expires: ${escapeText(expires)}</span>
            </div>
          </div>
          <div style="display: flex; gap: 6px;">
            <button class="btn btn-ghost btn-sm btn-anc-retract" data-id="${escapeText(id)}" title="${rawStatus === 'published' ? 'Retract Announcement' : 'Publish Announcement'}">
              ${rawStatus === 'published' || rawStatus === 'active' ? '⏸️' : '▶️'}
            </button>
            <button class="btn btn-ghost btn-sm btn-anc-delete" data-id="${escapeText(id)}" title="Delete Announcement" style="color: var(--danger);">
              🗑️
            </button>
          </div>
        </div>
      `;
    }).join("");
  }

  function updateDOM() {
    const grid = container.querySelector("#announcements-list-container");
    if (grid) {
      grid.innerHTML = renderCards();
      attachActions();
    }
  }

  function attachActions() {
    container.querySelectorAll(".btn-anc-delete").forEach(btn => {
      btn.onclick = async () => {
        const id = btn.getAttribute("data-id");
        if (confirm("Delete this announcement?")) {
          try {
            await deleteAnnouncement(id);
            showToast("Announcement deleted", "success");
            loadAnnouncements();
          } catch (err) {
            showToast(`Could not delete: ${err.message}`, "error");
          }
        }
      };
    });

    container.querySelectorAll(".btn-anc-retract").forEach(btn => {
      btn.onclick = async () => {
        const id = btn.getAttribute("data-id");
        const target = announcementsList.find(a => a.id === id);
        const newStatus = (target && (target.status === "published" || target.status === "Active")) ? "retracted" : "published";
        try {
          await updateAnnouncement(id, { status: newStatus });
          showToast(`Announcement marked as ${newStatus}`, "success");
          loadAnnouncements();
        } catch (err) {
          showToast(`Could not update: ${err.message}`, "error");
        }
      };
    });
  }

  container.innerHTML = `
    <!-- Header -->
    <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: var(--space-4);">
      <div>
        <h1 class="page-title">Announcements</h1>
        <p class="page-subtitle">Broadcast in-app release alerts and maintenance notices to connected clients.</p>
      </div>
      <button id="btn-create-announcement" class="btn btn-primary">
        <span>+</span> New announcement
      </button>
    </div>

    <!-- Active List -->
    <div class="announcements-grid" id="announcements-list-container">
      ${renderCards()}
    </div>
  `;

  document.getElementById("btn-create-announcement")?.addEventListener("click", () => {
    openAnnouncementModal(loadAnnouncements);
  });

  attachActions();

  async function loadAnnouncements() {
    try {
      const data = await fetchAnnouncements();
      if (Array.isArray(data)) {
        announcementsList = data;
        updateDOM();
      }
    } catch (err) {
      if (isPreviewMode()) {
        console.log("[Announcements] Sandbox preview mode:", err.message);
      } else {
        announcementsList = [];
        updateDOM();
      }
    }
  }

  loadAnnouncements();
}
