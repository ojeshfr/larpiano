import { apiFetch } from "./client.js";

export async function fetchOverview() {
  return await apiFetch("/api/admin/overview");
}
