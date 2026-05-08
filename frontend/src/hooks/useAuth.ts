import { useCallback, useEffect, useMemo, useState } from "react";

export type AuthUser = {
  authUserId: string;
  personId: number;
  companyId: number;
  companyRole: string;
  personName: string;
  email: string;
};

export type AuthStatus = {
  isAuthenticated: boolean;
  canBootstrap: boolean;
  user: AuthUser | null;
};

const API_URL = import.meta.env.VITE_API_URL ?? "https://vision-paint-api.azurewebsites.net/api";

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_URL}${path}`, {
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
      ...(init?.headers ?? {}),
    },
    ...init,
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

export function useAuth() {
  const [status, setStatus] = useState<AuthStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const data = await request<AuthStatus>("/auth/status");
      setStatus(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to load auth state.");
      setStatus({ isAuthenticated: false, canBootstrap: false, user: null });
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const login = useCallback(async (email: string, password: string) => {
    const user = await request<AuthUser>("/auth/login", {
      method: "POST",
      body: JSON.stringify({ email, password }),
    });
    setStatus({ isAuthenticated: true, canBootstrap: false, user });
    return user;
  }, []);

  const bootstrap = useCallback(async (name: string, email: string, password: string) => {
    const user = await request<AuthUser>("/auth/bootstrap", {
      method: "POST",
      body: JSON.stringify({ name, email, password }),
    });
    setStatus({ isAuthenticated: true, canBootstrap: false, user });
    return user;
  }, []);

  const logout = useCallback(async () => {
    await request<void>("/auth/logout", { method: "POST", headers: {} });
    setStatus({ isAuthenticated: false, canBootstrap: false, user: null });
  }, []);

  return useMemo(
    () => ({
      status,
      loading,
      error,
      refresh,
      login,
      bootstrap,
      logout,
    }),
    [status, loading, error, refresh, login, bootstrap, logout]
  );
}
