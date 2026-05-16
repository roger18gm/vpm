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

export const MANAGER_ROLES = ["owner", "admin", "manager"] as const;

export function isManagerRole(role: string | undefined): boolean {
  return role !== undefined && (MANAGER_ROLES as readonly string[]).includes(role);
}
