<script setup lang="ts">
import { Icon } from "@iconify/vue";

withDefaults(
  defineProps<{
    variant?: "error" | "info";
  }>(),
  { variant: "error" }
);

const variantClass = {
  error: "border-error/25 bg-error/5 text-error",
  info: "border-border bg-surface text-muted",
} as const;

const variantIcon = {
  error: "mdi:alert-circle-outline",
  info: "mdi:information-outline",
} as const;
</script>

<template>
  <div
    role="alert"
    class="flex gap-2.5 rounded-md border px-3 py-2.5 text-sm"
    :class="variantClass[variant]"
  >
    <Icon :icon="variantIcon[variant]" class="text-lg shrink-0 mt-0.5" aria-hidden="true" />
    <p class="flex-1 leading-snug"><slot /></p>
  </div>
</template>
