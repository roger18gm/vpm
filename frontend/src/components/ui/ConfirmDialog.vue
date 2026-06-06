<script setup lang="ts">
import VpButton from "@/components/ui/VpButton.vue";

defineProps<{
  open: boolean;
  title: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  busy?: boolean;
}>();

const emit = defineEmits<{
  confirm: [];
  cancel: [];
}>();
</script>

<template>
  <div
    v-if="open"
    class="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/40"
    role="dialog"
    aria-modal="true"
    :aria-label="title"
    @click.self="emit('cancel')"
  >
    <div class="w-full max-w-sm bg-surface border border-border rounded-lg p-5 shadow-xl">
      <h2 class="text-lg font-bold mb-2">{{ title }}</h2>
      <p class="text-sm text-muted mb-5">{{ message }}</p>
      <div class="grid grid-cols-2 gap-3">
        <VpButton variant="secondary" :disabled="busy" @click="emit('cancel')">
          {{ cancelLabel ?? "Cancel" }}
        </VpButton>
        <VpButton variant="danger" :disabled="busy" @click="emit('confirm')">
          {{ confirmLabel ?? "Confirm" }}
        </VpButton>
      </div>
    </div>
  </div>
</template>
