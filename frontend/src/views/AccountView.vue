<script setup lang="ts">
import { ref } from "vue";
import { RouterLink, useRouter } from "vue-router";
import ChangePasswordModal from "@/components/user/ChangePasswordModal.vue";
import PageHeader from "@/components/layout/PageHeader.vue";
import VpButton from "@/components/ui/VpButton.vue";
import VpCard from "@/components/ui/VpCard.vue";
import { useAuthStore } from "@/stores/auth";

const auth = useAuthStore();
const router = useRouter();

const modalOpen = ref(false);
const success = ref<string | null>(null);

async function signOut() {
  await auth.logout();
  await router.replace({ name: "login" });
}

function openChangePassword() {
  success.value = null;
  modalOpen.value = true;
}

function onPasswordChanged() {
  success.value = "Password updated.";
}
</script>

<template>
  <PageHeader title="Account" />
  <VpCard class="space-y-3">
    <div>
      <p class="text-xs text-muted">Name</p>
      <p class="font-semibold">{{ auth.user?.personName }}</p>
    </div>
    <div>
      <p class="text-xs text-muted">Email</p>
      <p class="font-semibold">{{ auth.user?.email }}</p>
    </div>
    <div>
      <p class="text-xs text-muted">Role</p>
      <p class="font-semibold capitalize">{{ auth.user?.companyRole }}</p>
    </div>
    <RouterLink v-if="auth.isAdmin" to="/users" class="text-sm text-primary inline-block">Manage users</RouterLink>

    <p v-if="success" class="text-sm text-muted">{{ success }}</p>

    <VpButton variant="secondary" block @click="openChangePassword">Change password</VpButton>
    <VpButton variant="secondary" block @click="signOut">Sign out</VpButton>
  </VpCard>

  <ChangePasswordModal
    :open="modalOpen"
    @close="modalOpen = false"
    @changed="onPasswordChanged"
  />
</template>
