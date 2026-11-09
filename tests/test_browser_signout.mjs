import { spawn } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import assert from "node:assert/strict";

const CHROME_PATH = "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe";
const PORT = 9226;
const tempProfile = fs.mkdtempSync(path.join(os.tmpdir(), "chrome_test_profile_"));

const chromeProc = spawn(CHROME_PATH, [
  "--headless=new",
  `--remote-debugging-port=${PORT}`,
  `--user-data-dir=${tempProfile}`,
  "--no-first-run",
  "--no-default-browser-check",
  "about:blank",
]);

async function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

let ws;
let msgId = 1;
const callbacks = new Map();

function sendCommand(method, params = {}) {
  const id = msgId++;
  return new Promise((resolve, reject) => {
    callbacks.set(id, { resolve, reject });
    ws.send(JSON.stringify({ id, method, params }));
  });
}

try {
  let wsUrl = null;
  for (let i = 0; i < 30; i++) {
    try {
      const res = await fetch(`http://127.0.0.1:${PORT}/json/version`);
      if (res.ok) {
        const json = await res.json();
        wsUrl = json.webSocketDebuggerUrl;
        break;
      }
    } catch (_) {}
    await sleep(200);
  }

  assert(wsUrl, "Could not obtain Chrome WebSocket debugger URL");

  const targetRes = await fetch(`http://127.0.0.1:${PORT}/json/new?about:blank`, { method: "PUT" });
  const targetJson = await targetRes.json();
  const pageWsUrl = targetJson.webSocketDebuggerUrl;

  ws = new WebSocket(pageWsUrl);
  await new Promise((resolve, reject) => {
    ws.onopen = resolve;
    ws.onerror = reject;
    ws.onmessage = (event) => {
      const data = JSON.parse(event.data);
      if (data.id && callbacks.has(data.id)) {
        const { resolve, reject } = callbacks.get(data.id);
        callbacks.delete(data.id);
        if (data.error) reject(new Error(data.error.message));
        else resolve(data.result);
      }
    };
  });

  await sendCommand("Page.enable");
  await sendCommand("Runtime.enable");

  // Navigate to application
  console.log("Navigating to http://localhost:8089/#/overview...");
  await sendCommand("Page.navigate", { url: "http://localhost:8089/#/overview" });
  await sleep(2000);

  // 1. Initial State: Unauthenticated user should see login gate immediately
  const initialGateCheck = await sendCommand("Runtime.evaluate", {
    expression: `
      ({
        gateExists: !!document.querySelector(".admin-login-gate"),
        profileName: document.querySelector(".profile-name")?.textContent
      })
    `,
    returnByValue: true
  });
  console.log("1. Initial Unauthenticated Load:", initialGateCheck.result.value);
  assert.equal(initialGateCheck.result.value.gateExists, true, "Login gate must be visible on initial unauthenticated load");

  // 2. Set an authenticated token and reload to verify authenticated session
  console.log("2. Simulating login with token...");
  await sendCommand("Runtime.evaluate", {
    expression: `
      sessionStorage.setItem("larpiano_admin_token", "adm-session-token-live");
    `,
  });
  await sendCommand("Page.reload");
  await sleep(1500);

  const authState = await sendCommand("Runtime.evaluate", {
    expression: `
      ({
        token: sessionStorage.getItem("larpiano_admin_token"),
        profileName: document.querySelector(".profile-name")?.textContent
      })
    `,
    returnByValue: true
  });
  console.log("2. Authenticated State:", authState.result.value);

  // 3. Perform Sign Out
  console.log("3. Clicking Sign Out...");
  const signoutRes = await sendCommand("Runtime.evaluate", {
    expression: `
      (async () => {
        const btn = document.getElementById("profile-action-signout");
        btn.click();
        await new Promise(r => setTimeout(r, 1000));
        return {
          tokenInSession: sessionStorage.getItem("larpiano_admin_token"),
          tokenInLocal: localStorage.getItem("larpiano_admin_token"),
          gateVisible: !!document.querySelector(".admin-login-gate"),
          gateNotice: document.querySelector(".admin-login-notice")?.textContent,
          profileName: document.querySelector(".profile-name")?.textContent
        };
      })()
    `,
    awaitPromise: true,
    returnByValue: true,
  });
  console.log("3. Post Sign-Out State:", signoutRes.result.value);
  assert.equal(signoutRes.result.value.tokenInSession, null, "sessionStorage token must be purged");
  assert.equal(signoutRes.result.value.tokenInLocal, null, "localStorage token must be purged");
  assert.equal(signoutRes.result.value.gateVisible, true, "Login gate must be shown after sign out");

  // 4. Test page refresh after sign-out: Must NEVER automatically restore dashboard!
  console.log("4. Testing page refresh after sign out...");
  await sendCommand("Page.reload");
  await sleep(1500);

  const postRefreshCheck = await sendCommand("Runtime.evaluate", {
    expression: `
      ({
        token: sessionStorage.getItem("larpiano_admin_token"),
        gateVisible: !!document.querySelector(".admin-login-gate"),
        profileName: document.querySelector(".profile-name")?.textContent
      })
    `,
    returnByValue: true
  });
  console.log("4. Post-Refresh Check:", postRefreshCheck.result.value);
  assert.equal(postRefreshCheck.result.value.token, null, "Token must remain null after refresh");
  assert.equal(postRefreshCheck.result.value.gateVisible, true, "Login gate MUST be visible after refresh; cannot auto-restore dashboard");

  // 5. Test Offline Preview Mode
  console.log("5. Testing Offline Preview mode...");
  const previewRes = await sendCommand("Runtime.evaluate", {
    expression: `
      (async () => {
        const guestBtn = document.getElementById("admin-guest-preview-btn");
        guestBtn.click();
        await new Promise(r => setTimeout(r, 500));
        return {
          gateGone: !document.querySelector(".admin-login-gate"),
          bannerTag: document.querySelector(".demo-tag")?.textContent,
          bannerText: document.querySelector(".demo-banner-text")?.textContent,
          profileName: document.querySelector(".profile-name")?.textContent,
          profileRole: document.querySelector(".profile-role")?.textContent,
          token: sessionStorage.getItem("larpiano_admin_token")
        };
      })()
    `,
    awaitPromise: true,
    returnByValue: true,
  });
  console.log("5. Offline Preview State:", previewRes.result.value);
  assert.equal(previewRes.result.value.gateGone, true, "Gate dismissed on preview click");
  assert.equal(previewRes.result.value.bannerTag, "OFFLINE PREVIEW", "Banner tag explicitly labelled OFFLINE PREVIEW");
  assert.equal(previewRes.result.value.profileName, "Preview Guest", "Profile name explicitly Preview Guest");
  assert.equal(previewRes.result.value.token, null, "Preview mode has zero tokens");

  // 6. Verify Offline Preview CANNOT execute protected operations
  console.log("6. Verifying Offline Preview blocks protected mutating API operations...");
  const operationBlockCheck = await sendCommand("Runtime.evaluate", {
    expression: `
      (async () => {
        try {
          const client = await import("./js/api/client.js");
          await client.apiFetch("/api/admin/announcements", {
            method: "POST",
            body: { title: "Malicious", message: "Payload" }
          });
          return { blocked: false };
        } catch (err) {
          return { blocked: true, error: err.message };
        }
      })()
    `,
    awaitPromise: true,
    returnByValue: true,
  });
  console.log("6. Protected Operation Result:", operationBlockCheck.result.value);
  assert.equal(operationBlockCheck.result.value.blocked, true, "Mutating operation must be blocked");

  console.log("ALL BROWSER AUTHENTICATION & SIGN-OUT VERIFICATION CHECKS PASSED!");
} finally {
  if (ws) ws.close();
  chromeProc.kill();
  try {
    fs.rmSync(tempProfile, { recursive: true, force: true });
  } catch (_) {}
}
