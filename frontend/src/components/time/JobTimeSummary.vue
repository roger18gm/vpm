<script setup lang="ts">
import type { JobTimeSummary } from "@/types/job";
import { formatMinutes } from "@/utils/job";

defineProps<{ summary: JobTimeSummary | null }>();
</script>

<template>
  <template v-if="summary">
    <p class="text-2xl font-bold">
      {{ formatMinutes(summary.totalMinutes) }}
      <span class="text-base font-normal text-muted">hrs total</span>
    </p>
    <ul v-if="summary.byPerson.length" class="mt-3 space-y-2 border-t border-border pt-3">
      <li v-for="row in summary.byPerson" :key="row.personId" class="flex justify-between text-sm">
        <span>{{ row.name }}</span>
        <span class="text-muted">
          {{ formatMinutes(row.minutes) }} hrs
          <span v-if="row.inProgress" class="text-primary"> · in progress</span>
        </span>
      </li>
    </ul>
  </template>
  <p v-else class="text-sm text-muted">No time logged yet.</p>
</template>
