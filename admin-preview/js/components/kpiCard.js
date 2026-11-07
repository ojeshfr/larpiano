/**
 * KPI Card Component
 */

export function renderKpiCard({ id, label, value, delta, isPositive, sparkline, icon, isDonut, donutPercent }) {
  let graphicHtml = "";

  if (isDonut) {
    // Mini Donut
    const strokeWidth = 3;
    const radius = 10;
    const circumference = 2 * Math.PI * radius;
    const offset = circumference - (donutPercent / 100) * circumference;

    graphicHtml = `
      <svg class="kpi-mini-donut" viewBox="0 0 26 26">
        <circle cx="13" cy="13" r="${radius}" fill="none" stroke="var(--surface-soft)" stroke-width="${strokeWidth}" />
        <circle cx="13" cy="13" r="${radius}" fill="none" stroke="var(--accent)" stroke-width="${strokeWidth}"
          stroke-dasharray="${circumference}" stroke-dashoffset="${offset}" stroke-linecap="round"
          transform="rotate(-90 13 13)" />
      </svg>
    `;
  } else if (sparkline && sparkline.length > 1) {
    // Sparkline SVG
    const min = Math.min(...sparkline);
    const max = Math.max(...sparkline);
    const range = max - min || 1;
    const width = 72;
    const height = 22;

    const points = sparkline.map((val, idx) => {
      const x = (idx / (sparkline.length - 1)) * width;
      const y = height - ((val - min) / range) * (height - 6) - 3;
      return `${x.toFixed(1)},${y.toFixed(1)}`;
    }).join(" ");

    graphicHtml = `
      <svg class="kpi-sparkline" viewBox="0 0 ${width} ${height}">
        <polyline points="${points}" fill="none" stroke="var(--accent)" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" />
      </svg>
    `;
  }

  const deltaClass = isPositive ? "positive" : "neutral";
  const arrow = isPositive ? "▲" : "";

  return `
    <div class="kpi-card" id="kpi-${id}">
      <div class="kpi-top">
        <div class="kpi-label-group">
          <span class="kpi-icon">${icon || ""}</span>
          <span class="kpi-label">${label}</span>
        </div>
      </div>
      <div class="kpi-body">
        <div class="kpi-value">${value}</div>
      </div>
      <div class="kpi-footer">
        <span class="kpi-delta ${deltaClass}">
          ${arrow} ${delta}
        </span>
        ${graphicHtml}
      </div>
    </div>
  `;
}
