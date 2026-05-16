<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { RouterLink, useRouter } from "vue-router";
import StatusBadge from "@/components/job/StatusBadge.vue";
import PriorityChip from "@/components/job/PriorityChip.vue";
import VpCard from "@/components/ui/VpCard.vue";
import VpButton from "@/components/ui/VpButton.vue";
import VpSelect from "@/components/ui/VpSelect.vue";
import type { Job } from "@/types/job";
import { formatJobAddress } from "@/utils/job";
import { useAuthStore } from "@/stores/auth";
import { useJobsStore } from "@/stores/jobs";
import { useClockStore } from "@/stores/clock";

const props = defineProps<{ id: number }>();
const auth = useAuthStore();
const jobsStore = useJobsStore();
const clock = useClockStore();
const router = useRouter();

const job = ref<Job | null>(null);
const error = ref<string | null>(null);
const statusDraft = ref("");

const address = computed(() => (job.value ? formatJobAddress(job.value) : null));
const isActiveClockJob = computed(() => clock.active?.jobId === props.id);

onMounted(async () => {
  try {
    job.value = jobsStore.getJobFromCache(props.id) ?? (await jobsStore.fetchJob(props.id));
    statusDraft.value = job.value.status;
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Job not found.";
  }
});

function clockInHere() {
  if (!job.value || clock.isClockedIn) return;
  clock.clockIn(job.value.id, job.value.title);
  void router.push({ name: "clock" });
}

async function saveStatus() {
  if (!job.value || !auth.isManager) return;
  try {
    job.value = await jobsStore.updateJob(props.id, {
      title: job.value.title,
      status: statusDraft.value,
    });
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Unable to update status.";
  }
}

async function archiveJob() {
  if (!confirm("Archive this job?")) return;
  await jobsStore.archiveJob(props.id);
  await router.push({ name: "jobs" });
}
</script>

<template>
  <RouterLink to="/jobs" class="text-sm text-primary mb-2 inline-block">← Jobs</RouterLink>

  <p v-if="error" class="text-error text-sm">{{ error }}</p>
  <template v-else-if="job">
    <div class="flex flex-wrap items-center gap-2 mb-1">
      <h1 class="text-xl font-bold flex-1">{{ job.title }}</h1>
      <StatusBadge :status="job.status" />
      <PriorityChip :priority="job.priority" />
    </div>
    <p v-if="address" class="text-sm text-muted mb-4">{{ address }}</p>

    <div class="grid grid-cols-2 gap-2 mb-4">
      <VpButton v-if="!clock.isClockedIn" block @click="clockInHere">Clock in</VpButton>
      <VpButton v-else-if="isActiveClockJob" variant="secondary" block @click="router.push({ name: 'clock' })">On clock</VpButton>
      <VpButton v-else variant="ghost" block disabled>Clocked elsewhere</VpButton>
      <RouterLink
        :to="{ name: 'job-photos', params: { id } }"
        class="bg-surface border border-border text-center font-semibold rounded-lg py-3 text-sm min-h-[44px] flex items-center justify-center"
      >
        Photos
      </RouterLink>
    </div>

    <VpCard v-if="auth.isManager" class="mb-3">
      <template #title>Manager</template>
      <div class="flex flex-wrap gap-2 items-end">
        <div class="flex-1 min-w-[140px]">
        <VpSelect v-model="statusDraft" label="Status">
          <option value="scheduled">Scheduled</option>
          <option value="in_progress">In progress</option>
          <option value="completed">Completed</option>
          <option value="cancelled">Cancelled</option>
        </VpSelect>
        </div>
        <VpButton @click="saveStatus">Update</VpButton>
        <RouterLink :to="{ name: 'job-edit', params: { id } }" class="text-sm text-primary">Edit</RouterLink>
        <RouterLink :to="{ name: 'job-crew', params: { id } }" class="text-sm text-primary">Assign crew</RouterLink>
        <VpButton variant="ghost" @click="archiveJob">Archive</VpButton>
      </div>
    </VpCard>

    <VpCard class="mb-3">
      <template #title>Schedule</template>
      <dl class="text-sm space-y-1">
        <div v-if="job.scheduledStartAt" class="flex justify-between"><dt class="text-muted">Start</dt><dd>{{ new Date(job.scheduledStartAt).toLocaleDateString() }}</dd></div>
        <div v-if="job.dueAt" class="flex justify-between"><dt class="text-muted">Due</dt><dd>{{ new Date(job.dueAt).toLocaleDateString() }}</dd></div>
      </dl>
    </VpCard>

    <VpCard v-if="job.description" class="mb-3">
      <template #title>Description</template>
      <p class="text-sm whitespace-pre-wrap">{{ job.description }}</p>
    </VpCard>

    <VpCard>
      <template #title>Time on this job</template>
      <p class="text-2xl font-bold">— <span class="text-base font-normal text-muted">hrs (API pending)</span></p>
    </VpCard>
  </template>
  <p v-else class="text-muted text-sm">Loading…</p>
</template>
