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

export type GustoDirectoryJob = {
  uuid: string;
  title: string;
  primary: boolean;
};

export type GustoDirectoryEmployee = {
  uuid: string;
  firstName: string;
  lastName: string;
  email: string;
  jobs: GustoDirectoryJob[];
};

export type GustoEmployeeLink = {
  personId: number;
  gustoEmployeeUuid: string;
  gustoJobUuid: string;
};

export type GustoEmployeeDirectory = {
  employees: GustoDirectoryEmployee[];
  links: GustoEmployeeLink[];
};
