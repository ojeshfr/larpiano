import { apiFetch } from "./client.js";

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
  return await apiFetch("/api/admin/users", {
    method: "POST",
    body: userData
  });
}

export async function updateUser(userId, updates) {
  return await apiFetch(`/api/admin/users/${encodeURIComponent(userId)}`, {
    method: "PATCH",
    body: updates
  });
}
