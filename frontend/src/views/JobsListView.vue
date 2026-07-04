<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { RouterLink } from "vue-router";
import { Icon } from "@iconify/vue";
import PageHeader from "@/components/layout/PageHeader.vue";
import JobCard from "@/components/job/JobCard.vue";
import EmptyState from "@/components/ui/EmptyState.vue";
import VpInput from "@/components/ui/VpInput.vue";
import { useAuthStore } from "@/stores/auth";
import { useJobsStore } from "@/stores/jobs";
import { matchesJobSearch } from "@/utils/job";

const auth = useAuthStore();
const jobsStore = useJobsStore();
const filter = ref("all");
const searchQuery = ref("");

onMounted(() => {
  void jobsStore.fetchJobs();
});

const filteredJobs = computed(() => {
  let rows = jobsStore.jobs;
  if (filter.value !== "all") {
    rows = rows.filter((job) => job.status === filter.value);
  }
  if (searchQuery.value.trim()) {
    rows = rows.filter((job) => matchesJobSearch(job, searchQuery.value));
  }
  return rows;
});

const filters = [
  { id: "all", label: "All" },
  { id: "scheduled", label: "Scheduled" },
  { id: "in_progress", label: "In progress" },
  { id: "completed", label: "Completed" },
];

const emptyMessage = computed(() => {
  if (searchQuery.value.trim()) {
    return "Try a different title or address.";
  }
  return auth.isManager
    ? "Create your first job to get started."
    : "Contact your manager if you expect work here.";
});

const emptyTitle = computed(() => {
  if (searchQuery.value.trim()) {
    return "No matching jobs";
  }
  return auth.isManager ? "No jobs yet" : "No jobs assigned yet";
});
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

  <VpInput
    v-model="searchQuery"
    label="Search"
    placeholder="Title or address"
    class="mb-4"
  />

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
    :title="emptyTitle"
    :message="emptyMessage"
  >
    <template v-if="auth.isManager && !searchQuery.trim()" #action>
      <RouterLink to="/jobs/new" class="text-primary font-semibold text-sm">Create job</RouterLink>
    </template>
  </EmptyState>
</template>
