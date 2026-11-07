import { apiFetch } from "./client.js";

export async function uploadAsset({ prefix, filename, file }) {
  const reader = new FileReader();
  const base64Promise = new Promise((resolve, reject) => {
    reader.onload = () => {
      const b64 = reader.result.split(",")[1];
      resolve(b64);
    };
    reader.onerror = reject;
  });
  reader.readAsDataURL(file);
  const dataBase64 = await base64Promise;

  return await apiFetch("/api/admin/storage/upload", {
    method: "POST",
    body: {
      prefix,
      filename,
      content_type: file.type || "application/octet-stream",
      data_base64: dataBase64
    }
  });
}

export async function exportData(type) {
  return await apiFetch("/api/admin/storage/export", {
    method: "POST",
    body: { type }
  });
}
