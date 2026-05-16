import { defineStore } from "pinia";
import { computed, ref } from "vue";
import { request } from "@/lib/api";
import type { Job, JobInput } from "@/types/job";
import { isJobOverdue } from "@/utils/job";

export const useJobsStore = defineStore("jobs", () => {
  const jobs = ref<Job[]>([]);
  const loading = ref(false);
  const error = ref<string | null>(null);

  const overdueJobs = computed(() => jobs.value.filter((job) => isJobOverdue(job)));

  const activeCount = computed(() => jobs.value.filter((j) => j.status !== "completed" && j.status !== "cancelled").length);
  const inProgressCount = computed(() => jobs.value.filter((j) => j.status === "in_progress").length);

  async function fetchJobs() {
    loading.value = true;
    error.value = null;
    try {
      jobs.value = await request<Job[]>("/jobs");
    } catch (err) {
      error.value = err instanceof Error ? err.message : "Unable to load jobs.";
      jobs.value = [];
    } finally {
      loading.value = false;
    }
  }

  async function fetchJob(id: number): Promise<Job> {
    return request<Job>(`/jobs/${id}`);
  }

  async function createJob(input: JobInput): Promise<Job> {
    const job = await request<Job>("/jobs", {
      method: "POST",
      body: JSON.stringify(input),
    });
    jobs.value = [job, ...jobs.value];
    return job;
  }

  async function updateJob(id: number, input: JobInput): Promise<Job> {
    const job = await request<Job>(`/jobs/${id}`, {
      method: "PUT",
      body: JSON.stringify(input),
    });
    jobs.value = jobs.value.map((existing) => (existing.id === id ? job : existing));
    return job;
  }

  async function archiveJob(id: number) {
    await request<void>(`/jobs/${id}`, { method: "DELETE" });
    jobs.value = jobs.value.filter((job) => job.id !== id);
  }

  function getJobFromCache(id: number): Job | undefined {
    return jobs.value.find((job) => job.id === id);
  }

  return {
    jobs,
    loading,
    error,
    overdueJobs,
    activeCount,
    inProgressCount,
    fetchJobs,
    fetchJob,
    createJob,
    updateJob,
    archiveJob,
    getJobFromCache,
  };
});
