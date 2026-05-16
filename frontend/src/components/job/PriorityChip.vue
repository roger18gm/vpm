<script setup lang="ts">
import { computed } from "vue";

const props = defineProps<{ priority: string }>();

const show = computed(() => props.priority === "high" || props.priority === "urgent");

const classes = computed(() =>
  props.priority === "urgent" ? "bg-red-100 text-primary" : "bg-orange-100 text-orange-800"
);
</script>

<template>
  <span v-if="show" class="text-xs px-2 py-0.5 rounded-full font-medium capitalize" :class="classes">
    {{ priority }}
  </span>
</template>
