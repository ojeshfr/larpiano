/**
 * Central API Client for Larpiano Control Center
 * 
 * Provides unified base URL resolution, bearer authentication headers,
 * request parsing, and error handling for all dashboard API modules.
 */

const DEFAULT_LOCAL_BACKEND = "http://127.0.0.1:8088";

export function getApiBaseUrl() {
  // If viewing via local preview server (port 8089), route requests to local Python API server
  if (window.location.port === "8089") {
    return DEFAULT_LOCAL_BACKEND;
  }
  
  // Production / Portways: same origin relative path
  return "";
}

export function getAdminToken() {
  return sessionStorage.getItem("larpiano_admin_token") || "";
}

export function setAdminToken(token) {
  if (token) {
    sessionStorage.setItem("larpiano_admin_token", token);
  } else {
    sessionStorage.removeItem("larpiano_admin_token");
  }
}

export async function apiFetch(endpoint, options = {}) {
  const base = getApiBaseUrl();
  const url = `${base}${endpoint.startsWith("/") ? "" : "/"}${endpoint}`;
  
  const headers = {
    "Accept": "application/json",
    ...(options.headers || {})
  };

  const token = getAdminToken();
  if (token && !headers["Authorization"]) {
    headers["Authorization"] = `Bearer ${token}`;
  }

  if (options.body && typeof options.body === "object" && !(options.body instanceof FormData)) {
    headers["Content-Type"] = "application/json";
    options.body = JSON.stringify(options.body);
  }

  try {
    const response = await fetch(url, {
      ...options,
      headers
    });

    if (!response.ok) {
      let errMsg = `HTTP ${response.status} ${response.statusText}`;
      try {
        const errJson = await response.json();
        if (errJson.error) errMsg = errJson.error;
      } catch (_) {}
      
      const error = new Error(errMsg);
      error.status = response.status;
      throw error;
    }

    // Handle 204 No Content
    if (response.status === 204) {
      return null;
    }

    return await response.json();
  } catch (err) {
    console.warn(`[Larpiano API] Request to ${endpoint} failed:`, err.message);
    throw err;
  }
}
