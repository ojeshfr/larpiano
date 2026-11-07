import { apiFetch } from "./client.js";

export async function fetchAuditLogs({ limit = 50, offset = 0 } = {}) {
  const params = new URLSearchParams();
  if (limit) params.set("limit", limit.toString());
  if (offset) params.set("offset", offset.toString());

  const qs = params.toString();
  return await apiFetch(`/api/admin/audit${qs ? `?${qs}` : ""}`);
}
