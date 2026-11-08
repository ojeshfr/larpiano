// Text and attribute encoding for data received from APIs or users.
export function escapeText(value) {
  if (value === null || value === undefined) return "";
  if (typeof value === "object") {
    try { value = JSON.stringify(value); } catch { return "[unavailable]"; }
  }
  return String(value).replace(/[&<>"']/g, c => ({"&":"&amp;", "<":"&lt;", ">":"&gt;", "\"":"&quot;", "'":"&#39;"})[c]);
}

export function safeHttpsUrl(value) {
  if (typeof value !== "string") return "";
  try {
    const url = new URL(value);
    return url.protocol === "https:" && !url.username && !url.password ? url.href : "";
  } catch { return ""; }
}
