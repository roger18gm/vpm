<script setup lang="ts">
import { ref, watch } from "vue";
import VpButton from "@/components/ui/VpButton.vue";
import VpCard from "@/components/ui/VpCard.vue";
import VpInput from "@/components/ui/VpInput.vue";
import { useAuthStore } from "@/stores/auth";

const props = defineProps<{ open: boolean }>();
const emit = defineEmits<{ close: []; changed: [] }>();

const auth = useAuthStore();

const currentPassword = ref("");
const newPassword = ref("");
const confirmPassword = ref("");
const busy = ref(false);
const error = ref<string | null>(null);

watch(
  () => props.open,
  (isOpen) => {
    if (!isOpen) return;
    currentPassword.value = "";
    newPassword.value = "";
    confirmPassword.value = "";
    error.value = null;
  }
);

async function submit() {
  error.value = null;

  if (!currentPassword.value || !newPassword.value || !confirmPassword.value) {
    error.value = "All password fields are required.";
    return;
  }
  if (newPassword.value.length < 8) {
    error.value = "New password must be at least 8 characters.";
    return;
  }
  if (newPassword.value !== confirmPassword.value) {
    error.value = "New passwords do not match.";
    return;
  }

  busy.value = true;
  try {
    await auth.changePassword(currentPassword.value, newPassword.value);
    emit("changed");
    emit("close");
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Unable to change password.";
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <div
    v-if="open"
    class="fixed inset-0 z-30 flex items-center justify-center bg-black/40 p-4"
    @click.self="emit('close')"
  >
    <VpCard class="w-full max-w-md space-y-3">
      <div class="flex items-start justify-between gap-3">
        <div>
          <h2 class="text-lg font-bold">Change password</h2>
          <p class="text-sm text-muted">Other signed-in devices will be signed out.</p>
        </div>
        <button type="button" class="text-sm text-muted hover:text-text" @click="emit('close')">Close</button>
      </div>

      <form class="space-y-3" @submit.prevent="submit">
        <VpInput
          v-model="currentPassword"
          label="Current password"
          type="password"
          required
          show-password-toggle
          autocomplete="current-password"
        />
        <VpInput
          v-model="newPassword"
          label="New password"
          type="password"
          required
          show-password-toggle
          autocomplete="new-password"
        />
        <VpInput
          v-model="confirmPassword"
          label="Confirm new password"
          type="password"
          required
          show-password-toggle
          autocomplete="new-password"
        />

        <p v-if="error" class="text-sm text-error">{{ error }}</p>

        <div class="grid grid-cols-2 gap-3 pt-1">
          <VpButton type="button" variant="secondary" :disabled="busy" @click="emit('close')">Cancel</VpButton>
          <VpButton type="submit" :disabled="busy">{{ busy ? "Updating…" : "Update password" }}</VpButton>
        </div>
      </form>
    </VpCard>
  </div>
</template>
