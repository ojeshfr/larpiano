import { apiFetch } from "./client.js";

export async function fetchAnalytics(range = "7d") {
  return await apiFetch(`/api/admin/analytics?range=${encodeURIComponent(range)}`);
}
