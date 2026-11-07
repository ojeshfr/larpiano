import { apiFetch } from "./client.js";

export async function fetchReleases() {
  return await apiFetch("/api/admin/releases");
}
