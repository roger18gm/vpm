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

export type AuthTokenResponse = {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAt: string;
  user: AuthUser;
};

export const MANAGER_ROLES = ["owner", "admin", "manager"] as const;

export const ADMIN_ROLES = ["owner", "admin"] as const;

export function isManagerRole(role: string | undefined): boolean {
  return role !== undefined && (MANAGER_ROLES as readonly string[]).includes(role);
}

export function isAdminRole(role: string | undefined): boolean {
  return role !== undefined && (ADMIN_ROLES as readonly string[]).includes(role);
}

export type StoredAuth = {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAt: string;
  user: AuthUser;
};

export const AUTH_STORAGE_KEY = "visionpaint.auth";
