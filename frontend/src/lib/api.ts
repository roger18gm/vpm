const API_URL = import.meta.env.VITE_API_URL ?? "https://vision-paint-api.azurewebsites.net/api";

let csrfToken: string | null = null;

export function setCsrfToken(token: string | null) {
  csrfToken = token && token.length > 0 ? token : null;
}

export async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const method = (init?.method ?? "GET").toUpperCase();
  const headers = new Headers(init?.headers);

  if (method !== "GET" && method !== "HEAD" && method !== "OPTIONS" && method !== "TRACE" && csrfToken) {
    headers.set("X-CSRF-TOKEN", csrfToken);
  }

  if (!headers.has("Content-Type") && init?.body) {
    headers.set("Content-Type", "application/json");
  }

  const response = await fetch(`${API_URL}${path}`, {
    credentials: "include",
    ...init,
    headers,
  });

  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || `Request failed with ${response.status}`);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}
