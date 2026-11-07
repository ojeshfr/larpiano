import { apiFetch, setAdminToken } from "./client.js";

export async function login(credentials) {
  const data = await apiFetch("/api/admin/auth/login", {
    method: "POST",
    body: credentials
  });
  if (data.token) {
    setAdminToken(data.token);
  }
  return data;
}

export async function logout() {
  try {
    await apiFetch("/api/admin/auth/logout", { method: "POST" });
  } finally {
    setAdminToken(null);
  }
}

export async function getCurrentUser() {
  return await apiFetch("/api/admin/auth/me");
}
