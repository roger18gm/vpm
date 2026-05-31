<script setup lang="ts">
import { onMounted } from "vue";
import { RouterView } from "vue-router";
import VpLoadingScreen from "@/components/loading/VpLoadingScreen.vue";
import { useAuthStore } from "@/stores/auth";

const auth = useAuthStore();

onMounted(() => {
  if (!auth.initialized) {
    void auth.initialize();
  }
});
</script>

<template>
  <VpLoadingScreen v-if="!auth.initialized" />
  <RouterView v-else />
</template>
