<script setup lang="ts">
defineProps<{
  label: string;
  modelValue: string;
}>();

defineEmits<{ "update:modelValue": [value: string] }>();
</script>

<template>
  <label class="block">
    <span class="text-xs text-muted mb-1 block">{{ label }}</span>
    <select
      :value="modelValue"
      class="w-full border border-border rounded-md px-3 py-2.5 text-sm min-h-[44px] bg-surface text-text"
      @change="$emit('update:modelValue', ($event.target as HTMLSelectElement).value)"
    >
      <slot />
    </select>
  </label>
</template>
