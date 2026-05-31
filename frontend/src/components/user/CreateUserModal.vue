<script setup lang="ts">
import { computed, ref, watch } from "vue";
import VpButton from "@/components/ui/VpButton.vue";
import VpCard from "@/components/ui/VpCard.vue";
import VpInput from "@/components/ui/VpInput.vue";
import VpSelect from "@/components/ui/VpSelect.vue";
import { useAuthStore } from "@/stores/auth";
import { useUsersStore } from "@/stores/users";

const props = defineProps<{ open: boolean }>();
const emit = defineEmits<{ close: []; created: [] }>();

const auth = useAuthStore();
const usersStore = useUsersStore();

const name = ref("");
const email = ref("");
const password = ref("");
const companyRole = ref("crew");
const busy = ref(false);
const error = ref<string | null>(null);

const roleOptions = computed(() => {
  const roles = ["crew", "manager", "admin"];
  if (auth.user?.companyRole === "owner") {
    roles.push("owner");
  }
  return roles;
});

watch(
  () => props.open,
  (isOpen) => {
    if (!isOpen) {
      return;
    }
    name.value = "";
    email.value = "";
    password.value = "";
    companyRole.value = "crew";
    error.value = null;
  },
);

async function submit() {
  error.value = null;
  if (!name.value.trim() || !email.value.trim() || !password.value) {
    error.value = "Name, email, and password are required.";
    return;
  }
  if (password.value.length < 8) {
    error.value = "Password must be at least 8 characters.";
    return;
  }

  busy.value = true;
  try {
    await usersStore.createUser({
      name: name.value.trim(),
      email: email.value.trim(),
      password: password.value,
      companyRole: companyRole.value,
    });
    emit("created");
    emit("close");
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Unable to create user.";
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
          <h2 class="text-lg font-bold">New user</h2>
          <p class="text-sm text-muted">Create a login for your company.</p>
        </div>
        <button type="button" class="text-sm text-muted hover:text-text" @click="emit('close')">Close</button>
      </div>

      <form class="space-y-3" @submit.prevent="submit">
        <VpInput v-model="name" label="Name" required />
        <VpInput v-model="email" label="Email" type="email" required />
        <VpInput v-model="password" label="Password" type="password" required show-password-toggle />
        <VpSelect v-model="companyRole" label="Role">
          <option v-for="role in roleOptions" :key="role" :value="role">
            {{ role }}
          </option>
        </VpSelect>

        <p v-if="error" class="text-sm text-error">{{ error }}</p>

        <div class="flex gap-2 pt-1">
          <VpButton type="button" variant="secondary" block @click="emit('close')">Cancel</VpButton>
          <VpButton type="submit" block :disabled="busy">{{ busy ? "Creating…" : "Create user" }}</VpButton>
        </div>
      </form>
    </VpCard>
  </div>
</template>
