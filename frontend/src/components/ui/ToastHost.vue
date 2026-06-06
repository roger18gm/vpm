<script setup lang="ts">
import { useToastStore } from "@/stores/toast";

const toast = useToastStore();
</script>

<template>
  <div class="fixed left-4 right-4 max-w-md mx-auto bottom-24 md:bottom-6 z-50 space-y-2 pointer-events-none">
    <div
      v-for="message in toast.messages"
      :key="message.id"
      class="pointer-events-auto rounded-lg px-4 py-3 text-sm font-medium shadow-lg border"
      :class="message.kind === 'error' ? 'bg-surface border-error text-error' : 'bg-text text-white border-text'"
      role="status"
    >
      {{ message.text }}
    </div>
  </div>
</template>
