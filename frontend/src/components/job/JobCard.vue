<script setup lang="ts">
import { RouterLink } from "vue-router";
import StatusBadge from "./StatusBadge.vue";
import PriorityChip from "./PriorityChip.vue";
import type { Job } from "@/types/job";
import { formatJobAddress, isJobOverdue } from "@/utils/job";

defineProps<{ job: Job }>();
</script>

<template>
  <RouterLink
    :to="{ name: 'job-detail', params: { id: job.id } }"
    class="block bg-surface border border-border rounded-lg p-4 hover:border-primary transition-colors"
  >
    <div class="flex justify-between items-start gap-2">
      <h2 class="font-semibold text-[15px]">{{ job.title }}</h2>
      <StatusBadge :status="job.status" />
    </div>
    <p v-if="formatJobAddress(job)" class="text-sm text-muted mt-1">{{ formatJobAddress(job) }}</p>
    <p v-if="job.dueAt" class="text-xs mt-2" :class="isJobOverdue(job) ? 'text-primary font-medium' : 'text-muted'">
      {{ isJobOverdue(job) ? "Overdue · " : "Due " }}{{ new Date(job.dueAt).toLocaleDateString() }}
    </p>
    <PriorityChip :priority="job.priority" class="mt-2 inline-block" />
  </RouterLink>
</template>
