<script setup lang="ts">
defineProps<{ title: string; subtitle?: string }>();
</script>

<template>
  <header class="flex justify-between items-start gap-4 mb-4">
    <div>
      <p class="text-xs uppercase text-primary font-semibold">VisionPaint</p>
      <h1 class="text-2xl md:text-3xl font-bold">{{ title }}</h1>
      <p v-if="subtitle" class="text-sm text-muted mt-1">{{ subtitle }}</p>
    </div>
    <slot name="actions" />
  </header>
</template>
