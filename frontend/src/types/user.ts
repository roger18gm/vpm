export type UserAdmin = {
  personId: number;
  authUserId: string;
  name: string;
  email: string;
  companyRole: string;
  membershipStatus: string;
  isActive: boolean;
  isClockedIn: boolean;
  clockedInJobTitle: string | null;
  lastLoginAt: string | null;
};

export type CreateUserInput = {
  name: string;
  email: string;
  password: string;
  companyRole: string;
};

export const USER_ROLES = ["crew", "manager", "admin", "owner"] as const;
