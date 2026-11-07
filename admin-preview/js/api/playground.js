import { apiFetch } from "./client.js";

export async function triggerPlaygroundEffect({ effectType, targetUserId = null, durationSeconds = 30 }) {
  return await apiFetch("/api/admin/playground/trigger", {
    method: "POST",
    body: {
      effect_type: effectType,
      target_user_id: targetUserId,
      duration_seconds: durationSeconds
    }
  });
}
