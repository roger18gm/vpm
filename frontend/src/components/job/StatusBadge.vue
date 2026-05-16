<script setup lang="ts">
import { computed } from "vue";
import { statusLabel } from "@/utils/job";

const props = defineProps<{ status: string }>();

const classes = computed(() => {
  switch (props.status) {
    case "in_progress":
      return "bg-amber-100 text-amber-900";
    case "completed":
      return "bg-green-100 text-green-800";
    case "cancelled":
      return "bg-red-100 text-primary";
    default:
      return "bg-gray-100 text-gray-700";
  }
});
</script>

<template>
  <span class="text-xs px-2 py-0.5 rounded-full font-medium shrink-0" :class="classes">
    {{ statusLabel(status) }}
  </span>
</template>
