/**
 * Central API Client for Larpiano Control Center
 * 
 * Provides unified base URL resolution, bearer authentication headers,
 * request parsing, and error handling for all dashboard API modules.
 */

export function getApiBaseUrl() {
  if (typeof window !== "undefined" && window.__LARPIANO_API_URL__) {
    return window.__LARPIANO_API_URL__;
  }
  // All environments (including serve.py proxy on 8089 and Portways production)
  // use relative origin path to prevent CORS failures and proxy through serve.py
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
    sessionStorage.removeItem("admin_token");
    localStorage.removeItem("larpiano_admin_token");
    localStorage.removeItem("admin_token");
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
  const isMutatingAdminAction = endpoint.startsWith("/api/admin/") && 
    !endpoint.includes("/auth/") && 
    options.method && 
    options.method.toUpperCase() !== "GET";

  if (isMutatingAdminAction && !token) {
    throw new Error("Administrative privileges required. Protected operations are disabled in Offline Preview.");
  }

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

export function isPreviewMode() {
  return typeof sessionStorage !== "undefined" && sessionStorage.getItem("larpiano_preview_active") === "true";
}

const syncChannel = typeof BroadcastChannel !== "undefined" ? new BroadcastChannel("larpiano_admin_sync") : null;

export function broadcastDataChange(mutationType) {
  if (syncChannel) {
    try {
      syncChannel.postMessage({ type: mutationType, timestamp: Date.now() });
    } catch (_) {}
  }
  try {
    localStorage.setItem("larpiano_last_mutation", JSON.stringify({ type: mutationType, timestamp: Date.now() }));
  } catch (_) {}
}

export function onDataChange(callback) {
  if (syncChannel) {
    syncChannel.onmessage = (event) => callback(event.data);
  }
  if (typeof window !== "undefined") {
    window.addEventListener("storage", (event) => {
      if (event.key === "larpiano_last_mutation" && event.newValue) {
        try {
          callback(JSON.parse(event.newValue));
        } catch (_) {}
      }
    });
  }
}
