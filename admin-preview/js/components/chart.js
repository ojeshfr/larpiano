/**
 * Reactive SVG Chart Components
 * High-performance, zero dependency, responsive SVG rendering
 */

/**
 * Render the 24h Live Activity Area Chart
 */
export function renderActivityChart(data, containerId) {
  const container = document.getElementById(containerId);
  if (!container) return;

  const width = 640;
  const height = 200;
  const padding = { top: 36, right: 20, bottom: 28, left: 35 };

  const chartW = width - padding.left - padding.right;
  const chartH = height - padding.top - padding.bottom;

  const maxY = 45;
  const points = data.map((d, i) => {
    const x = padding.left + (i / (data.length - 1)) * chartW;
    const y = padding.top + chartH - (d.count / maxY) * chartH;
    return { x, y, ...d };
  });

  // Construct smooth cubic bezier spline
  function getSmoothCurve(pts) {
    if (pts.length < 2) return "";
    let d = `M ${pts[0].x.toFixed(1)} ${pts[0].y.toFixed(1)}`;
    for (let i = 0; i < pts.length - 1; i++) {
      const p0 = pts[i === 0 ? 0 : i - 1];
      const p1 = pts[i];
      const p2 = pts[i + 1];
      const p3 = pts[i + 2] || p2;

      const cp1x = p1.x + (p2.x - p0.x) / 6;
      const cp1y = p1.y + (p2.y - p0.y) / 6;
      const cp2x = p2.x - (p3.x - p1.x) / 6;
      const cp2y = p2.y - (p3.y - p1.y) / 6;

      d += ` C ${cp1x.toFixed(1)} ${cp1y.toFixed(1)}, ${cp2x.toFixed(1)} ${cp2y.toFixed(1)}, ${p2.x.toFixed(1)} ${p2.y.toFixed(1)}`;
    }
    return d;
  }

  const pathData = getSmoothCurve(points);
  const areaData = `${pathData} L ${points[points.length - 1].x.toFixed(1)} ${padding.top + chartH} L ${points[0].x.toFixed(1)} ${padding.top + chartH} Z`;

  // Grid ticks
  const yTicks = [0, 10, 20, 30, 40];
  const gridHtml = yTicks.map(val => {
    const yPos = padding.top + chartH - (val / maxY) * chartH;
    return `
      <line x1="${padding.left}" y1="${yPos}" x2="${padding.left + chartW}" y2="${yPos}" class="chart-grid-line" />
      <text x="${padding.left - 10}" y="${yPos + 4}" text-anchor="end" class="chart-axis-text">${val}</text>
    `;
  }).join("");

  // X ticks (every 3 hours)
  const xLabels = ["00:00", "03:00", "06:00", "09:00", "12:00", "15:00", "18:00", "21:00"];
  const xTicksHtml = xLabels.map(label => {
    const idx = data.findIndex(d => d.time.startsWith(label.slice(0, 2)));
    if (idx === -1) return "";
    const xPos = padding.left + (idx / (data.length - 1)) * chartW;
    return `
      <text x="${xPos}" y="${height - 8}" text-anchor="middle" class="chart-axis-text">${label}</text>
    `;
  }).join("");

  // Default active sample cursor (e.g. at 19:40 with 24 online)
  const sampleIdx = data.findIndex(d => d.time === "19:40");
  const samplePoint = points[sampleIdx >= 0 ? sampleIdx : points.length - 3];
  const initialOffset = samplePoint.y < 42 ? 22 : -28;

  container.innerHTML = `
    <svg class="svg-chart" viewBox="0 0 ${width} ${height}" preserveAspectRatio="none">
      <defs>
        <linearGradient id="areaGradient" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="#A78BFA" stop-opacity="0.32"/>
          <stop offset="60%" stop-color="#8B5CF6" stop-opacity="0.10"/>
          <stop offset="100%" stop-color="#14141B" stop-opacity="0.0"/>
        </linearGradient>
      </defs>
      
      <!-- Grid Lines -->
      ${gridHtml}
      ${xTicksHtml}

      <!-- Area Fill -->
      <path d="${areaData}" fill="url(#areaGradient)" />

      <!-- Main Stroke -->
      <path d="${pathData}" fill="none" stroke="#A78BFA" stroke-width="2" stroke-linejoin="round" stroke-linecap="round" />

      <!-- Interactive Highlight Cursor -->
      <g id="chart-cursor" class="chart-tooltip-group" transform="translate(${samplePoint.x}, ${samplePoint.y})">
        <line x1="0" y1="${-(samplePoint.y - padding.top)}" x2="0" y2="${padding.top + chartH - samplePoint.y}" stroke="#A78BFA" stroke-dasharray="3, 3" stroke-width="1" opacity="0.6"/>
        <circle cx="0" cy="0" r="4.5" fill="#FFFFFF" stroke="#A78BFA" stroke-width="2.5"/>
        <g id="chart-tooltip-bubble" transform="translate(0, ${initialOffset})">
          <rect x="-46" y="-12" width="92" height="22" rx="4" fill="#1C1A27" stroke="#7653C6" stroke-width="1"/>
          <text id="chart-tooltip-text" x="0" y="3" text-anchor="middle" fill="#F2F0F7" font-size="11" font-weight="600" font-family="'Inter', sans-serif">
            ${samplePoint.count} online · ${samplePoint.time}
          </text>
        </g>
      </g>
    </svg>
  `;

  // Attach hover interaction
  const svg = container.querySelector("svg");
  if (!svg) return;
  const cursor = container.querySelector("#chart-cursor");
  const tooltipText = container.querySelector("#chart-tooltip-text");
  const tooltipBubble = container.querySelector("#chart-tooltip-bubble");

  svg.addEventListener("mousemove", (e) => {
    const rect = svg.getBoundingClientRect();
    const mouseX = ((e.clientX - rect.left) / rect.width) * width;

    // Find closest data point
    let closest = points[0];
    let minDiff = Infinity;
    points.forEach(p => {
      const diff = Math.abs(p.x - mouseX);
      if (diff < minDiff) {
        minDiff = diff;
        closest = p;
      }
    });

    if (cursor && tooltipText) {
      cursor.setAttribute("transform", `translate(${closest.x}, ${closest.y})`);
      cursor.querySelector("line").setAttribute("y1", `${-(closest.y - padding.top)}`);
      cursor.querySelector("line").setAttribute("y2", `${padding.top + chartH - closest.y}`);
      tooltipText.textContent = `${closest.count} online · ${closest.time}`;
      if (tooltipBubble) {
        const offset = closest.y < 42 ? 22 : -28;
        tooltipBubble.setAttribute("transform", `translate(0, ${offset})`);
      }
    }
  });
}

/**
 * Render Daily Active Users Bar Chart
 */
export function renderBarChart(data, containerId) {
  const container = document.getElementById(containerId);
  if (!container) return;

  const width = 500;
  const height = 180;
  const padding = { top: 20, right: 15, bottom: 25, left: 35 };

  const chartW = width - padding.left - padding.right;
  const chartH = height - padding.top - padding.bottom;

  const maxVal = 400;
  const barCount = data.length;
  const barWidth = (chartW / barCount) * 0.65;
  const step = chartW / barCount;

  const barsHtml = data.map((d, i) => {
    const x = padding.left + i * step + (step - barWidth) / 2;
    const barH = (d.count / maxVal) * chartH;
    const y = padding.top + chartH - barH;

    return `
      <g class="chart-bar-group" data-label="${d.label}: ${d.count} users">
        <rect x="${x}" y="${y}" width="${barWidth}" height="${barH}" rx="3" fill="#8B5CF6" opacity="0.85"
          style="transition: all 150ms ease; cursor: pointer;">
          <title>${d.label}: ${d.count} users</title>
        </rect>
        <text x="${x + barWidth / 2}" y="${height - 6}" text-anchor="middle" class="chart-axis-text">${d.label}</text>
      </g>
    `;
  }).join("");

  const yTicks = [0, 100, 200, 300, 400];
  const gridHtml = yTicks.map(val => {
    const yPos = padding.top + chartH - (val / maxVal) * chartH;
    return `
      <line x1="${padding.left}" y1="${yPos}" x2="${width - padding.right}" y2="${yPos}" class="chart-grid-line" />
      <text x="${padding.left - 8}" y="${yPos + 4}" text-anchor="end" class="chart-axis-text">${val}</text>
    `;
  }).join("");

  container.innerHTML = `
    <svg class="svg-chart" viewBox="0 0 ${width} ${height}">
      ${gridHtml}
      ${barsHtml}
    </svg>
  `;
}

/**
 * Render Version Distribution Donut Chart
 */
export function renderDonutChart(data, containerId) {
  const container = document.getElementById(containerId);
  if (!container) return;

  const size = 160;
  const strokeWidth = 20;
  const radius = (size - strokeWidth) / 2;
  const circumference = 2 * Math.PI * radius;

  let currentOffset = 0;
  const arcs = data.map(item => {
    const arcLength = (item.share / 100) * circumference;
    const dashOffset = -currentOffset;
    currentOffset += arcLength;

    return `
      <circle cx="${size / 2}" cy="${size / 2}" r="${radius}" fill="none"
        stroke="${item.color}" stroke-width="${strokeWidth}"
        stroke-dasharray="${arcLength} ${circumference - arcLength}"
        stroke-dashoffset="${dashOffset}"
        transform="rotate(-90 ${size / 2} ${size / 2})"
        style="transition: stroke-dasharray 300ms ease;" />
    `;
  }).join("");

  const legendHtml = data.map(item => `
    <div class="legend-item">
      <div>
        <span class="legend-color-dot" style="background-color: ${item.color};"></span>
        <span style="color: var(--text); font-weight: 500;">${item.version}</span>
      </div>
      <span style="color: var(--muted); font-family: var(--font-mono);">${item.share}%</span>
    </div>
  `).join("");

  container.innerHTML = `
    <div style="position: relative; width: ${size}px; height: ${size}px; margin: 0 auto;">
      <svg width="${size}" height="${size}" viewBox="0 0 ${size} ${size}">
        ${arcs}
      </svg>
      <div style="position: absolute; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; pointer-events: none;">
        <span style="font-size: 11px; color: var(--subtle); text-transform: uppercase; letter-spacing: 0.05em;">Current</span>
        <span style="font-size: 15px; font-weight: 600; color: var(--text); font-family: var(--font-mono);">v1.1.0</span>
      </div>
    </div>
    <div class="donut-legend">
      ${legendHtml}
    </div>
  `;
}
