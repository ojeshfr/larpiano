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

  const iconNode = document.createElement("span");
  iconNode.style.color = "var(--accent)";
  iconNode.style.fontWeight = "bold";
  iconNode.textContent = icon;
  const messageNode = document.createElement("span");
  messageNode.style.flex = "1";
  messageNode.textContent = String(message ?? "");
  toast.append(iconNode, messageNode);

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
