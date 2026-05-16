<script setup lang="ts">
import { useRouter } from "vue-router";
import PageHeader from "@/components/layout/PageHeader.vue";
import VpCard from "@/components/ui/VpCard.vue";
import VpButton from "@/components/ui/VpButton.vue";
import { useAuthStore } from "@/stores/auth";

const auth = useAuthStore();
const router = useRouter();

async function signOut() {
  await auth.logout();
  await router.replace({ name: "login" });
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
    <VpButton variant="secondary" block @click="signOut">Sign out</VpButton>
  </VpCard>
</template>
