/**
 * Admin Playground Local Preview Effects Runner
 * 
 * STRICT BOUNDARY: All effects execute exclusively inside this local frontend prototype.
 * No network requests, no remote client messaging, no live data alterations.
 */

import { showToast } from "./toast.js";

export function runPlaygroundEffect(effectId) {
  switch (effectId) {
    case "toast":
      showToast("♪ In-app preview: 'New community song arrangement available in catalog'", "success", 5000);
      break;

    case "theme":
      previewThemeTakeover();
      break;

    case "confetti":
      previewConfetti();
      break;

    case "vip":
      previewVipBadge();
      break;

    case "piano":
      previewPianoCelebration();
      break;

    case "custom":
      previewCustomShimmer();
      break;

    default:
      showToast("Unknown preview effect.", "warning");
  }
}

function previewThemeTakeover() {
  document.body.classList.add("theme-lavender-takeover");
  showToast("Theme takeover active locally (8s duration)", "info", 3000);

  const styleEl = document.createElement("style");
  styleEl.id = "theme-takeover-style";
  styleEl.textContent = `
    body.theme-lavender-takeover .card, 
    body.theme-lavender-takeover .kpi-card {
      border-color: rgba(167, 139, 250, 0.6) !important;
      box-shadow: 0 0 20px rgba(167, 139, 250, 0.15) !important;
      transition: all 400ms ease;
    }
  `;
  document.head.appendChild(styleEl);

  setTimeout(() => {
    document.body.classList.remove("theme-lavender-takeover");
    if (styleEl.parentNode) styleEl.parentNode.removeChild(styleEl);
  }, 8000);
}

function previewConfetti() {
  const canvas = document.getElementById("confetti-canvas");
  if (!canvas) return;

  const ctx = canvas.getContext("2d");
  canvas.width = window.innerWidth;
  canvas.height = window.innerHeight;

  const colors = ["#A78BFA", "#C4B5FD", "#8B5CF6", "#F2F0F7", "#E6B86B", "#55C99A"];
  const particles = [];
  for (let i = 0; i < 90; i++) {
    particles.push({
      x: Math.random() * canvas.width,
      y: Math.random() * -canvas.height * 0.5,
      w: Math.random() * 8 + 4,
      h: Math.random() * 10 + 6,
      color: colors[Math.floor(Math.random() * colors.length)],
      vx: (Math.random() - 0.5) * 3,
      vy: Math.random() * 4 + 3,
      rotation: Math.random() * 360,
      vRot: (Math.random() - 0.5) * 8
    });
  }

  let animationFrame;
  const startTime = Date.now();

  function animate() {
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    particles.forEach(p => {
      p.x += p.vx;
      p.y += p.vy;
      p.rotation += p.vRot;

      ctx.save();
      ctx.translate(p.x, p.y);
      ctx.rotate((p.rotation * Math.PI) / 180);
      ctx.fillStyle = p.color;
      ctx.fillRect(-p.w / 2, -p.h / 2, p.w, p.h);
      ctx.restore();
    });

    if (Date.now() - startTime < 4500) {
      animationFrame = requestAnimationFrame(animate);
    } else {
      ctx.clearRect(0, 0, canvas.width, canvas.height);
      cancelAnimationFrame(animationFrame);
    }
  }

  animate();
  showToast("Celebration confetti triggered locally (5s)", "info", 3000);
}

function previewVipBadge() {
  const avatar = document.querySelector(".profile-avatar");
  if (!avatar) return;

  const oldBadge = document.getElementById("temp-vip-badge");
  if (oldBadge) oldBadge.remove();

  const badge = document.createElement("div");
  badge.id = "temp-vip-badge";
  badge.innerHTML = "★ VIP";
  badge.style.cssText = `
    position: absolute;
    bottom: -6px;
    right: -10px;
    background: linear-gradient(135deg, #E6B86B, #FBBF24);
    color: #0B0B0F;
    font-size: 9px;
    font-weight: 800;
    padding: 1px 4px;
    border-radius: 4px;
    border: 1px solid #FFF;
    box-shadow: 0 0 8px rgba(230, 184, 107, 0.8);
    pointer-events: none;
    animation: fadeIn 200ms ease;
  `;
  avatar.style.position = "relative";
  avatar.appendChild(badge);

  showToast("VIP badge applied to local profile preview (15s)", "success", 4000);

  setTimeout(() => {
    if (badge.parentNode) badge.parentNode.removeChild(badge);
  }, 15000);
}

function previewPianoCelebration() {
  const existing = document.getElementById("temp-piano-visualizer");
  if (existing) existing.remove();

  const bar = document.createElement("div");
  bar.id = "temp-piano-visualizer";
  bar.className = "piano-visualizer";

  // Generate an octave of keys
  const keysPattern = [
    { type: "white", note: "C" },
    { type: "black", note: "C#" },
    { type: "white", note: "D" },
    { type: "black", note: "D#" },
    { type: "white", note: "E" },
    { type: "white", note: "F" },
    { type: "black", note: "F#" },
    { type: "white", note: "G" },
    { type: "black", note: "G#" },
    { type: "white", note: "A" },
    { type: "black", note: "A#" },
    { type: "white", note: "B" },
    { type: "white", note: "C5" },
    { type: "black", note: "C#5" },
    { type: "white", note: "D5" },
    { type: "black", note: "D#5" },
    { type: "white", note: "E5" }
  ];

  bar.innerHTML = keysPattern.map((k, i) => `
    <div class="piano-key ${k.type}" data-idx="${i}"></div>
  `).join("");

  document.body.appendChild(bar);

  // Play animation sequence
  const keyEls = bar.querySelectorAll(".piano-key");
  const chordSequence = [
    [0, 4, 7],     // C Major
    [2, 7, 11],    // G/B
    [9, 12, 16],   // A Minor
    [5, 9, 12]     // F Major
  ];

  let step = 0;
  const interval = setInterval(() => {
    keyEls.forEach(k => k.classList.remove("active"));
    const chord = chordSequence[step % chordSequence.length];
    chord.forEach(idx => {
      if (keyEls[idx]) keyEls[idx].classList.add("active");
    });
    step++;
  }, 350);

  showToast("Piano visual celebration active locally (8s)", "info", 4000);

  setTimeout(() => {
    clearInterval(interval);
    if (bar.parentNode) bar.parentNode.removeChild(bar);
  }, 8000);
}

function previewCustomShimmer() {
  const cards = document.querySelectorAll(".card, .kpi-card");
  cards.forEach(c => {
    c.style.transition = "box-shadow 400ms ease, border-color 400ms ease";
    c.style.borderColor = "#A78BFA";
    c.style.boxShadow = "0 0 15px rgba(167, 139, 250, 0.35)";
  });

  showToast("Luminous wave shimmer effect preview (10s)", "info", 3000);

  setTimeout(() => {
    cards.forEach(c => {
      c.style.borderColor = "";
      c.style.boxShadow = "";
    });
  }, 10000);
}
