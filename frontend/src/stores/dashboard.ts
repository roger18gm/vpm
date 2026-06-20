import { defineStore } from "pinia";
import { ref } from "vue";
import { request } from "@/lib/api";
import type { DashboardSummary } from "@/types/dashboard";

function normalizeSummary(raw: Record<string, unknown>): DashboardSummary {
  const workers = Array.isArray(raw.clockedInWorkers) ? raw.clockedInWorkers : [];
  return {
    hoursThisWeekMinutes: Number(raw.hoursThisWeekMinutes ?? 0),
    completedThisWeekCount: Number(raw.completedThisWeekCount ?? 0),
    clockedInWorkers: workers.map((worker) => {
      const row = worker as Record<string, unknown>;
      return {
        personId: Number(row.personId),
        name: String(row.name ?? ""),
        jobId: Number(row.jobId),
        jobTitle: String(row.jobTitle ?? ""),
        clockInAt: String(row.clockInAt ?? ""),
        onBreak: Boolean(row.onBreak),
      };
    }),
  };
}

export const useDashboardStore = defineStore("dashboard", () => {
  const summary = ref<DashboardSummary | null>(null);
  const loading = ref(false);
  const error = ref<string | null>(null);

  async function fetchSummary() {
    loading.value = true;
    error.value = null;
    try {
      const raw = await request<Record<string, unknown>>("/dashboard/summary");
      summary.value = normalizeSummary(raw);
    } catch (err) {
      error.value = err instanceof Error ? err.message : "Unable to load dashboard summary.";
      summary.value = null;
    } finally {
      loading.value = false;
    }
  }

  return {
    summary,
    loading,
    error,
    fetchSummary,
  };
});
