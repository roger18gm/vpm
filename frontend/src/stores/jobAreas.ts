import { defineStore } from "pinia";
import { ref } from "vue";
import { request } from "@/lib/api";
import type { JobArea, JobAreaStatus } from "@/types/job";

export const useJobAreasStore = defineStore("jobAreas", () => {
  const byJobId = ref<Record<number, JobArea[]>>({});
  const loading = ref(false);

  function normalize(raw: Record<string, unknown>): JobArea {
    return {
      id: Number(raw.id ?? raw.Id ?? 0),
      jobId: Number(raw.jobId ?? raw.JobId ?? 0),
      name: String(raw.name ?? raw.Name ?? ""),
      status: (raw.status ?? raw.Status ?? "not_started") as JobAreaStatus,
      sortOrder: Number(raw.sortOrder ?? raw.SortOrder ?? 0),
      startedAt: (raw.startedAt ?? raw.StartedAt ?? null) as string | null,
      completedAt: (raw.completedAt ?? raw.CompletedAt ?? null) as string | null,
    };
  }

  async function fetchAreas(jobId: number): Promise<JobArea[]> {
    loading.value = true;
    try {
      const rows = await request<Record<string, unknown>[]>(`/jobs/${jobId}/areas`);
      const areas = rows.map(normalize);
      byJobId.value[jobId] = areas;
      return areas;
    } finally {
      loading.value = false;
    }
  }

  function list(jobId: number): JobArea[] {
    return byJobId.value[jobId] ?? [];
  }

  async function createArea(jobId: number, name: string): Promise<JobArea> {
    const raw = await request<Record<string, unknown>>(`/jobs/${jobId}/areas`, {
      method: "POST",
      body: JSON.stringify({ name }),
    });
    const area = normalize(raw);
    byJobId.value[jobId] = [...list(jobId), area];
    return area;
  }

  async function updateArea(
    jobId: number,
    areaId: number,
    patch: { name?: string; status?: JobAreaStatus }
  ): Promise<JobArea> {
    const raw = await request<Record<string, unknown>>(`/jobs/${jobId}/areas/${areaId}`, {
      method: "PATCH",
      body: JSON.stringify(patch),
    });
    const area = normalize(raw);
    byJobId.value[jobId] = list(jobId).map((item) => (item.id === areaId ? area : item));
    return area;
  }

  async function deleteArea(jobId: number, areaId: number): Promise<void> {
    await request(`/jobs/${jobId}/areas/${areaId}`, { method: "DELETE" });
    byJobId.value[jobId] = list(jobId).filter((item) => item.id !== areaId);
  }

  return { byJobId, loading, fetchAreas, list, createArea, updateArea, deleteArea };
});
