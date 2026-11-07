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
import { MOCK_NOTIFICATIONS } from "./data/mockData.js";
import { showToast } from "./components/toast.js";

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
    notifList.innerHTML = MOCK_NOTIFICATIONS.map(n => `
      <div style="padding: 10px 12px; border-bottom: 1px solid var(--border-subtle); display: flex; flex-direction: column; gap: 2px;">
        <div style="display: flex; justify-content: space-between; align-items: center;">
          <span style="font-size: 13px; font-weight: 500; color: var(--text);">${n.title}</span>
          <span style="font-size: 10px; color: var(--subtle);">${n.time}</span>
        </div>
        <div style="font-size: 12px; color: var(--muted);">${n.desc}</div>
      </div>
    `).join("");
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

  // Demo sign out / profile actions
  document.getElementById("profile-action-settings")?.addEventListener("click", () => {
    profilePopover?.classList.remove("open");
    navigateTo("settings");
  });

  document.getElementById("profile-action-signout")?.addEventListener("click", () => {
    profilePopover?.classList.remove("open");
    showToast("Signed out from administrative demo session", "info");
  });

  // Close popovers on outer click
  window.addEventListener("click", () => {
    notifPopover?.classList.remove("open");
    profilePopover?.classList.remove("open");
  });
}

// Bootstrap
function bootstrap() {
  initSidebar();
  initHeaderPopovers();
  initUserDrawer();
  initAnnouncementModal(() => {
    if (currentRoute === "announcements") renderAnnouncements(document.getElementById("page-viewport"));
  });
  initReleaseModal();
  initSearchModal(navigateTo, openUserDrawer);

  // Initial Route based on URL hash
  const initialHash = window.location.hash.replace("#/", "");
  navigateTo(initialHash || "overview");
}

if (document.readyState === "loading") {
  window.addEventListener("DOMContentLoaded", bootstrap);
} else {
  bootstrap();
}
