<script setup lang="ts">
import { onMounted, ref } from "vue";
import { RouterLink } from "vue-router";
import VpCard from "@/components/ui/VpCard.vue";
import type { Job } from "@/types/job";
import { useJobsStore } from "@/stores/jobs";

const props = defineProps<{ id: number }>();
const jobsStore = useJobsStore();
const job = ref<Job | null>(null);

onMounted(async () => {
  job.value = jobsStore.getJobFromCache(props.id) ?? (await jobsStore.fetchJob(props.id));
});
</script>

<template>
  <RouterLink :to="{ name: 'job-detail', params: { id } }" class="text-sm text-primary mb-3 inline-block">← Job</RouterLink>
  <h1 class="text-xl font-bold mb-2">Assign crew</h1>
  <p class="text-sm text-muted mb-4">{{ job?.title }}</p>
  <VpCard>
    <p class="text-sm text-muted">Crew assignment API coming in a later sprint. For now, note assignments offline and track in job notes.</p>
  </VpCard>
</template>
