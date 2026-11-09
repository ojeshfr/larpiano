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
  let serverRevoked = false;
  let serverError = null;
  try {
    const res = await apiFetch("/api/admin/auth/logout", { method: "POST" });
    serverRevoked = res?.status === "ok" || res?.success === true;
  } catch (err) {
    serverRevoked = false;
    serverError = err.message || "Server unreachable";
  } finally {
    setAdminToken(null);
  }
  return { success: true, serverRevoked, error: serverError };
}

export async function getCurrentUser() {
  return await apiFetch("/api/admin/auth/me");
}
