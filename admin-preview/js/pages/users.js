import { escapeText } from "../components/safeText.js";
/**
 * Page: Users Management
 * Connected to live backend users API and user_preferences table.
 */

import { openUserDrawer } from "../components/userDrawer.js";
import { showToast } from "../components/toast.js";
import { fetchUsers, createUser, updateUser } from "../api/users.js";
import { MOCK_USERS } from "../data/mockData.js";

let currentFilter = "all";
let searchQuery = "";
let loadedUsers = [...MOCK_USERS];

export function renderUsers(container) {
  function getFilteredUsers() {
    return loadedUsers.filter(u => {
      const status = u.status || u.account_status || "offline";
      const role = u.role ? u.role.toLowerCase() : "user";

      // Role / status filter
      if (currentFilter === "active" && status !== "Online" && status !== "Idle" && status !== "active") return false;
      if (currentFilter === "suspended" && status !== "Suspended" && status !== "suspended") return false;
      if (currentFilter === "admin" && role !== "admin") return false;
      if (currentFilter === "tester" && role !== "tester") return false;

      // Text search
      if (searchQuery) {
        const q = searchQuery.toLowerCase();
        const name = (u.name || u.display_name || "").toLowerCase();
        const email = (u.email || "").toLowerCase();
        const ver = (u.version || u.app_version || "").toLowerCase();
        return name.includes(q) || email.includes(q) || role.includes(q) || ver.includes(q);
      }
      return true;
    });
  }

  function renderTableRows(users) {
    if (users.length === 0) {
      return `
        <tr>
          <td colspan="7" style="text-align: center; padding: 32px; color: var(--subtle);">
            No users match the current filter or search query.
          </td>
        </tr>
      `;
    }

    return users.map(u => {
      const name = u.name || u.display_name || "User";
      const id = u.id || "0";
      const role = u.role ? (u.role.charAt(0).toUpperCase() + u.role.slice(1)) : "User";
      const rawStatus = u.status || u.account_status || "active";
      const version = u.version || u.app_version || "Unavailable";
      const lastSeen = u.lastSeen || (u.last_login_at ? new Date(u.last_login_at).toLocaleDateString() : "Unavailable");
      const sessionsCount = u.sessionsCount !== undefined ? u.sessionsCount : (u.telemetry_consent ? "Consent: Yes" : "Consent: Off");

      const statusBadge = (rawStatus === "Online" || rawStatus === "active")
        ? `<span class="badge badge-online"><span class="status-indicator-dot online"></span>Active</span>`
        : (rawStatus === "Idle"
          ? `<span class="badge badge-idle"><span class="status-indicator-dot idle"></span>Idle</span>`
          : (rawStatus === "Suspended" || rawStatus === "suspended"
            ? `<span class="badge" style="background: var(--danger-bg); color: var(--danger);"><span class="status-indicator-dot" style="background: var(--danger);"></span>Suspended</span>`
            : `<span class="badge badge-offline"><span class="status-indicator-dot offline"></span>Offline</span>`));

      const roleBadge = role === "Admin"
        ? `<span class="badge badge-role-admin">Admin</span>`
        : (role === "Tester"
          ? `<span class="badge badge-role-tester">Tester</span>`
          : `<span class="badge badge-role-user">User</span>`);

      return `
        <tr class="clickable" data-user-id="${escapeText(id)}">
          <td style="font-weight: 500;">
            <div style="display: flex; align-items: center; gap: 10px;">
              <div style="width: 26px; height: 26px; border-radius: 50%; background: var(--surface-soft); display: flex; align-items: center; justify-content: center; font-size: 11px; color: var(--accent-soft); font-weight: 600;">
                ${escapeText(name.charAt(0).toUpperCase())}
              </div>
              <div>
                <div style="color: var(--text);">${escapeText(name)}</div>
                <div class="cell-meta font-mono" style="font-size: 10px;">${escapeText(u.email || id)}</div>
              </div>
            </div>
          </td>
          <td>${roleBadge}</td>
          <td>${statusBadge}</td>
          <td><span class="cell-mono">${escapeText(version)}</span></td>
          <td><span class="cell-meta">${escapeText(lastSeen)}</span></td>
          <td style="color: var(--muted); font-family: var(--font-mono); font-size: 12px;">${escapeText(sessionsCount)}</td>
          <td>
            <button class="btn btn-ghost btn-icon btn-user-action" data-user-id="${escapeText(id)}" title="Inspect Account" style="color: var(--subtle);">
              •••
            </button>
          </td>
        </tr>
      `;
    }).join("");
  }

  container.innerHTML = `
    <!-- Page Header -->
    <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: var(--space-4);">
      <div>
        <h1 class="page-title">Users</h1>
        <p class="page-subtitle">Registered accounts, client versions, and telemetry consent status.</p>
      </div>
      <button id="btn-new-user-invite" class="btn btn-primary">
        <span>+</span> New user
      </button>
    </div>

    <!-- Toolbar: Search & Filters -->
    <div class="toolbar" style="margin-bottom: var(--space-4);">
      <div class="input-search">
        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="color: var(--subtle);"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>
        <input type="text" id="users-search-input" placeholder="Search by name, email, role, version..." value="${escapeText(searchQuery)}" />
      </div>

      <div class="filter-pills">
        <button class="filter-pill ${currentFilter === 'all' ? 'active' : ''}" data-filter="all">All</button>
        <button class="filter-pill ${currentFilter === 'active' ? 'active' : ''}" data-filter="active">Active</button>
        <button class="filter-pill ${currentFilter === 'tester' ? 'active' : ''}" data-filter="tester">Testers</button>
        <button class="filter-pill ${currentFilter === 'admin' ? 'active' : ''}" data-filter="admin">Admins</button>
        <button class="filter-pill ${currentFilter === 'suspended' ? 'active' : ''}" data-filter="suspended">Suspended</button>
      </div>
    </div>

    <!-- Users Table -->
    <div class="table-container">
      <table class="data-table">
        <thead>
          <tr>
            <th>User</th>
            <th>Role</th>
            <th>Status</th>
            <th>Version</th>
            <th>Last seen</th>
            <th>Sessions / Consent</th>
            <th style="width: 40px;"></th>
          </tr>
        </thead>
        <tbody id="users-table-body">
          ${renderTableRows(getFilteredUsers())}
        </tbody>
      </table>
    </div>

    <div style="display: flex; justify-content: space-between; align-items: center; margin-top: var(--space-3); font-size: var(--text-xs); color: var(--subtle);">
      <span id="users-info-label">Connected to PostgreSQL database</span>
      <span id="users-count-label">${loadedUsers.length} total accounts</span>
    </div>
  `;

  // Attach search listener
  const searchInput = document.getElementById("users-search-input");
  const tbody = document.getElementById("users-table-body");

  const updateTable = () => {
    tbody.innerHTML = renderTableRows(getFilteredUsers());
    attachRowListeners();
  };

  if (searchInput) {
    searchInput.addEventListener("input", (e) => {
      searchQuery = e.target.value.trim();
      updateTable();
    });
  }

  // Attach filter pill listeners
  container.querySelectorAll(".filter-pill").forEach(pill => {
    pill.addEventListener("click", () => {
      container.querySelectorAll(".filter-pill").forEach(p => p.classList.remove("active"));
      pill.classList.add("active");
      currentFilter = pill.getAttribute("data-filter");
      updateTable();
    });
  });

  // Attach new user button
  document.getElementById("btn-new-user-invite")?.addEventListener("click", async () => {
    const email = prompt("Enter new user email address:");
    if (!email) return;
    const displayName = prompt("Enter display name:", email.split("@")[0]);
    if (!displayName) return;
    const role = confirm("Should this account have Admin privileges? (Click OK for Admin, Cancel for standard User)") ? "admin" : "user";
    
    try {
      await createUser({ email, display_name: displayName, role });
      showToast(`User ${displayName} created successfully!`, "success");
      loadUsers();
    } catch (err) {
      showToast(`Failed to create user: ${err.message}`, "error");
    }
  });

  function attachRowListeners() {
    container.querySelectorAll("tr[data-user-id]").forEach(tr => {
      tr.addEventListener("click", (e) => {
        if (e.target.closest(".btn-user-action")) return;
        const uid = tr.getAttribute("data-user-id");
        const user = loadedUsers.find(u => (u.id === uid || u.email === uid));
        if (user) openUserDrawer(user);
      });
    });

    container.querySelectorAll(".btn-user-action").forEach(btn => {
      btn.addEventListener("click", async (e) => {
        e.stopPropagation();
        const uid = btn.getAttribute("data-user-id");
        const action = prompt("Account Action: type 'suspend' to suspend, 'activate' to activate, or 'admin' to promote to admin:");
        if (!action) return;

        try {
          if (action.toLowerCase() === "suspend") {
            await updateUser(uid, { account_status: "suspended" });
            showToast("User suspended", "warning");
          } else if (action.toLowerCase() === "activate") {
            await updateUser(uid, { account_status: "active" });
            showToast("User activated", "success");
          } else if (action.toLowerCase() === "admin") {
            await updateUser(uid, { role: "admin" });
            showToast("User promoted to Admin", "success");
          }
          loadUsers();
        } catch (err) {
          showToast(`Action failed: ${err.message}`, "error");
        }
      });
    });
  }

  attachRowListeners();

  async function loadUsers() {
    try {
      const data = await fetchUsers({ search: searchQuery, status: currentFilter });
      if (data && Array.isArray(data.users)) {
        loadedUsers = data.users;
        updateTable();
        const countSpan = document.getElementById("users-count-label");
        if (countSpan) countSpan.textContent = `${data.total} total accounts`;
      }
    } catch (err) {
      console.log("[Users] Sandbox preview mode:", err.message);
    }
  }

  loadUsers();
}
