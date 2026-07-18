<script setup lang="ts">
import { computed, ref } from "vue";
import { RouterLink, useRoute, useRouter } from "vue-router";
import VpAlert from "@/components/ui/VpAlert.vue";
import VpButton from "@/components/ui/VpButton.vue";
import VpInput from "@/components/ui/VpInput.vue";
import { useAuthStore } from "@/stores/auth";

const auth = useAuthStore();
const route = useRoute();
const router = useRouter();

const token = computed(() => {
  const value = route.query.token;
  return typeof value === "string" ? value : "";
});

const password = ref("");
const confirm = ref("");
const busy = ref(false);
const error = ref<string | null>(null);

async function onSubmit() {
  error.value = null;
  if (!token.value) {
    error.value = "This reset link is missing a token. Request a new one from the sign-in page.";
    return;
  }
  if (password.value.length < 8) {
    error.value = "Password must be at least 8 characters.";
    return;
  }
  if (password.value !== confirm.value) {
    error.value = "Passwords do not match.";
    return;
  }

  busy.value = true;
  try {
    await auth.resetPassword(token.value, password.value);
    await router.replace({ name: "login", query: { reset: "1" } });
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Unable to reset password.";
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <section class="w-full max-w-sm bg-surface border border-border rounded-lg p-6 shadow-sm">
    <p class="text-xs uppercase text-primary font-semibold mb-1">VisionPaint</p>
    <h1 class="text-2xl font-bold mb-6">Set a new password</h1>

    <p v-if="!token" class="text-sm text-error mb-4">
      This reset link is invalid. Request a new one from the sign-in page.
    </p>

    <form v-else class="space-y-3" @submit.prevent="onSubmit">
      <VpInput v-model="password" label="New password" type="password" required show-password-toggle />
      <VpInput v-model="confirm" label="Confirm password" type="password" required show-password-toggle />
      <VpButton type="submit" block :disabled="busy">{{ busy ? "Saving…" : "Update password" }}</VpButton>
    </form>

    <RouterLink to="/login" class="mt-4 text-sm text-primary inline-block">← Back to sign in</RouterLink>
    <VpAlert v-if="error" class="mt-4">{{ error }}</VpAlert>
  </section>
</template>
