<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRouter } from "vue-router";
import { RouterLink } from "vue-router";
import JobForm from "@/components/job/JobForm.vue";
import type { Job, JobInput } from "@/types/job";
import { useJobsStore } from "@/stores/jobs";

const props = defineProps<{ id: number }>();
const router = useRouter();
const jobsStore = useJobsStore();
const job = ref<Job | null>(null);
const busy = ref(false);
const error = ref<string | null>(null);

onMounted(async () => {
  try {
    job.value = jobsStore.getJobFromCache(props.id) ?? (await jobsStore.fetchJob(props.id));
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Job not found.";
  }
});

async function onSubmit(payload: JobInput) {
  busy.value = true;
  error.value = null;
  try {
    await jobsStore.updateJob(props.id, payload);
    await router.push({ name: "job-detail", params: { id: props.id } });
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Unable to update job.";
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <RouterLink :to="{ name: 'job-detail', params: { id } }" class="text-sm text-primary mb-3 inline-block">← Job</RouterLink>
  <h1 class="text-2xl font-bold mb-6">Edit job</h1>
  <p v-if="error && !job" class="text-sm text-error">{{ error }}</p>
  <JobForm v-else-if="job" :initial="job" :busy="busy" @submit="onSubmit" @cancel="router.back()" />
  <p v-else class="text-sm text-muted">Loading…</p>
</template>
