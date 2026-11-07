import { apiFetch } from "./client.js";

export async function fetchAnalytics() {
  return await apiFetch("/api/admin/analytics");
}
