<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { Icon } from "@iconify/vue";
import PageHeader from "@/components/layout/PageHeader.vue";
import CreateUserModal from "@/components/user/CreateUserModal.vue";
import VpButton from "@/components/ui/VpButton.vue";
import VpCard from "@/components/ui/VpCard.vue";
import { useAuthStore } from "@/stores/auth";
import { useUsersStore } from "@/stores/users";
import type { UserAdmin } from "@/types/user";

const auth = useAuthStore();
const usersStore = useUsersStore();
const showCreate = ref(false);
const roleErrors = ref<Record<number, string>>({});

const roleOptions = computed(() => {
  const roles = ["crew", "manager", "admin"];
  if (auth.user?.companyRole === "owner") {
    roles.push("owner");
  }
  return roles;
});

onMounted(() => {
  void usersStore.fetchUsers();
});

function formatLastLogin(value: string | null): string {
  if (!value) {
    return "—";
  }
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "—";
  }
  return date.toLocaleString(undefined, {
    month: "short",
    day: "numeric",
    year: "numeric",
    hour: "numeric",
    minute: "2-digit",
  });
}

function statusLabel(user: UserAdmin): string {
  return user.membershipStatus.charAt(0).toUpperCase() + user.membershipStatus.slice(1);
}

function canChangeRole(user: UserAdmin): boolean {
  return user.personId !== auth.user?.personId;
}

async function onRoleChange(user: UserAdmin, role: string) {
  const previous = user.companyRole;
  if (role === previous) {
    return;
  }

  roleErrors.value = { ...roleErrors.value, [user.personId]: "" };
  try {
    await usersStore.updateRole(user.personId, role);
  } catch (err) {
    roleErrors.value = {
      ...roleErrors.value,
      [user.personId]: err instanceof Error ? err.message : "Unable to update role.",
    };
    await usersStore.fetchUsers();
  }
}

async function onCreated() {
  await usersStore.fetchUsers();
}
</script>

<template>
  <PageHeader title="Users" subtitle="Manage login accounts for your company">
    <template #actions>
      <VpButton @click="showCreate = true">
        <span class="inline-flex items-center gap-1">
          <Icon icon="mdi:plus" />
          New user
        </span>
      </VpButton>
    </template>
  </PageHeader>

  <p v-if="usersStore.loading" class="text-sm text-muted">Loading users…</p>
  <p v-else-if="usersStore.error" class="text-sm text-error">{{ usersStore.error }}</p>

  <VpCard v-else class="overflow-x-auto">
    <table class="w-full text-sm min-w-[640px]">
      <thead>
        <tr class="text-left text-xs uppercase text-muted border-b border-border">
          <th class="py-2 pr-3 font-semibold">Name</th>
          <th class="py-2 pr-3 font-semibold">Email</th>
          <th class="py-2 pr-3 font-semibold">Role</th>
          <th class="py-2 pr-3 font-semibold">Status</th>
          <th class="py-2 pr-3 font-semibold">Clock</th>
          <th class="py-2 font-semibold hidden md:table-cell">Last login</th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="user in usersStore.users"
          :key="user.personId"
          class="border-b border-border last:border-b-0"
          :class="!user.isActive ? 'opacity-60' : ''"
        >
          <td class="py-3 pr-3 font-medium">{{ user.name }}</td>
          <td class="py-3 pr-3 text-muted">{{ user.email }}</td>
          <td class="py-3 pr-3 min-w-[140px]">
            <select
              v-if="canChangeRole(user)"
              :value="user.companyRole"
              class="w-full border border-border rounded-md px-2 py-2 text-sm min-h-[44px] bg-surface capitalize"
              @change="onRoleChange(user, ($event.target as HTMLSelectElement).value)"
            >
              <option v-for="role in roleOptions" :key="role" :value="role">{{ role }}</option>
            </select>
            <span v-else class="capitalize">{{ user.companyRole }}</span>
            <p v-if="roleErrors[user.personId]" class="text-xs text-error mt-1">{{ roleErrors[user.personId] }}</p>
          </td>
          <td class="py-3 pr-3">
            <span
              class="inline-flex text-xs px-2 py-1 rounded-full border capitalize"
              :class="user.membershipStatus === 'active' ? 'border-primary/30 text-primary bg-primary/5' : 'border-border text-muted'"
            >
              {{ statusLabel(user) }}
            </span>
          </td>
          <td class="py-3 pr-3">
            <span v-if="user.isClockedIn" class="text-primary">On clock — {{ user.clockedInJobTitle ?? "Job" }}</span>
            <span v-else class="text-muted">—</span>
          </td>
          <td class="py-3 hidden md:table-cell text-muted">{{ formatLastLogin(user.lastLoginAt) }}</td>
        </tr>
      </tbody>
    </table>

    <p v-if="!usersStore.users.length" class="text-sm text-muted py-4">No users found.</p>
  </VpCard>

  <CreateUserModal :open="showCreate" @close="showCreate = false" @created="onCreated" />
</template>
