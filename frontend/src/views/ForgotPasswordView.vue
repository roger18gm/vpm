<script setup lang="ts">
import { ref } from "vue";
import { RouterLink } from "vue-router";
import VpAlert from "@/components/ui/VpAlert.vue";
import VpButton from "@/components/ui/VpButton.vue";
import VpInput from "@/components/ui/VpInput.vue";
import { useAuthStore } from "@/stores/auth";

const auth = useAuthStore();
const email = ref("");
const busy = ref(false);
const submitted = ref(false);
const error = ref<string | null>(null);

async function onSubmit() {
  busy.value = true;
  error.value = null;
  try {
    await auth.forgotPassword(email.value.trim());
    submitted.value = true;
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Unable to send reset email.";
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <section class="w-full max-w-sm bg-surface border border-border rounded-lg p-6 shadow-sm">
    <p class="text-xs uppercase text-primary font-semibold mb-1">VisionPaint</p>
    <h1 class="text-2xl font-bold mb-2">Forgot password</h1>
    <p class="text-sm text-muted mb-6">Enter your email and we’ll send reset instructions if an account exists.</p>

    <form v-if="!submitted" class="space-y-3" @submit.prevent="onSubmit">
      <VpInput v-model="email" label="Email" type="email" required placeholder="you@company.com" />
      <VpButton type="submit" block :disabled="busy">{{ busy ? "Sending…" : "Send reset link" }}</VpButton>
    </form>

    <p v-else class="text-sm text-muted">
      If an account exists for that email, we sent reset instructions.
    </p>

    <RouterLink to="/login" class="mt-4 text-sm text-primary inline-block">← Back to sign in</RouterLink>
    <VpAlert v-if="error" class="mt-4">{{ error }}</VpAlert>
  </section>
</template>
