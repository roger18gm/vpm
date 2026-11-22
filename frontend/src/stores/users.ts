import { defineStore } from "pinia";
import { ref } from "vue";
import { request } from "@/lib/api";
import type {
  CreateUserInput,
  GustoDirectoryEmployee,
  GustoDirectoryJob,
  GustoEmployeeDirectory,
  GustoEmployeeLink,
  UserAdmin,
} from "@/types/user";

function normalizeUser(raw: Record<string, unknown>): UserAdmin {
  return {
    personId: Number(raw.personId ?? raw.PersonId ?? 0),
    authUserId: String(raw.authUserId ?? raw.AuthUserId ?? ""),
    name: String(raw.name ?? raw.Name ?? ""),
    email: String(raw.email ?? raw.Email ?? ""),
    companyRole: String(raw.companyRole ?? raw.CompanyRole ?? ""),
    membershipStatus: String(raw.membershipStatus ?? raw.MembershipStatus ?? ""),
    isActive: Boolean(raw.isActive ?? raw.IsActive ?? true),
    isClockedIn: Boolean(raw.isClockedIn ?? raw.IsClockedIn ?? false),
    clockedInJobTitle: (raw.clockedInJobTitle ?? raw.ClockedInJobTitle ?? null) as string | null,
    lastLoginAt: (raw.lastLoginAt ?? raw.LastLoginAt ?? null) as string | null,
  };
}

function normalizeJob(raw: Record<string, unknown>): GustoDirectoryJob {
  return {
    uuid: String(raw.uuid ?? raw.Uuid ?? ""),
    title: String(raw.title ?? raw.Title ?? ""),
    primary: Boolean(raw.primary ?? raw.Primary ?? false),
  };
}

function normalizeEmployee(raw: Record<string, unknown>): GustoDirectoryEmployee {
  const jobs = (raw.jobs ?? raw.Jobs ?? []) as Record<string, unknown>[];
  return {
    uuid: String(raw.uuid ?? raw.Uuid ?? ""),
    firstName: String(raw.firstName ?? raw.FirstName ?? ""),
    lastName: String(raw.lastName ?? raw.LastName ?? ""),
    email: String(raw.email ?? raw.Email ?? ""),
    jobs: Array.isArray(jobs) ? jobs.map(normalizeJob) : [],
  };
}

function normalizeLink(raw: Record<string, unknown>): GustoEmployeeLink {
  return {
    personId: Number(raw.personId ?? raw.PersonId ?? 0),
    gustoEmployeeUuid: String(raw.gustoEmployeeUuid ?? raw.GustoEmployeeUuid ?? ""),
    gustoJobUuid: String(raw.gustoJobUuid ?? raw.GustoJobUuid ?? ""),
  };
}

function normalizeDirectory(raw: Record<string, unknown>): GustoEmployeeDirectory {
  const employees = (raw.employees ?? raw.Employees ?? []) as Record<string, unknown>[];
  const links = (raw.links ?? raw.Links ?? []) as Record<string, unknown>[];
  return {
    employees: Array.isArray(employees) ? employees.map(normalizeEmployee) : [],
    links: Array.isArray(links) ? links.map(normalizeLink) : [],
  };
}

export const useUsersStore = defineStore("users", () => {
  const users = ref<UserAdmin[]>([]);
  const loading = ref(false);
  const error = ref<string | null>(null);
  const gustoDirectory = ref<GustoEmployeeDirectory | null>(null);
  const gustoDirectoryLoading = ref(false);
  const gustoDirectoryError = ref<string | null>(null);

  async function fetchUsers() {
    loading.value = true;
    error.value = null;
    try {
      const rows = await request<Record<string, unknown>[]>("/users");
      users.value = rows.map(normalizeUser);
    } catch (err) {
      error.value = err instanceof Error ? err.message : "Unable to load users.";
      users.value = [];
    } finally {
      loading.value = false;
    }
  }

  async function fetchGustoDirectory() {
    gustoDirectoryLoading.value = true;
    gustoDirectoryError.value = null;
    try {
      const raw = await request<Record<string, unknown>>("/users/gusto-directory");
      gustoDirectory.value = normalizeDirectory(raw);
    } catch (err) {
      gustoDirectory.value = null;
      gustoDirectoryError.value = err instanceof Error
        ? err.message
        : "The Gusto demo is not configured or could not be reached.";
    } finally {
      gustoDirectoryLoading.value = false;
    }
  }

  function rememberGustoLink(link: GustoEmployeeLink) {
    if (!gustoDirectory.value) {
      return;
    }

    const links = gustoDirectory.value.links.filter((existing) => existing.personId !== link.personId);
    gustoDirectory.value = { ...gustoDirectory.value, links: [...links, link] };
  }

  async function saveGustoLink(
    personId: number,
    input: { gustoEmployeeUuid: string; gustoJobUuid: string },
  ): Promise<GustoEmployeeLink> {
    const raw = await request<Record<string, unknown>>(`/users/${personId}/gusto-link`, {
      method: "PUT",
      body: JSON.stringify(input),
    });
    return normalizeLink(raw);
  }

  async function openGustoOnboarding(flowType: "add_employees" | "company_onboarding"): Promise<string> {
    const raw = await request<Record<string, unknown>>("/users/gusto-onboarding", {
      method: "POST",
      body: JSON.stringify({ flowType }),
    });
    const url = String(raw.url ?? raw.Url ?? "");
    if (!url.startsWith("https://flows.gusto-demo.com/")) {
      throw new Error("The Gusto demo is not configured or could not be reached.");
    }
    return url;
  }

  async function createUser(input: CreateUserInput): Promise<UserAdmin> {
    const raw = await request<Record<string, unknown>>("/users", {
      method: "POST",
      body: JSON.stringify(input),
    });
    const user = normalizeUser(raw);
    users.value = [user, ...users.value];
    return user;
  }

  async function updateRole(personId: number, companyRole: string): Promise<UserAdmin> {
    const raw = await request<Record<string, unknown>>(`/users/${personId}`, {
      method: "PATCH",
      body: JSON.stringify({ companyRole }),
    });
    const user = normalizeUser(raw);
    users.value = users.value.map((existing) => (existing.personId === personId ? user : existing));
    return user;
  }

  return {
    users,
    loading,
    error,
    gustoDirectory,
    gustoDirectoryLoading,
    gustoDirectoryError,
    fetchUsers,
    fetchGustoDirectory,
    rememberGustoLink,
    saveGustoLink,
    openGustoOnboarding,
    createUser,
    updateRole,
  };
});
