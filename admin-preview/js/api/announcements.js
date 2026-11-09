import { apiFetch, broadcastDataChange } from "./client.js";

export async function fetchAnnouncements() {
  return await apiFetch("/api/admin/announcements");
}

export async function createAnnouncement(announcementData) {
  const res = await apiFetch("/api/admin/announcements", {
    method: "POST",
    body: announcementData
  });
  broadcastDataChange("ANNOUNCEMENTS_CHANGED");
  return res;
}

export async function updateAnnouncement(id, updates) {
  const res = await apiFetch(`/api/admin/announcements/${encodeURIComponent(id)}`, {
    method: "PATCH",
    body: updates
  });
  broadcastDataChange("ANNOUNCEMENTS_CHANGED");
  return res;
}

export async function deleteAnnouncement(id) {
  const res = await apiFetch(`/api/admin/announcements/${encodeURIComponent(id)}`, {
    method: "DELETE"
  });
  broadcastDataChange("ANNOUNCEMENTS_CHANGED");
  return res;
}
