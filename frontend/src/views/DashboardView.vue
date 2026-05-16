<script setup lang="ts">
import { onMounted } from "vue";
import { RouterLink } from "vue-router";
import { Icon } from "@iconify/vue";
import PageHeader from "@/components/layout/PageHeader.vue";
import VpCard from "@/components/ui/VpCard.vue";
import StatusBadge from "@/components/job/StatusBadge.vue";
import { useJobsStore } from "@/stores/jobs";

const jobsStore = useJobsStore();

onMounted(() => {
  void jobsStore.fetchJobs();
});
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

  <div class="grid grid-cols-2 md:grid-cols-4 gap-3 mb-6">
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
      <p class="text-xs text-muted">Total jobs</p>
      <p class="text-2xl font-bold">{{ jobsStore.jobs.length }}</p>
    </VpCard>
    </div>
  </div>

  <VpCard>
    <template #title>Overdue jobs</template>
    <p v-if="jobsStore.loading" class="text-sm text-muted">Loading…</p>
    <ul v-else-if="jobsStore.overdueJobs.length" class="divide-y divide-border">
      <li v-for="job in jobsStore.overdueJobs" :key="job.id">
        <RouterLink :to="{ name: 'job-detail', params: { id: job.id } }" class="flex justify-between items-center py-3 gap-2">
          <div>
            <p class="font-semibold text-sm">{{ job.title }}</p>
            <p class="text-xs text-muted">Due {{ new Date(job.dueAt!).toLocaleDateString() }}</p>
          </div>
          <StatusBadge :status="job.status" />
        </RouterLink>
      </li>
    </ul>
    <p v-else class="text-sm text-muted">No overdue jobs.</p>
  </VpCard>
</template>
