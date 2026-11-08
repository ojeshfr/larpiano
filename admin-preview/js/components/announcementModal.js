/**
 * Announcement Composer Modal & Live Preview
 * Connected to PostgreSQL announcements table and real client broadcast.
 */

import { showToast } from "./toast.js";
import { createAnnouncement } from "../api/announcements.js";

let modalEl = null;
let onUpdateCallback = null;

export function initAnnouncementModal(callback) {
  onUpdateCallback = callback;
  modalEl = document.getElementById("announcement-modal");

  const closeBtn = document.getElementById("modal-anc-close-btn");
  if (closeBtn) closeBtn.addEventListener("click", closeAnnouncementModal);

  const cancelBtn = document.getElementById("modal-anc-cancel-btn");
  if (cancelBtn) cancelBtn.addEventListener("click", closeAnnouncementModal);

  const saveDraftBtn = document.getElementById("modal-anc-save-draft");
  if (saveDraftBtn) {
    saveDraftBtn.addEventListener("click", () => handleSaveAnnouncement("Draft"));
  }

  const publishBtn = document.getElementById("modal-anc-publish");
  if (publishBtn) {
    publishBtn.addEventListener("click", () => handleSaveAnnouncement("Active"));
  }

  // Real-time Live Preview listeners
  const titleInput = document.getElementById("anc-input-title");
  const msgInput = document.getElementById("anc-input-msg");
  const audSelect = document.getElementById("anc-select-audience");

  const updatePreview = () => {
    const previewTitle = document.getElementById("preview-anc-title");
    const previewMsg = document.getElementById("preview-anc-msg");
    const previewAud = document.getElementById("preview-anc-aud");

    if (previewTitle) previewTitle.textContent = titleInput.value.trim() || "Announcement Title";
    if (previewMsg) previewMsg.textContent = msgInput.value.trim() || "Announcement message content will appear here...";
    if (previewAud) previewAud.textContent = audSelect.value;
  };

  if (titleInput) titleInput.addEventListener("input", updatePreview);
  if (msgInput) msgInput.addEventListener("input", updatePreview);
  if (audSelect) audSelect.addEventListener("change", updatePreview);
}

export function openAnnouncementModal() {
  if (!modalEl) initAnnouncementModal();
  if (modalEl) modalEl.classList.add("open");
}

export function closeAnnouncementModal() {
  if (modalEl) modalEl.classList.remove("open");
}

async function handleSaveAnnouncement(status) {
  const titleInput = document.getElementById("anc-input-title");
  const msgInput = document.getElementById("anc-input-msg");
  const audSelect = document.getElementById("anc-select-audience");

  const title = titleInput.value.trim();
  const message = msgInput.value.trim();
  const audience = audSelect.value;

  if (!title) {
    showToast("Please enter an announcement title.", "warning");
    return;
  }

  // Persist to database via API
  try {
    const dbStatus = status === "Active" ? "published" : "draft";
    await createAnnouncement({
      title,
      message: message || "No message content provided.",
      audience,
      status: dbStatus
    });
    showToast(`Announcement published and broadcast to desktop clients!`, "success");
  } catch (err) {
    showToast(`Could not save announcement: ${err.message}`, "error");
    return;
  }

  closeAnnouncementModal();

  // Reset fields
  titleInput.value = "";
  msgInput.value = "";

  if (onUpdateCallback) onUpdateCallback();
}
