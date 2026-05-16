<script setup lang="ts">
defineProps<{ title: string; message?: string }>();
</script>

<template>
  <div class="text-center py-10 px-4">
    <p class="font-semibold text-text">{{ title }}</p>
    <p v-if="message" class="text-sm text-muted mt-1">{{ message }}</p>
    <div v-if="$slots.action" class="mt-4">
      <slot name="action" />
    </div>
  </div>
</template>
