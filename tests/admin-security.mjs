import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
const source = await readFile(new URL("../admin-preview/js/components/safeText.js", import.meta.url), "utf8");
const { escapeText, safeHttpsUrl } = await import(`data:text/javascript;base64,${Buffer.from(source).toString("base64")}`);
for (const payload of [
  '<script>alert(1)</script>',
  '<img src=x onerror=alert(1)>',
  '<svg onload=alert(1)>',
  '" autofocus onfocus="alert(1)',
  { injected: '<script>alert(1)</script>' },
  ['<svg onload=alert(1)>']
]) {
  const encoded = escapeText(payload);
  assert(!/[<>"']/.test(encoded), `Unsafe output: ${encoded}`);
}
for (const url of ['javascript:alert(1)', 'data:text/html,<script>alert(1)</script>', 'https://user:pass@example.com/', {}, []]) {
  assert.equal(safeHttpsUrl(url), '');
}
assert.equal(safeHttpsUrl('https://example.com/a'), 'https://example.com/a');
console.log('Text encoding and URL checks passed');

// --- Sign-out and Token Management Regression Tests ---

// Mock browser storage & fetch environment
const mockStorage = new Map();
globalThis.sessionStorage = {
  getItem: (k) => mockStorage.get(k) || null,
  setItem: (k, v) => mockStorage.set(k, String(v)),
  removeItem: (k) => mockStorage.delete(k),
  clear: () => mockStorage.clear(),
};
globalThis.localStorage = {
  getItem: (k) => mockStorage.get(k) || null,
  setItem: (k, v) => mockStorage.set(k, String(v)),
  removeItem: (k) => mockStorage.delete(k),
  clear: () => mockStorage.clear(),
};
globalThis.window = { location: { port: "", protocol: "https:", hostname: "larpiano.portways.app" } };

const clientSource = await readFile(new URL("../admin-preview/js/api/client.js", import.meta.url), "utf8");
const { getAdminToken, setAdminToken, apiFetch } = await import(`data:text/javascript;base64,${Buffer.from(clientSource).toString("base64")}`);

const authSource = await readFile(new URL("../admin-preview/js/api/auth.js", import.meta.url), "utf8");
// Provide client.js exports into auth module via a mock data URL
const patchedAuth = authSource.replace('import { apiFetch, setAdminToken } from "./client.js";', 'const { apiFetch, setAdminToken } = globalThis.__TEST_CLIENT__;');
globalThis.__TEST_CLIENT__ = { apiFetch, setAdminToken };
const { logout } = await import(`data:text/javascript;base64,${Buffer.from(patchedAuth).toString("base64")}`);

// 1. Token storage and clearing
setAdminToken("admin-secret-token-12345");
assert.equal(getAdminToken(), "admin-secret-token-12345");
setAdminToken(null);
assert.equal(getAdminToken(), "");

// 2. Successful server-side sign out clears local token and reports success
setAdminToken("active-token-999");
globalThis.fetch = async (url, opts) => {
  assert(url.endsWith("/api/admin/auth/logout"));
  assert.equal(opts.method, "POST");
  assert.equal(opts.headers.Authorization, "Bearer active-token-999");
  return { ok: true, status: 200, json: async () => ({ status: "ok" }) };
};

const res1 = await logout();
assert.equal(res1.success, true);
assert.equal(res1.serverRevoked, true);
assert.equal(getAdminToken(), "", "Token must be removed from storage on successful logout");

// 3. Network failure / backend offline sign out STILL clears local token
setAdminToken("offline-token-888");
globalThis.fetch = async () => {
  throw new Error("Failed to fetch (net::ERR_CONNECTION_REFUSED)");
};

const res2 = await logout();
assert.equal(res2.success, true, "Local sign-out must succeed even when backend is offline");
assert.equal(res2.serverRevoked, false);
assert.equal(getAdminToken(), "", "Token must be removed from storage even on network failure");

// 4. Server error (HTTP 500) on sign out STILL clears local token
setAdminToken("error-token-777");
globalThis.fetch = async () => {
  return { ok: false, status: 500, statusText: "Internal Server Error", json: async () => ({ error: "Database down" }) };
};

const res3 = await logout();
assert.equal(res3.success, true);
assert.equal(res3.serverRevoked, false);
assert.equal(getAdminToken(), "", "Token must be removed from storage on 500 server error");

// 5. Double sign out executes safely and idempotently without throwing
const res4 = await logout();
assert.equal(res4.success, true);
assert.equal(getAdminToken(), "");

console.log('All admin authentication & sign-out regression tests passed');
process.exit(0);
