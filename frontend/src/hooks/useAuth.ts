import { useCallback, useEffect, useMemo, useState } from "react";
import { request, setCsrfToken } from "../lib/api";

export type AuthUser = {
  authUserId: string;
  personId: number;
  companyId: number;
  companyRole: string;
  personName: string;
  email: string;
  csrfToken: string;
};

export type AuthStatus = {
  isAuthenticated: boolean;
  canBootstrap: boolean;
  user: AuthUser | null;
  csrfToken: string;
};

export function useAuth() {
  const [status, setStatus] = useState<AuthStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const data = await request<AuthStatus>("/auth/status");
      setCsrfToken(data.csrfToken);
      setStatus(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to load auth state.");
      setCsrfToken(null);
      setStatus({ isAuthenticated: false, canBootstrap: false, user: null, csrfToken: "" });
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
    setCsrfToken(user.csrfToken);
    setStatus({ isAuthenticated: true, canBootstrap: false, user, csrfToken: user.csrfToken });
    return user;
  }, []);

  const bootstrap = useCallback(async (name: string, email: string, password: string) => {
    const user = await request<AuthUser>("/auth/bootstrap", {
      method: "POST",
      body: JSON.stringify({ name, email, password }),
    });
    setCsrfToken(user.csrfToken);
    setStatus({ isAuthenticated: true, canBootstrap: false, user, csrfToken: user.csrfToken });
    return user;
  }, []);

  const logout = useCallback(async () => {
    try {
      await request<void>("/auth/logout", { method: "POST" });
      setStatus({ isAuthenticated: false, canBootstrap: false, user: null, csrfToken: "" });
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to sign out.");
      throw err;
    }
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
