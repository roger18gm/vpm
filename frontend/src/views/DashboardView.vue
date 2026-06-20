<script setup lang="ts">
import { computed, onMounted } from "vue";
import { RouterLink } from "vue-router";
import { Icon } from "@iconify/vue";
import PageHeader from "@/components/layout/PageHeader.vue";
import VpCard from "@/components/ui/VpCard.vue";
import StatusBadge from "@/components/job/StatusBadge.vue";
import { useDashboardStore } from "@/stores/dashboard";
import { useJobsStore } from "@/stores/jobs";
import { formatMinutes } from "@/utils/job";

const jobsStore = useJobsStore();
const dashboardStore = useDashboardStore();

onMounted(() => {
  void Promise.all([jobsStore.fetchJobs(), dashboardStore.fetchSummary()]);
});

const hoursThisWeek = computed(() => {
  const minutes = dashboardStore.summary?.hoursThisWeekMinutes ?? 0;
  return formatMinutes(minutes);
});

const completedThisWeek = computed(() => dashboardStore.summary?.completedThisWeekCount ?? 0);

const clockedInWorkers = computed(() => dashboardStore.summary?.clockedInWorkers ?? []);

function formatDueDate(iso: string): string {
  return new Date(iso).toLocaleDateString();
}

function formatClockIn(iso: string): string {
  return new Date(iso).toLocaleTimeString([], { hour: "numeric", minute: "2-digit" });
}
</script>

<template>
  <PageHeader title="Dashboard">
    <template #actions>
      <RouterLink to="/jobs/new" class="inline-flex items-center gap-1 bg-primary text-white text-sm font-semibold px-4 py-2 rounded-md min-h-[44px]">
        <Icon icon="mdi:plus" />
        New job
      </RouterLink>
    </template>
  </PageHeader>

  <div class="grid grid-cols-2 md:grid-cols-4 gap-3 mb-2">
    <VpCard>
      <p class="text-xs text-muted">Active</p>
      <p class="text-2xl font-bold">{{ jobsStore.activeCount }}</p>
    </VpCard>
    <VpCard>
      <p class="text-xs text-muted">In progress</p>
      <p class="text-2xl font-bold">{{ jobsStore.inProgressCount }}</p>
    </VpCard>
    <VpCard>
      <p class="text-xs text-muted">Overdue</p>
      <p class="text-2xl font-bold text-primary">{{ jobsStore.overdueJobs.length }}</p>
    </VpCard>
    <div class="col-span-2 md:col-span-1">
      <VpCard>
        <p class="text-xs text-muted">Hours this week</p>
        <p v-if="dashboardStore.loading && !dashboardStore.summary" class="text-2xl font-bold">—</p>
        <p v-else class="text-2xl font-bold">{{ hoursThisWeek }}</p>
      </VpCard>
    </div>
  </div>

  <p class="text-sm text-muted mb-6">
    Completed this week:
    <span class="font-semibold text-text">{{ completedThisWeek }}</span>
  </p>

  <p v-if="dashboardStore.error" class="text-sm text-error mb-4">{{ dashboardStore.error }}</p>

  <div class="space-y-6">
    <VpCard>
      <template #title>Overdue jobs</template>
      <p v-if="jobsStore.loading" class="text-sm text-muted">Loading…</p>
      <ul v-else-if="jobsStore.overdueJobs.length" class="divide-y divide-border">
        <li v-for="job in jobsStore.overdueJobs" :key="job.id">
          <RouterLink :to="{ name: 'job-detail', params: { id: job.id } }" class="flex justify-between items-center py-3 gap-2">
            <div>
              <p class="font-semibold text-sm">{{ job.title }}</p>
              <p class="text-xs text-muted">Due {{ formatDueDate(job.dueAt!) }}</p>
            </div>
            <StatusBadge :status="job.status" />
          </RouterLink>
        </li>
      </ul>
      <p v-else class="text-sm text-muted">No overdue jobs.</p>
    </VpCard>

    <VpCard>
      <template #title>Due soon</template>
      <p class="text-xs text-muted mb-3">Next 7 days</p>
      <p v-if="jobsStore.loading" class="text-sm text-muted">Loading…</p>
      <ul v-else-if="jobsStore.dueSoonJobs.length" class="divide-y divide-border">
        <li v-for="job in jobsStore.dueSoonJobs" :key="job.id">
          <RouterLink :to="{ name: 'job-detail', params: { id: job.id } }" class="flex justify-between items-center py-3 gap-2">
            <div>
              <p class="font-semibold text-sm">{{ job.title }}</p>
              <p class="text-xs text-muted">Due {{ formatDueDate(job.dueAt!) }}</p>
            </div>
            <StatusBadge :status="job.status" />
          </RouterLink>
        </li>
      </ul>
      <p v-else class="text-sm text-muted">No jobs due in the next 7 days.</p>
    </VpCard>

    <VpCard>
      <template #title>Currently on site</template>
      <p v-if="dashboardStore.loading && !dashboardStore.summary" class="text-sm text-muted">Loading…</p>
      <ul v-else-if="clockedInWorkers.length" class="divide-y divide-border">
        <li v-for="worker in clockedInWorkers" :key="`${worker.personId}-${worker.jobId}`">
          <RouterLink :to="{ name: 'job-detail', params: { id: worker.jobId } }" class="flex justify-between items-center py-3 gap-2">
            <div>
              <p class="font-semibold text-sm">{{ worker.name }}</p>
              <p class="text-xs text-muted">
                {{ worker.jobTitle }} · since {{ formatClockIn(worker.clockInAt) }}
                <span v-if="worker.onBreak"> · on break</span>
              </p>
            </div>
            <Icon icon="mdi:clock-outline" class="text-muted shrink-0" />
          </RouterLink>
        </li>
      </ul>
      <p v-else class="text-sm text-muted">No one is clocked in right now.</p>
    </VpCard>
  </div>
</template>
