import { defineStore } from "pinia";
import { ref } from "vue";
import { request } from "@/lib/api";
import type { CreateUserInput, UserAdmin } from "@/types/user";

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

export const useUsersStore = defineStore("users", () => {
  const users = ref<UserAdmin[]>([]);
  const loading = ref(false);
  const error = ref<string | null>(null);

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

  return { users, loading, error, fetchUsers, createUser, updateRole };
});
