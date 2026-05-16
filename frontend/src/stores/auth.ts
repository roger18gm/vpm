import { defineStore } from "pinia";
import { computed, ref } from "vue";
import { request, setCsrfToken } from "@/lib/api";
import type { AuthStatus, AuthUser } from "@/types/auth";
import { isManagerRole } from "@/types/auth";

export const useAuthStore = defineStore("auth", () => {
  const initialized = ref(false);
  const loading = ref(false);
  const error = ref<string | null>(null);
  const isAuthenticated = ref(false);
  const canBootstrap = ref(false);
  const user = ref<AuthUser | null>(null);

  const isManager = computed(() => isManagerRole(user.value?.companyRole));
  const defaultHome = computed(() => (isManager.value ? "/dashboard" : "/jobs"));

  function applyStatus(data: AuthStatus) {
    setCsrfToken(data.csrfToken);
    isAuthenticated.value = data.isAuthenticated;
    canBootstrap.value = data.canBootstrap;
    user.value = data.user;
  }

  async function initialize() {
    if (initialized.value) return;
    loading.value = true;
    error.value = null;
    try {
      const data = await request<AuthStatus>("/auth/status");
      applyStatus(data);
    } catch (err) {
      error.value = err instanceof Error ? err.message : "Unable to load session.";
      setCsrfToken(null);
      applyStatus({ isAuthenticated: false, canBootstrap: false, user: null, csrfToken: "" });
    } finally {
      loading.value = false;
      initialized.value = true;
    }
  }

  async function login(email: string, password: string) {
    const loggedIn = await request<AuthUser>("/auth/login", {
      method: "POST",
      body: JSON.stringify({ email, password }),
    });
    applyStatus({
      isAuthenticated: true,
      canBootstrap: false,
      user: loggedIn,
      csrfToken: loggedIn.csrfToken,
    });
    return loggedIn;
  }

  async function bootstrap(name: string, email: string, password: string) {
    const loggedIn = await request<AuthUser>("/auth/bootstrap", {
      method: "POST",
      body: JSON.stringify({ name, email, password }),
    });
    applyStatus({
      isAuthenticated: true,
      canBootstrap: false,
      user: loggedIn,
      csrfToken: loggedIn.csrfToken,
    });
    return loggedIn;
  }

  async function logout() {
    await request<void>("/auth/logout", { method: "POST" });
    applyStatus({ isAuthenticated: false, canBootstrap: false, user: null, csrfToken: "" });
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
  };
});
