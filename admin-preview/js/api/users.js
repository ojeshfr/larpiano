import { apiFetch, broadcastDataChange } from "./client.js";

export async function fetchUsers({ search = "", status = "", role = "", limit = 50, offset = 0 } = {}) {
  const params = new URLSearchParams();
  if (search) params.set("search", search);
  if (status && status !== "all") params.set("status", status);
  if (role && role !== "all") params.set("role", role);
  if (limit) params.set("limit", limit.toString());
  if (offset) params.set("offset", offset.toString());

  const qs = params.toString();
  return await apiFetch(`/api/admin/users${qs ? `?${qs}` : ""}`);
}

export async function createUser(userData) {
  const res = await apiFetch("/api/admin/users", {
    method: "POST",
    body: userData
  });
  broadcastDataChange("USERS_CHANGED");
  return res;
}

export async function updateUser(userId, updates) {
  const res = await apiFetch(`/api/admin/users/${encodeURIComponent(userId)}`, {
    method: "PATCH",
    body: updates
  });
  broadcastDataChange("USERS_CHANGED");
  return res;
}
