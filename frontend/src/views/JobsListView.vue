<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { RouterLink } from "vue-router";
import { Icon } from "@iconify/vue";
import PageHeader from "@/components/layout/PageHeader.vue";
import JobCard from "@/components/job/JobCard.vue";
import EmptyState from "@/components/ui/EmptyState.vue";
import { useAuthStore } from "@/stores/auth";
import { useJobsStore } from "@/stores/jobs";

const auth = useAuthStore();
const jobsStore = useJobsStore();
const filter = ref("all");

onMounted(() => {
  void jobsStore.fetchJobs();
});

const filteredJobs = computed(() => {
  if (filter.value === "all") return jobsStore.jobs;
  return jobsStore.jobs.filter((j) => j.status === filter.value);
});

const filters = [
  { id: "all", label: "All" },
  { id: "scheduled", label: "Scheduled" },
  { id: "in_progress", label: "In progress" },
  { id: "completed", label: "Completed" },
];
</script>

<template>
  <PageHeader :title="auth.isManager ? 'Jobs' : 'My jobs'" :subtitle="auth.isManager ? undefined : 'Assigned to you'">
    <template v-if="auth.isManager" #actions>
      <RouterLink to="/jobs/new" class="inline-flex items-center gap-1 bg-primary text-white text-sm font-semibold px-4 py-2 rounded-md">
        <Icon icon="mdi:plus" />
        New
      </RouterLink>
    </template>
  </PageHeader>

  <div v-if="auth.isManager" class="flex gap-2 flex-wrap mb-4">
    <button
      v-for="f in filters"
      :key="f.id"
      type="button"
      class="text-xs px-3 py-1.5 rounded-full border"
      :class="filter === f.id ? 'bg-text text-white border-text' : 'bg-surface border-border'"
      @click="filter = f.id"
    >
      {{ f.label }}
    </button>
  </div>

  <p v-if="jobsStore.loading" class="text-sm text-muted">Loading jobs…</p>
  <p v-else-if="jobsStore.error" class="text-sm text-error">{{ jobsStore.error }}</p>

  <ul v-else-if="filteredJobs.length" class="space-y-3">
    <li v-for="job in filteredJobs" :key="job.id">
      <JobCard :job="job" />
    </li>
  </ul>

  <EmptyState
    v-else
    :title="auth.isManager ? 'No jobs yet' : 'No jobs assigned yet'"
    :message="auth.isManager ? 'Create your first job to get started.' : 'Contact your manager if you expect work here.'"
  >
    <template v-if="auth.isManager" #action>
      <RouterLink to="/jobs/new" class="text-primary font-semibold text-sm">Create job</RouterLink>
    </template>
  </EmptyState>
</template>
