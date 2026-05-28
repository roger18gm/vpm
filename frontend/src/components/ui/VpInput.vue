<script setup lang="ts">
import { computed, ref } from "vue";
import { Icon } from "@iconify/vue";

const props = defineProps<{
  label: string;
  modelValue: string;
  type?: string;
  placeholder?: string;
  required?: boolean;
  showPasswordToggle?: boolean;
}>();

defineEmits<{ "update:modelValue": [value: string] }>();

const passwordVisible = ref(false);

const inputType = computed(() => {
  if (props.showPasswordToggle && props.type === "password") {
    return passwordVisible.value ? "text" : "password";
  }
  return props.type ?? "text";
});

const isRevealablePassword = computed(() => props.showPasswordToggle && props.type === "password");
</script>

<template>
  <label class="block">
    <span class="text-xs text-muted mb-1 block">{{ label }}</span>
    <div class="relative">
      <input
        :type="inputType"
        :value="modelValue"
        :placeholder="placeholder"
        :required="required"
        class="w-full border border-border rounded-md px-3 py-2.5 text-sm min-h-[44px] bg-surface text-text select-text"
        :class="{ 'pr-11': isRevealablePassword }"
        @input="$emit('update:modelValue', ($event.target as HTMLInputElement).value)"
      />
      <button
        v-if="isRevealablePassword"
        type="button"
        class="absolute inset-y-0 right-0 flex items-center px-3 text-muted hover:text-text"
        :aria-label="passwordVisible ? 'Hide password' : 'Show password'"
        :aria-pressed="passwordVisible"
        @click="passwordVisible = !passwordVisible"
      >
        <Icon
          :icon="passwordVisible ? 'mdi:eye-off-outline' : 'mdi:eye-outline'"
          class="text-xl"
          aria-hidden="true"
        />
      </button>
    </div>
  </label>
</template>
