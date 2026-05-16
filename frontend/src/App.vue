<script setup lang="ts">
import { onMounted } from "vue";
import { RouterView } from "vue-router";
import { useAuthStore } from "@/stores/auth";

const auth = useAuthStore();

onMounted(() => {
  if (!auth.initialized) {
    void auth.initialize();
  }
});
</script>

<template>
  <div v-if="!auth.initialized" class="min-h-screen bg-page flex items-center justify-center text-muted text-sm">
    Loading…
  </div>
  <RouterView v-else />
</template>
