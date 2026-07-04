<script setup lang="ts">
import { RouterLink } from "vue-router";
import { Icon } from "@iconify/vue";
import StatusBadge from "./StatusBadge.vue";
import PriorityChip from "./PriorityChip.vue";
import type { Job } from "@/types/job";
import { formatJobAddress, isJobOverdue } from "@/utils/job";

defineProps<{ job: Job }>();

function photoCountLabel(count: number): string {
  return count === 1 ? "1 photo" : `${count} photos`;
}
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
    <div class="flex flex-wrap items-center gap-2 mt-2">
      <p v-if="job.dueAt" class="text-xs" :class="isJobOverdue(job) ? 'text-primary font-medium' : 'text-muted'">
        {{ isJobOverdue(job) ? "Overdue · " : "Due " }}{{ new Date(job.dueAt).toLocaleDateString() }}
      </p>
      <span
        v-if="job.photoCount && job.photoCount > 0"
        class="inline-flex items-center gap-1 text-xs text-muted bg-page border border-border rounded-full px-2 py-0.5"
      >
        <Icon icon="mdi:camera-outline" class="text-sm" aria-hidden="true" />
        {{ photoCountLabel(job.photoCount) }}
      </span>
    </div>
    <PriorityChip :priority="job.priority" class="mt-2 inline-block" />
  </RouterLink>
</template>
