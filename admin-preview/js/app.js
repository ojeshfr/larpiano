/**
 * LARPIANO CONTROL CENTER — APPLICATION ENTRY POINT & ROUTER
 */

import { renderOverview } from "./pages/overview.js";
import { renderUsers } from "./pages/users.js";
import { renderLiveSessions } from "./pages/sessions.js";
import { renderAnalytics } from "./pages/analytics.js";
import { renderAnnouncements } from "./pages/announcements.js";
import { renderReleases } from "./pages/releases.js";
import { renderPlayground } from "./pages/playground.js";
import { renderAuditLog } from "./pages/audit.js";
import { renderSettings } from "./pages/settings.js";

import { initUserDrawer, openUserDrawer } from "./components/userDrawer.js";
import { initAnnouncementModal, openAnnouncementModal } from "./components/announcementModal.js";
import { initReleaseModal, openDraftReleaseModal } from "./components/releaseModal.js";
import { initSearchModal, openSearchModal } from "./components/searchModal.js";
import { showToast } from "./components/toast.js";
import { login, logout, getCurrentUser } from "./api/auth.js";
import { getAdminToken, setAdminToken, getApiBaseUrl, onDataChange, isPreviewMode } from "./api/client.js";
import { escapeText } from "./components/safeText.js";

let currentRoute = "overview";

const PAGE_TITLES = {
  overview: "Overview",
  users: "Users",
  sessions: "Live Sessions",
  analytics: "Analytics",
  announcements: "Announcements",
  releases: "Releases",
  playground: "Admin Playground",
  audit: "Audit Log",
  settings: "Settings"
};

export function navigateTo(route) {
  if (!PAGE_TITLES[route]) route = "overview";
  currentRoute = route;

  // Update URL hash without reload
  window.location.hash = `#/${route}`;

  // Update Breadcrumb
  const breadcrumb = document.getElementById("header-breadcrumb");
  if (breadcrumb) {
    breadcrumb.textContent = PAGE_TITLES[route];
  }

  // Update Nav links active state
  document.querySelectorAll(".nav-item").forEach(item => {
    if (item.getAttribute("data-route") === route) {
      item.classList.add("active");
    } else {
      item.classList.remove("active");
    }
  });

  // Render view
  const viewport = document.getElementById("page-viewport");
  if (!viewport) return;

  viewport.scrollTop = 0;

  switch (route) {
    case "overview":
      renderOverview(viewport, {
        onNavigate: navigateTo,
        onOpenAnnouncement: openAnnouncementModal,
        onDraftRelease: openDraftReleaseModal
      });
      break;

    case "users":
      renderUsers(viewport);
      break;

    case "sessions":
      renderLiveSessions(viewport);
      break;

    case "analytics":
      renderAnalytics(viewport);
      break;

    case "announcements":
      renderAnnouncements(viewport);
      break;

    case "releases":
      renderReleases(viewport);
      break;

    case "playground":
      renderPlayground(viewport);
      break;

    case "audit":
      renderAuditLog(viewport);
      break;

    case "settings":
      renderSettings(viewport);
      break;
  }

  // Close mobile sidebar if open
  closeMobileSidebar();
}

function initSidebar() {
  document.querySelectorAll(".nav-item").forEach(item => {
    item.addEventListener("click", (e) => {
      e.preventDefault();
      const route = item.getAttribute("data-route");
      navigateTo(route);
    });
  });

  // Mobile toggle
  const toggleBtn = document.getElementById("mobile-menu-toggle");
  const sidebar = document.getElementById("app-sidebar");
  const backdrop = document.getElementById("app-backdrop");

  if (toggleBtn && sidebar && backdrop) {
    toggleBtn.addEventListener("click", () => {
      sidebar.classList.toggle("open");
      backdrop.classList.toggle("visible");
    });

    backdrop.addEventListener("click", () => {
      closeMobileSidebar();
    });
  }
}

function closeMobileSidebar() {
  const sidebar = document.getElementById("app-sidebar");
  const backdrop = document.getElementById("app-backdrop");
  if (sidebar) sidebar.classList.remove("open");
  if (backdrop) backdrop.classList.remove("visible");
}

function initHeaderPopovers() {
  // Global search trigger
  const searchBar = document.getElementById("header-search-bar");
  if (searchBar) {
    searchBar.addEventListener("click", openSearchModal);
  }

  // Notification Popover
  const notifBtn = document.getElementById("header-notif-btn");
  const notifPopover = document.getElementById("notif-popover");
  const notifList = document.getElementById("notif-list");

  if (notifList) {
    notifList.textContent = "Notifications are not available yet.";
  }

  if (notifBtn && notifPopover) {
    notifBtn.addEventListener("click", (e) => {
      e.stopPropagation();
      notifPopover.classList.toggle("open");
      profilePopover?.classList.remove("open");
    });
  }

  // Profile Popover
  const profileBtn = document.getElementById("header-profile-btn");
  const profilePopover = document.getElementById("profile-popover");

  if (profileBtn && profilePopover) {
    profileBtn.addEventListener("click", (e) => {
      e.stopPropagation();
      profilePopover.classList.toggle("open");
      notifPopover?.classList.remove("open");
    });
  }

  // Profile popover actions
  document.getElementById("profile-action-settings")?.addEventListener("click", () => {
    profilePopover?.classList.remove("open");
    navigateTo("settings");
  });

  const signoutBtn = document.getElementById("profile-action-signout");
  signoutBtn?.addEventListener("click", async () => {
    profilePopover?.classList.remove("open");

    sessionStorage.removeItem("larpiano_preview_active");
    sessionStorage.removeItem("larpiano_admin_token");
    sessionStorage.removeItem("admin_token");
    localStorage.removeItem("larpiano_admin_token");
    localStorage.removeItem("admin_token");

    const currentToken = getAdminToken();
    let result = { serverRevoked: false };
    if (currentToken) {
      result = await logout();
    } else {
      setAdminToken(null);
    }
    updateHeaderProfile(null);

    // Invalidate and clear protected view content
    const viewport = document.getElementById("page-viewport");
    if (viewport) {
      viewport.innerHTML = "";
    }

    const notice = currentToken && result.serverRevoked
      ? "Signed out successfully."
      : "Signed out.";

    showLoginGate(notice);
  });

  // Close popovers on outer click
  window.addEventListener("click", () => {
    notifPopover?.classList.remove("open");
    profilePopover?.classList.remove("open");
  });
}

export function updateHeaderProfile(user) {
  const nameEl = document.querySelector(".profile-name");
  const roleEl = document.querySelector(".profile-role");
  const avatarEl = document.querySelector(".profile-avatar");
  const popoverNameEl = document.querySelector(".popover-user-name");
  const popoverRoleEl = document.querySelector(".popover-user-role");
  const signoutItem = document.getElementById("profile-action-signout");
  const bannerTag = document.querySelector(".demo-tag");
  const bannerText = document.querySelector(".demo-banner-text");

  if (user && user.is_preview) {
    if (nameEl) nameEl.textContent = "Preview Guest";
    if (roleEl) roleEl.textContent = "Offline Preview";
    if (avatarEl) avatarEl.textContent = "P";
    if (popoverNameEl) popoverNameEl.textContent = "Preview Guest";
    if (popoverRoleEl) popoverRoleEl.textContent = "Offline Sandbox (Read-Only)";
    if (bannerTag) bannerTag.textContent = "OFFLINE PREVIEW";
    if (bannerText) bannerText.textContent = "Unauthenticated Read-Only Sandbox · Zero Administrative Privileges";
    if (signoutItem) {
      signoutItem.innerHTML = `<span>🚪</span> Exit Preview`;
      signoutItem.style.color = "var(--warning)";
    }
  } else if (user) {
    const name = user.display_name || user.email || user.username || "Administrator";
    const role = user.role === "admin" ? "Administrator" : (user.role || "Operator");
    const initial = name.charAt(0).toUpperCase();

    if (nameEl) nameEl.textContent = name;
    if (roleEl) roleEl.textContent = role;
    if (avatarEl) avatarEl.textContent = initial;
    if (popoverNameEl) popoverNameEl.textContent = name;
    if (popoverRoleEl) popoverRoleEl.textContent = role;
    if (bannerTag) bannerTag.textContent = "CONTROL CENTER";
    if (bannerText) bannerText.textContent = "Larpiano Admin Fleet Console";
    if (signoutItem) {
      signoutItem.innerHTML = `<span>🚪</span> Sign out`;
      signoutItem.style.color = "var(--danger)";
    }
  } else {
    if (nameEl) nameEl.textContent = "Not signed in";
    if (roleEl) roleEl.textContent = "Unauthenticated";
    if (avatarEl) avatarEl.textContent = "—";
    if (popoverNameEl) popoverNameEl.textContent = "Not signed in";
    if (popoverRoleEl) popoverRoleEl.textContent = "Guest";
    if (bannerTag) bannerTag.textContent = "CONTROL CENTER";
    if (bannerText) bannerText.textContent = "Larpiano Admin Fleet Console";
    if (signoutItem) {
      signoutItem.innerHTML = `<span>🔑</span> Sign in`;
      signoutItem.style.color = "var(--accent-soft)";
    }
  }
}

export function showLoginGate(notice = "") {
  let gate = document.querySelector(".admin-login-gate");
  if (gate) {
    const noticeEl = gate.querySelector(".admin-login-notice");
    if (noticeEl) {
      noticeEl.textContent = notice;
      noticeEl.style.display = notice ? "block" : "none";
    }
    return;
  }

  gate = document.createElement("div");
  gate.className = "admin-login-gate";
  gate.innerHTML = `
    <form class="admin-login-form">
      <h1>Admin Sign In</h1>
      <p>Sign in with an administrator account or enter your configured access token.</p>
      ${notice ? `<div class="admin-login-notice" style="color: var(--accent-soft); font-size: 13px; margin-bottom: 12px; padding: 8px 12px; background: rgba(167, 139, 250, 0.1); border-radius: 6px; border: 1px solid var(--border-accent);">${escapeText(notice)}</div>` : `<div class="admin-login-notice" style="display: none;"></div>`}
      <label>Email or Username
        <input name="username" autocomplete="username" autofocus>
      </label>
      <label>Password
        <input name="password" type="password" autocomplete="current-password">
      </label>
      <label>Access Token (optional)
        <input name="token" type="password" autocomplete="off">
      </label>
      <p class="admin-login-error" role="alert" style="min-height: 1.2em; font-size: 12px; margin-top: 4px;"></p>
      <div style="display: flex; gap: 10px; margin-top: 16px; align-items: center;">
        <button type="submit" class="btn btn-primary" style="flex: 1;">Sign In</button>
        <button type="button" id="admin-guest-preview-btn" class="btn btn-secondary" style="font-size: 12px;">Offline Preview</button>
      </div>
    </form>
  `;
  document.body.appendChild(gate);

  const form = gate.querySelector("form");
  const error = gate.querySelector(".admin-login-error");
  const guestBtn = gate.querySelector("#admin-guest-preview-btn");

  guestBtn?.addEventListener("click", () => {
    gate.remove();
    sessionStorage.setItem("larpiano_preview_active", "true");
    updateHeaderProfile({ display_name: "Preview Guest", role: "Offline Preview (Read-Only)", is_preview: true });
    const initialHash = window.location.hash.replace("#/", "");
    navigateTo(initialHash || "overview");
  });

  form.addEventListener("submit", async event => {
    event.preventDefault();
    const data = new FormData(form);
    const submit = form.querySelector("button[type='submit']");
    submit.disabled = true;
    error.textContent = "";
    try {
      const res = await login({
        username: String(data.get("username") || ""),
        password: String(data.get("password") || ""),
        token: String(data.get("token") || "")
      });
      gate.remove();
      sessionStorage.removeItem("larpiano_preview_active");
      updateHeaderProfile(res.user || { display_name: "Administrator", role: "admin" });
      const initialHash = window.location.hash.replace("#/", "");
      navigateTo(initialHash || "overview");
    } catch (err) {
      error.textContent = err.message || "Sign in failed. Check your credentials and try again.";
    } finally {
      submit.disabled = false;
    }
  });
}


export async function checkFleetHealth() {
  const tag = document.querySelector(".fleet-channel-tag");
  if (!tag) return;
  const isPreview = isPreviewMode();
  if (isPreview) {
    tag.textContent = "v1.1.0 · Offline Preview Mode";
    tag.style.background = "rgba(245, 158, 11, 0.15)";
    tag.style.color = "var(--warning)";
    tag.style.borderColor = "rgba(245, 158, 11, 0.4)";
    return;
  }

  const token = getAdminToken();
  if (!token) {
    tag.textContent = "Auth Required";
    tag.style.background = "rgba(148, 163, 184, 0.15)";
    tag.style.color = "var(--muted)";
    tag.style.borderColor = "var(--border-subtle)";
    return;
  }

  try {
    const res = await fetch(`${getApiBaseUrl()}/api/health`, { headers: { "Accept": "application/json" } });
    if (res.ok) {
      tag.textContent = "v1.1.0 · Live API Connected";
      tag.style.background = "rgba(52, 211, 153, 0.15)";
      tag.style.color = "var(--success)";
      tag.style.borderColor = "rgba(52, 211, 153, 0.4)";
    } else {
      tag.textContent = `API Error (${res.status})`;
      tag.style.background = "rgba(239, 68, 68, 0.15)";
      tag.style.color = "var(--danger)";
      tag.style.borderColor = "rgba(239, 68, 68, 0.4)";
    }
  } catch (_) {
    tag.textContent = "Backend API Offline";
    tag.style.background = "rgba(239, 68, 68, 0.15)";
    tag.style.color = "var(--danger)";
    tag.style.borderColor = "rgba(239, 68, 68, 0.4)";
  }
}

// Bootstrap
async function bootstrap() {
  initSidebar();
  initHeaderPopovers();
  initUserDrawer();
  initAnnouncementModal(() => {
    if (currentRoute === "announcements") renderAnnouncements(document.getElementById("page-viewport"));
  });
  initReleaseModal();
  initSearchModal(navigateTo, openUserDrawer);

  checkFleetHealth();
  setInterval(checkFleetHealth, 20000);

  // Cross-tab synchronization via BroadcastChannel & storage events
  onDataChange((msg) => {
    if (!msg || !msg.type) return;
    const viewport = document.getElementById("page-viewport");
    if (!viewport) return;

    if (msg.type === "USERS_CHANGED" && (currentRoute === "users" || currentRoute === "overview")) {
      navigateTo(currentRoute);
    } else if (msg.type === "ANNOUNCEMENTS_CHANGED" && currentRoute === "announcements") {
      navigateTo(currentRoute);
    } else if (msg.type === "SESSIONS_CHANGED" && (currentRoute === "sessions" || currentRoute === "overview")) {
      navigateTo(currentRoute);
    } else if (msg.type === "RELEASES_CHANGED" && (currentRoute === "releases" || currentRoute === "overview")) {
      navigateTo(currentRoute);
    }
  });

  const token = getAdminToken();
  if (token) {
    try {
      const res = await getCurrentUser();
      sessionStorage.removeItem("larpiano_preview_active");
      updateHeaderProfile(res?.user || { display_name: "Administrator", role: "admin" });
      const initialHash = window.location.hash.replace("#/", "");
      navigateTo(initialHash || "overview");
      return;
    } catch (_) {
      // Clear invalid or stale token
      setAdminToken(null);
    }
  }

  // Check if explicit preview mode was activated during this session
  const isPreview = sessionStorage.getItem("larpiano_preview_active") === "true";
  if (isPreview) {
    updateHeaderProfile({ display_name: "Preview Guest", role: "Offline Preview (Read-Only)", is_preview: true });
    const initialHash = window.location.hash.replace("#/", "");
    navigateTo(initialHash || "overview");
    return;
  }

  // Not authenticated & no preview: ALWAYS show login gate on refresh/reopen
  updateHeaderProfile(null);
  const viewport = document.getElementById("page-viewport");
  if (viewport) {
    viewport.innerHTML = "";
  }
  showLoginGate();
}

// Script is at bottom of body; ensure bootstrap always executes reliably
if (document.readyState === "loading") {
  document.addEventListener("DOMContentLoaded", bootstrap);
} else {
  bootstrap();
}
