<script setup lang="ts">
import { RouterLink } from "vue-router";
import type { JobAssignment } from "@/types/job";

defineProps<{
  assignments: JobAssignment[];
  jobId: number;
  showAssignLink?: boolean;
}>();
</script>

<template>
  <ul v-if="assignments.length" class="space-y-1">
    <li v-for="person in assignments" :key="person.personId" class="text-sm">
      {{ person.name }}
    </li>
  </ul>
  <p v-else class="text-sm text-muted">No crew assigned yet.</p>
  <RouterLink
    v-if="showAssignLink"
    :to="{ name: 'job-crew', params: { id: jobId } }"
    class="text-sm text-primary inline-block mt-2"
  >
    Assign crew
  </RouterLink>
</template>
