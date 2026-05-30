<script setup lang="ts">
import { ref } from "vue";
import PageHeader from "@/components/layout/PageHeader.vue";
import VpButton from "@/components/ui/VpButton.vue";
import VpCard from "@/components/ui/VpCard.vue";
import type { Job } from "@/types/job";
import { useClockStore } from "@/stores/clock";
import { useJobsStore } from "@/stores/jobs";

const clock = useClockStore();
const jobsStore = useJobsStore();
const selectedJobId = ref<number | null>(null);

if (!jobsStore.jobs.length) {
  void jobsStore.fetchJobs();
}

const assignableJobs = () =>
  jobsStore.jobs.filter((j) => j.status === "scheduled" || j.status === "in_progress");

function selectJob(job: Job) {
  if (clock.isClockedIn) return;
  selectedJobId.value = job.id;
}

const busy = ref(false);
const error = ref<string | null>(null);

async function confirmClockIn() {
  const job = jobsStore.jobs.find((j) => j.id === selectedJobId.value);
  if (!job) return;
  busy.value = true;
  error.value = null;
  try {
    await clock.clockIn(job.id, job.title);
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Unable to clock in.";
  } finally {
    busy.value = false;
  }
}

async function confirmClockOut() {
  if (!confirm("Clock out now?")) return;
  busy.value = true;
  error.value = null;
  try {
    await clock.clockOut();
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Unable to clock out.";
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <PageHeader title="Clock" :subtitle="clock.isClockedIn ? undefined : 'Select a job to clock in'" />

  <template v-if="clock.isClockedIn && clock.active">
    <VpCard class="text-center mb-6">
      <p class="text-sm text-muted mb-1">Working on</p>
      <h2 class="text-lg font-bold mb-4">{{ clock.active.jobTitle }}</h2>
      <p class="text-4xl font-mono font-semibold text-primary tabular-nums">{{ clock.elapsedDisplay }}</p>
      <p v-if="clock.active.onBreak" class="text-xs text-amber-700 mt-2">On break</p>
    </VpCard>

    <div class="grid grid-cols-2 gap-3 mb-4">
      <VpButton v-if="!clock.active.onBreak" variant="secondary" :disabled="busy" @click="clock.startBreak">Start break</VpButton>
      <VpButton v-else variant="secondary" :disabled="busy" @click="clock.endBreak">End break</VpButton>
    </div>

    <VpButton variant="danger" block :disabled="busy" @click="confirmClockOut">Clock out</VpButton>
    <p v-if="error" class="text-xs text-error text-center mt-3">{{ error }}</p>
  </template>

  <template v-else>
    <VpCard class="mb-4">
      <p class="text-sm font-semibold text-muted mb-3">Active jobs</p>
      <button
        v-for="job in assignableJobs()"
        :key="job.id"
        type="button"
        class="w-full text-left border rounded-lg p-4 mb-2 last:mb-0"
        :class="selectedJobId === job.id ? 'border-primary ring-2 ring-primary/20' : 'border-border'"
        @click="selectJob(job)"
      >
        <span class="font-semibold block">{{ job.title }}</span>
        <span class="text-xs text-muted">{{ job.addressLine1 ?? "No address" }}</span>
      </button>
      <p v-if="!assignableJobs().length" class="text-sm text-muted">No active jobs to clock into.</p>
    </VpCard>

    <VpButton block :disabled="!selectedJobId || busy" @click="confirmClockIn">Clock in</VpButton>
    <p v-if="error" class="text-xs text-error text-center mt-3">{{ error }}</p>
  </template>
</template>
