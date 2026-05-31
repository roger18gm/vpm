type ApiErrorPayload = {
  message?: unknown;
  title?: unknown;
  detail?: unknown;
  error?: unknown;
  errors?: Record<string, unknown>;
};

function firstValidationMessage(errors: Record<string, unknown>): string | null {
  for (const value of Object.values(errors)) {
    if (typeof value === "string" && value.trim()) {
      return value.trim();
    }
    if (Array.isArray(value)) {
      const first = value.find((item) => typeof item === "string" && item.trim());
      if (typeof first === "string") {
        return first.trim();
      }
    }
  }
  return null;
}

export function parseApiErrorMessage(body: string, status?: number): string {
  const trimmed = body.trim();

  if (!trimmed) {
    if (status === 401) return "Invalid email or password.";
    if (status === 403) return "You do not have permission to do that.";
    if (status && status >= 500) return "Something went wrong on our end. Please try again.";
    return status ? `Request failed (${status}).` : "Request failed.";
  }

  if (trimmed.startsWith("{") || trimmed.startsWith("[")) {
    try {
      const data = JSON.parse(trimmed) as ApiErrorPayload;
      const candidates = [data.message, data.detail, data.title, data.error];
      for (const candidate of candidates) {
        if (typeof candidate === "string" && candidate.trim()) {
          return candidate.trim();
        }
      }
      if (data.errors && typeof data.errors === "object") {
        const validation = firstValidationMessage(data.errors);
        if (validation) return validation;
      }
    } catch {
      // fall through to raw text
    }
  }

  return trimmed;
}
