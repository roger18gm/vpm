<script setup lang="ts">
import { RouterLink } from "vue-router";
import { useAuthStore } from "@/stores/auth";

const auth = useAuthStore();
</script>

<template>
  <div class="min-h-screen bg-page flex items-center justify-center p-4">
    <section class="max-w-md text-center">
      <h1 class="text-2xl font-bold mb-2">Access denied</h1>
      <p class="text-muted text-sm mb-6">You don’t have permission to view that page.</p>
      <RouterLink :to="auth.defaultHome" class="text-primary font-semibold">Back to home</RouterLink>
    </section>
  </div>
</template>
