import { defineStore } from "pinia";
import { computed, ref } from "vue";
import { getAccessToken, request, setAccessToken, setRefreshHandler } from "@/lib/api";
import { useClockStore } from "@/stores/clock";
import type { AuthStatus, AuthTokenResponse, AuthUser, StoredAuth } from "@/types/auth";
import { AUTH_STORAGE_KEY, isManagerRole } from "@/types/auth";

function loadStoredAuth(): StoredAuth | null {
  try {
    const raw = localStorage.getItem(AUTH_STORAGE_KEY) ?? sessionStorage.getItem(AUTH_STORAGE_KEY);
    return raw ? (JSON.parse(raw) as StoredAuth) : null;
  } catch {
    return null;
  }
}

function saveStoredAuth(session: StoredAuth | null) {
  if (session) {
    localStorage.setItem(AUTH_STORAGE_KEY, JSON.stringify(session));
    sessionStorage.removeItem(AUTH_STORAGE_KEY);
  } else {
    localStorage.removeItem(AUTH_STORAGE_KEY);
    sessionStorage.removeItem(AUTH_STORAGE_KEY);
  }
}

function isAccessTokenValid(expiresAt: string) {
  return new Date(expiresAt).getTime() > Date.now() + 30_000;
}

function readString(record: Record<string, unknown>, camel: string, pascal: string) {
  const value = record[camel] ?? record[pascal];
  return typeof value === "string" ? value : "";
}

function normalizeAuthUser(raw: unknown): AuthUser {
  const record = (raw ?? {}) as Record<string, unknown>;
  return {
    authUserId: readString(record, "authUserId", "AuthUserId"),
    personId: Number(record.personId ?? record.PersonId ?? 0),
    companyId: Number(record.companyId ?? record.CompanyId ?? 0),
    companyRole: readString(record, "companyRole", "CompanyRole"),
    personName: readString(record, "personName", "PersonName"),
    email: readString(record, "email", "Email"),
  };
}

function normalizeAuthTokenResponse(raw: unknown): AuthTokenResponse {
  const record = (raw ?? {}) as Record<string, unknown>;
  const user = normalizeAuthUser(record.user ?? record.User);
  const accessToken = readString(record, "accessToken", "AccessToken");
  const refreshTokenValue = readString(record, "refreshToken", "RefreshToken");
  const accessTokenExpiresAt = readString(record, "accessTokenExpiresAt", "AccessTokenExpiresAt");

  if (!accessToken) {
    throw new Error("Auth response did not include an access token.");
  }

  if (!user.companyRole) {
    throw new Error("Auth response did not include a company role.");
  }

  return {
    accessToken,
    refreshToken: refreshTokenValue,
    accessTokenExpiresAt,
    user,
  };
}

export const useAuthStore = defineStore("auth", () => {
  const initialized = ref(false);
  const loading = ref(false);
  const error = ref<string | null>(null);
  const isAuthenticated = ref(false);
  const canBootstrap = ref(false);
  const user = ref<AuthUser | null>(null);
  const refreshToken = ref<string | null>(null);

  const isManager = computed(() => isManagerRole(user.value?.companyRole));
  const defaultHome = computed(() => (isManager.value ? "/dashboard" : "/jobs"));

  function applyStatus(data: AuthStatus) {
    isAuthenticated.value = data.isAuthenticated;
    canBootstrap.value = data.canBootstrap;
    user.value = data.user ? normalizeAuthUser(data.user) : null;
    if (!data.isAuthenticated) {
      setAccessToken(null);
      refreshToken.value = null;
    }
  }

  function applyTokenResponse(raw: AuthTokenResponse) {
    const data = normalizeAuthTokenResponse(raw);
    setAccessToken(data.accessToken);
    refreshToken.value = data.refreshToken;
    isAuthenticated.value = true;
    canBootstrap.value = false;
    user.value = data.user;
    saveStoredAuth({
      accessToken: data.accessToken,
      refreshToken: data.refreshToken,
      accessTokenExpiresAt: data.accessTokenExpiresAt,
      user: data.user,
    });
  }

  function clearSession() {
    setAccessToken(null);
    refreshToken.value = null;
    saveStoredAuth(null);
    applyStatus({ isAuthenticated: false, canBootstrap: false, user: null });
  }

  async function refreshSession(): Promise<boolean> {
    const token = refreshToken.value ?? loadStoredAuth()?.refreshToken;
    if (!token) {
      return false;
    }

    try {
      const data = await request<AuthTokenResponse>(
        "/auth/refresh",
        {
          method: "POST",
          body: JSON.stringify({ refreshToken: token }),
        },
        false
      );
      applyTokenResponse(data);
      return true;
    } catch {
      clearSession();
      return false;
    }
  }

  async function initialize() {
    if (initialized.value) return;
    loading.value = true;
    error.value = null;

    setRefreshHandler(refreshSession);

    try {
      const stored = loadStoredAuth();
      if (stored) {
        refreshToken.value = stored.refreshToken;
        if (isAccessTokenValid(stored.accessTokenExpiresAt)) {
          const cachedAccessToken = stored.accessToken;
          setAccessToken(cachedAccessToken);

          const currentUser = normalizeAuthUser(await request<AuthUser>("/auth/me"));
          applyStatus({ isAuthenticated: true, canBootstrap: false, user: currentUser });

          if (getAccessToken() === cachedAccessToken) {
            saveStoredAuth({
              ...stored,
              user: currentUser,
            });
          }

          await useClockStore().hydrateFromServer();
          return;
        }

        if (await refreshSession()) {
          await useClockStore().hydrateFromServer();
          return;
        }
      }

      const data = await request<AuthStatus>("/auth/status");
      applyStatus(data);
      if (data.isAuthenticated) {
        await useClockStore().hydrateFromServer();
      }
    } catch (err) {
      error.value = err instanceof Error ? err.message : "Unable to load session.";
      clearSession();
    } finally {
      loading.value = false;
      initialized.value = true;
    }
  }

  async function login(email: string, password: string) {
    const data = await request<AuthTokenResponse>("/auth/login", {
      method: "POST",
      body: JSON.stringify({ email, password }),
    }, false);
    applyTokenResponse(data);
    await useClockStore().hydrateFromServer();
    return data.user;
  }

  async function bootstrap(name: string, email: string, password: string) {
    const data = await request<AuthTokenResponse>("/auth/bootstrap", {
      method: "POST",
      body: JSON.stringify({ name, email, password }),
    }, false);
    applyTokenResponse(data);
    await useClockStore().hydrateFromServer();
    return data.user;
  }

  async function logout() {
    try {
      if (getAccessToken()) {
        await request<void>("/auth/logout", { method: "POST" });
      }
    } finally {
      clearSession();
    }
  }

  return {
    initialized,
    loading,
    error,
    isAuthenticated,
    canBootstrap,
    user,
    isManager,
    defaultHome,
    initialize,
    login,
    bootstrap,
    logout,
    refreshSession,
  };
});
