<script setup lang="ts">
import { onMounted, ref } from "vue";
import { RouterLink, useRoute, useRouter } from "vue-router";
import VpAlert from "@/components/ui/VpAlert.vue";
import VpButton from "@/components/ui/VpButton.vue";
import VpInput from "@/components/ui/VpInput.vue";
import { useAuthStore } from "@/stores/auth";

const auth = useAuthStore();
const router = useRouter();
const route = useRoute();

const mode = ref(auth.canBootstrap ? "bootstrap" : "login");
const name = ref("VisionPaint Owner");
const email = ref("");
const password = ref("");
const busy = ref(false);
const message = ref<string | null>(null);
const error = ref<string | null>(null);

onMounted(() => {
  if (route.query.reset === "1") {
    message.value = "Password updated. Sign in with your new password.";
  }
});

async function onSubmit() {
  busy.value = true;
  message.value = null;
  error.value = null;
  try {
    if (mode.value === "bootstrap") {
      await auth.bootstrap(name.value, email.value, password.value);
    } else {
      await auth.login(email.value, password.value);
    }
    const redirectQuery = typeof route.query.redirect === "string" ? route.query.redirect : undefined;
    const destination =
      redirectQuery && redirectQuery !== "/" && redirectQuery.startsWith("/")
        ? redirectQuery
        : auth.defaultHome;
    await router.replace(destination);
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Sign in failed.";
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <section class="w-full max-w-sm bg-surface border border-border rounded-lg p-6 shadow-sm">
    <p class="text-xs uppercase text-primary font-semibold mb-1">VisionPaint</p>
    <h1 class="text-2xl font-bold mb-6">{{ mode === "bootstrap" ? "Create the first account" : "Sign in" }}</h1>

    <form class="space-y-3" @submit.prevent="onSubmit">
      <VpInput v-if="mode === 'bootstrap'" v-model="name" label="Your name" required />
      <VpInput v-model="email" label="Email" type="email" required placeholder="you@company.com" />
      <VpInput v-model="password" label="Password" type="password" required show-password-toggle />
      <VpButton type="submit" block :disabled="busy">{{ busy ? "Working…" : mode === "bootstrap" ? "Create account" : "Sign in" }}</VpButton>
    </form>

    <RouterLink
      v-if="mode === 'login'"
      to="/forgot-password"
      class="mt-3 text-sm text-primary inline-block"
    >
      Forgot password?
    </RouterLink>

    <button
      v-if="auth.canBootstrap"
      type="button"
      class="mt-4 text-xs text-primary w-full"
      @click="mode = mode === 'bootstrap' ? 'login' : 'bootstrap'"
    >
      {{ mode === "bootstrap" ? "Use sign in" : "First-time setup" }}
    </button>

    <p v-if="message" class="mt-3 text-sm text-muted">{{ message }}</p>
    <VpAlert v-if="error" class="mt-4">{{ error }}</VpAlert>
  </section>
</template>
