/**
 * Page: Analytics
 */

import { MOCK_ANALYTICS } from "../data/mockData.js";
import { renderBarChart, renderDonutChart } from "../components/chart.js";
import { showToast } from "../components/toast.js";

let selectedRange = "7d";

export function renderAnalytics(container) {
  container.innerHTML = `
    <!-- Page Header & Date Range Filter -->
    <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: var(--space-4);">
      <div>
        <h1 class="page-title">Analytics</h1>
        <p class="page-subtitle">Daily active players, session duration, and client version distribution.</p>
      </div>

      <!-- Date Filter Dropdown -->
      <div style="display: flex; align-items: center; gap: 8px;">
        <span style="font-size: var(--text-xs); color: var(--subtle);">Period:</span>
        <select id="analytics-range-select" class="form-select" style="padding: 6px 12px; font-size: var(--text-xs); background: var(--surface);">
          <option value="7d" ${selectedRange === '7d' ? 'selected' : ''}>Last 7 days</option>
          <option value="30d" ${selectedRange === '30d' ? 'selected' : ''}>Last 30 days</option>
          <option value="90d" ${selectedRange === '90d' ? 'selected' : ''}>Last 90 days</option>
        </select>
      </div>
    </div>

    <!-- 3 Core Metric Tiles -->
    <div class="analytics-metrics-row">
      <div class="card">
        <div class="kpi-top">
          <span class="kpi-label">Daily active users</span>
        </div>
        <div class="kpi-body" style="margin-top: 8px;">
          <div class="kpi-value">${MOCK_ANALYTICS.dau}</div>
        </div>
        <div class="kpi-footer">
          <span class="kpi-delta positive">▲ ${MOCK_ANALYTICS.dauDelta} vs prior week</span>
        </div>
      </div>

      <div class="card">
        <div class="kpi-top">
          <span class="kpi-label">Weekly active users</span>
        </div>
        <div class="kpi-body" style="margin-top: 8px;">
          <div class="kpi-value">${MOCK_ANALYTICS.wau.toLocaleString()}</div>
        </div>
        <div class="kpi-footer">
          <span class="kpi-delta positive">▲ ${MOCK_ANALYTICS.wauDelta} vs prior month</span>
        </div>
      </div>

      <div class="card">
        <div class="kpi-top">
          <span class="kpi-label">Avg. session duration</span>
        </div>
        <div class="kpi-body" style="margin-top: 8px;">
          <div class="kpi-value">${MOCK_ANALYTICS.avgSession}</div>
        </div>
        <div class="kpi-footer">
          <span class="kpi-delta positive">▲ ${MOCK_ANALYTICS.avgSessionDelta}</span>
        </div>
      </div>
    </div>

    <!-- Charts Grid (Daily Active Users Bars & Version Distribution Donut) -->
    <div class="analytics-charts-grid" style="margin-top: var(--space-4);">
      <!-- Bar Chart Card -->
      <div class="card">
        <div class="card-header">
          <div>
            <h2 class="card-title">Active users (daily)</h2>
            <p class="card-subtitle">Aggregate counts for the selected period · Demo Data</p>
          </div>
          <span style="font-size: 11px; color: var(--subtle); font-family: var(--font-mono);">7-day series</span>
        </div>
        <div id="analytics-bar-chart" style="height: 190px; width: 100%;"></div>
      </div>

      <!-- Version Distribution Donut Card -->
      <div class="card">
        <div class="card-header">
          <div>
            <h2 class="card-title">Version distribution</h2>
            <p class="card-subtitle">Consented telemetry</p>
          </div>
        </div>
        <div id="analytics-donut-chart"></div>
      </div>
    </div>

    <!-- Privacy & Telemetry Statement -->
    <div class="info-banner" style="margin-top: var(--space-4);">
      <div class="info-banner-icon">🔒</div>
      <div style="font-size: 12px; color: var(--muted);">
        <strong style="color: var(--text);">Aggregated Analytics Architecture:</strong>
        Larpiano metrics are strictly aggregated at hourly intervals. No individual keystrokes, Roblox chat logs, file paths, or private song data are ever collected or stored.
      </div>
    </div>
  `;

  // Render Charts
  setTimeout(() => {
    renderBarChart(MOCK_ANALYTICS.dailyUsers7d, "analytics-bar-chart");
    renderDonutChart(MOCK_ANALYTICS.versionDistribution, "analytics-donut-chart");
  }, 10);

  // Date range switcher
  const select = document.getElementById("analytics-range-select");
  if (select) {
    select.addEventListener("change", (e) => {
      selectedRange = e.target.value;
      showToast(`Updated analytics range: Last ${selectedRange === '7d' ? '7 days' : (selectedRange === '30d' ? '30 days' : '90 days')} (Demo data)`, "info");
      
      const chartData = selectedRange === "30d" 
        ? [
            { label: "Oct 12", count: 180 },
            { label: "Oct 19", count: 220 },
            { label: "Oct 26", count: 260 },
            { label: "Nov 2", count: 290 },
            { label: "Nov 8", count: 312 }
          ]
        : MOCK_ANALYTICS.dailyUsers7d;

      renderBarChart(chartData, "analytics-bar-chart");
    });
  }
}
