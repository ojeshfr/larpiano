/**
 * Toast Notification Service
 */

let container = null;

function getContainer() {
  if (!container) {
    container = document.createElement("div");
    container.className = "toast-container";
    document.body.appendChild(container);
  }
  return container;
}

export function showToast(message, type = "info", duration = 4000) {
  const c = getContainer();
  const toast = document.createElement("div");
  toast.className = `toast toast-${type}`;

  const icon = type === "success" ? "✓" : (type === "warning" ? "⚠" : (type === "danger" ? "✕" : "⚡"));

  toast.innerHTML = `
    <span style="color: var(--accent); font-weight: bold;">${icon}</span>
    <span style="flex: 1;">${message}</span>
  `;

  c.appendChild(toast);

  setTimeout(() => {
    toast.style.opacity = "0";
    toast.style.transform = "translateY(10px)";
    toast.style.transition = "all 200ms ease";
    setTimeout(() => {
      if (toast.parentNode) {
        toast.parentNode.removeChild(toast);
      }
    }, 200);
  }, duration);
}
