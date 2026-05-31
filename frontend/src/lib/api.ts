const API_URL = import.meta.env.VITE_API_URL ?? "https://vision-paint-api.azurewebsites.net/api";

export function resolveAssetUrl(url: string): string {
  if (url.startsWith("http://") || url.startsWith("https://")) {
    return url;
  }
  const origin = API_URL.replace(/\/api\/?$/i, "");
  return `${origin}${url.startsWith("/") ? url : `/${url}`}`;
}

let accessToken: string | null = null;
let refreshHandler: (() => Promise<boolean>) | null = null;

export function setAccessToken(token: string | null) {
  accessToken = token && token.length > 0 ? token : null;
}

export function getAccessToken() {
  return accessToken;
}

export function setRefreshHandler(handler: (() => Promise<boolean>) | null) {
  refreshHandler = handler;
}

async function fetchWithAuth(path: string, init: RequestInit, allowRetry: boolean): Promise<Response> {
  const headers = new Headers(init.headers);

  if (accessToken) {
    headers.set("Authorization", `Bearer ${accessToken}`);
  }

  const response = await fetch(`${API_URL}${path}`, {
    ...init,
    headers,
  });

  if (response.status === 401 && allowRetry && refreshHandler && !path.includes("/auth/login") && !path.includes("/auth/refresh")) {
    const refreshed = await refreshHandler();
    if (refreshed) {
      return fetchWithAuth(path, init, false);
    }
  }

  return response;
}

export async function request<T>(path: string, init?: RequestInit, allowRetry = true): Promise<T> {
  const headers = new Headers(init?.headers);

  if (!headers.has("Content-Type") && init?.body) {
    headers.set("Content-Type", "application/json");
  }

  const response = await fetchWithAuth(path, {
    ...init,
    headers,
  }, allowRetry);

  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || `Request failed with ${response.status}`);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

export async function uploadForm<T>(path: string, form: FormData): Promise<T> {
  const response = await fetchWithAuth(path, {
    method: "POST",
    body: form,
  }, true);

  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || `Upload failed with ${response.status}`);
  }

  return (await response.json()) as T;
}
