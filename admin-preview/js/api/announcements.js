import { apiFetch } from "./client.js";

export async function fetchAnnouncements() {
  return await apiFetch("/api/admin/announcements");
}

export async function createAnnouncement(announcementData) {
  return await apiFetch("/api/admin/announcements", {
    method: "POST",
    body: announcementData
  });
}

export async function updateAnnouncement(id, updates) {
  return await apiFetch(`/api/admin/announcements/${encodeURIComponent(id)}`, {
    method: "PATCH",
    body: updates
  });
}

export async function deleteAnnouncement(id) {
  return await apiFetch(`/api/admin/announcements/${encodeURIComponent(id)}`, {
    method: "DELETE"
  });
}
