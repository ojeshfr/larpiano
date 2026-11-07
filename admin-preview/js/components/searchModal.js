/**
 * Command Palette & Global Search (Ctrl + K)
 */

import { MOCK_USERS, MOCK_ANNOUNCEMENTS } from "../data/mockData.js";

let searchModalEl = null;
let onNavigateCallback = null;
let onOpenUserCallback = null;

export function initSearchModal(navigateCallback, userCallback) {
  onNavigateCallback = navigateCallback;
  onOpenUserCallback = userCallback;

  searchModalEl = document.getElementById("search-modal");
  const input = document.getElementById("search-modal-input");
  const resultsEl = document.getElementById("search-modal-results");

  // Global hotkey Ctrl+K / Cmd+K
  window.addEventListener("keydown", (e) => {
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
      e.preventDefault();
      openSearchModal();
    } else if (e.key === "Escape" && searchModalEl && searchModalEl.classList.contains("open")) {
      closeSearchModal();
    }
  });

  const closeBtn = document.getElementById("search-modal-close");
  if (closeBtn) closeBtn.addEventListener("click", closeSearchModal);

  if (input) {
    input.addEventListener("input", () => {
      renderResults(input.value.trim().toLowerCase());
    });
  }
}

export function openSearchModal() {
  if (searchModalEl) {
    searchModalEl.classList.add("open");
    const input = document.getElementById("search-modal-input");
    if (input) {
      input.value = "";
      input.focus();
      renderResults("");
    }
  }
}

export function closeSearchModal() {
  if (searchModalEl) {
    searchModalEl.classList.remove("open");
  }
}

function renderResults(query) {
  const container = document.getElementById("search-modal-results");
  if (!container) return;

  const pages = [
    { id: "overview", label: "Overview", desc: "Live metrics, active sessions & recent events", icon: "📊" },
    { id: "users", label: "Users", desc: "User accounts, roles & telemetry consent", icon: "👥" },
    { id: "sessions", label: "Live Sessions", desc: "Active desktop clients linked to Roblox", icon: "💻" },
    { id: "analytics", label: "Analytics", desc: "Daily active users & version distribution", icon: "📈" },
    { id: "announcements", label: "Announcements", desc: "Broadcast alerts & in-app notices", icon: "📢" },
    { id: "releases", label: "Releases", desc: "Windows builds & GitHub release notes", icon: "🏷️" },
    { id: "playground", label: "Admin Playground", desc: "Test local effects & notification toasts", icon: "⚡" },
    { id: "audit", label: "Audit Log", desc: "Timestamped admin actions & event history", icon: "🛡️" },
    { id: "settings", label: "Settings", desc: "Profile, layout density & privacy limits", icon: "⚙️" }
  ];

  let matchedPages = pages;
  let matchedUsers = MOCK_USERS;
  let matchedAnc = MOCK_ANNOUNCEMENTS;

  if (query) {
    matchedPages = pages.filter(p => p.label.toLowerCase().includes(query) || p.desc.toLowerCase().includes(query));
    matchedUsers = MOCK_USERS.filter(u => u.name.toLowerCase().includes(query) || u.role.toLowerCase().includes(query));
    matchedAnc = MOCK_ANNOUNCEMENTS.filter(a => a.title.toLowerCase().includes(query) || a.message.toLowerCase().includes(query));
  }

  let html = "";

  if (matchedPages.length > 0) {
    html += `<div style="font-size: 11px; text-transform: uppercase; color: var(--subtle); padding: 8px 12px 4px 12px; font-weight: 600;">Navigation</div>`;
    html += matchedPages.map(p => `
      <div class="search-result-item" data-type="page" data-id="${p.id}" style="display: flex; align-items: center; gap: 10px; padding: 8px 12px; border-radius: var(--radius-sm); cursor: pointer;">
        <span style="font-size: 16px;">${p.icon}</span>
        <div>
          <div style="font-size: 13px; font-weight: 500; color: var(--text);">${p.label}</div>
          <div style="font-size: 11px; color: var(--subtle);">${p.desc}</div>
        </div>
      </div>
    `).join("");
  }

  if (matchedUsers.length > 0) {
    html += `<div style="font-size: 11px; text-transform: uppercase; color: var(--subtle); padding: 12px 12px 4px 12px; font-weight: 600;">Users</div>`;
    html += matchedUsers.slice(0, 4).map(u => `
      <div class="search-result-item" data-type="user" data-id="${u.id}" style="display: flex; align-items: center; gap: 10px; padding: 8px 12px; border-radius: var(--radius-sm); cursor: pointer;">
        <div style="width: 24px; height: 24px; border-radius: 50%; background: var(--accent-deep); display: flex; align-items: center; justify-content: center; font-size: 11px; color: #fff;">${u.name.charAt(0).toUpperCase()}</div>
        <div>
          <div style="font-size: 13px; font-weight: 500; color: var(--text);">${u.name} <span class="badge badge-role-user" style="font-size: 10px; margin-left: 6px;">${u.role}</span></div>
          <div style="font-size: 11px; color: var(--subtle);">${u.status} · ${u.version}</div>
        </div>
      </div>
    `).join("");
  }

  if (!html) {
    html = `<div style="padding: 24px; text-align: center; color: var(--subtle); font-size: 13px;">No results found for "${query}"</div>`;
  }

  container.innerHTML = html;

  // Click listeners
  container.querySelectorAll(".search-result-item").forEach(item => {
    item.addEventListener("click", () => {
      const type = item.getAttribute("data-type");
      const id = item.getAttribute("data-id");
      closeSearchModal();

      if (type === "page" && onNavigateCallback) {
        onNavigateCallback(id);
      } else if (type === "user" && onOpenUserCallback) {
        const user = MOCK_USERS.find(u => u.id === id);
        if (user) {
          onNavigateCallback("users");
          setTimeout(() => onOpenUserCallback(user), 100);
        }
      }
    });

    item.addEventListener("mouseenter", () => {
      item.style.backgroundColor = "var(--surface-raised)";
    });
    item.addEventListener("mouseleave", () => {
      item.style.backgroundColor = "transparent";
    });
  });
}
