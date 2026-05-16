<script setup lang="ts">
withDefaults(
  defineProps<{
    variant?: "primary" | "secondary" | "ghost" | "danger";
    type?: "button" | "submit";
    disabled?: boolean;
    block?: boolean;
  }>(),
  { variant: "primary", type: "button", disabled: false, block: false }
);
</script>

<template>
  <button
    :type="type"
    :disabled="disabled"
    class="inline-flex items-center justify-center rounded-lg text-sm font-semibold min-h-[44px] px-4 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
    :class="[
      block ? 'w-full' : '',
      variant === 'primary' && 'bg-primary text-white hover:bg-primary-hover',
      variant === 'secondary' && 'bg-surface border border-border text-text hover:bg-page',
      variant === 'ghost' && 'bg-page border border-border text-text',
      variant === 'danger' && 'border-2 border-primary text-primary bg-surface hover:bg-red-50',
    ]"
  >
    <slot />
  </button>
</template>
