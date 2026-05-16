<script setup lang="ts">
defineProps<{
  label: string;
  modelValue: string;
  type?: string;
  placeholder?: string;
  required?: boolean;
}>();

defineEmits<{ "update:modelValue": [value: string] }>();
</script>

<template>
  <label class="block">
    <span class="text-xs text-muted mb-1 block">{{ label }}</span>
    <input
      :type="type ?? 'text'"
      :value="modelValue"
      :placeholder="placeholder"
      :required="required"
      class="w-full border border-border rounded-md px-3 py-2.5 text-sm min-h-[44px] bg-surface text-text"
      @input="$emit('update:modelValue', ($event.target as HTMLInputElement).value)"
    />
  </label>
</template>
