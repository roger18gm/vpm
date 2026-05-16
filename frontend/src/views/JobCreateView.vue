<script setup lang="ts">
import { ref } from "vue";
import { useRouter } from "vue-router";
import { RouterLink } from "vue-router";
import JobForm from "@/components/job/JobForm.vue";
import type { JobInput } from "@/types/job";
import { useJobsStore } from "@/stores/jobs";

const router = useRouter();
const jobsStore = useJobsStore();
const busy = ref(false);
const error = ref<string | null>(null);

async function onSubmit(payload: JobInput) {
  busy.value = true;
  error.value = null;
  try {
    const job = await jobsStore.createJob({ ...payload, status: payload.status ?? "scheduled" });
    await router.push({ name: "job-detail", params: { id: job.id } });
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Unable to create job.";
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <RouterLink to="/jobs" class="text-sm text-primary mb-3 inline-block">← Jobs</RouterLink>
  <p class="text-xs uppercase text-primary font-semibold">VisionPaint</p>
  <h1 class="text-2xl font-bold mb-6">New job</h1>
  <p v-if="error" class="text-sm text-error mb-3">{{ error }}</p>
  <JobForm :busy="busy" @submit="onSubmit" @cancel="router.push('/jobs')" />
</template>
