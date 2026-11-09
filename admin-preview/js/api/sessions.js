import { apiFetch, broadcastDataChange } from "./client.js";

export async function fetchLiveSessions({ status = "all", limit = 50, offset = 0 } = {}) {
  const params = new URLSearchParams();
  if (status && status !== "all") params.set("status", status);
  if (limit) params.set("limit", limit.toString());
  if (offset) params.set("offset", offset.toString());

  const qs = params.toString();
  return await apiFetch(`/api/admin/sessions${qs ? `?${qs}` : ""}`);
}

export async function terminateSession(sessionId) {
  const res = await apiFetch(`/api/admin/sessions/${encodeURIComponent(sessionId)}/terminate`, {
    method: "POST"
  });
  broadcastDataChange("SESSIONS_CHANGED");
  return res;
}
