import { useCallback, useEffect, useMemo, useState } from "react";
import { request } from "../lib/api";

export type Job = {
  id: number;
  companyId: number;
  clientId: number | null;
  createdByPersonId: number | null;
  title: string;
  description: string | null;
  status: string;
  priority: string;
  addressLine1: string | null;
  addressLine2: string | null;
  city: string | null;
  stateRegion: string | null;
  postalCode: string | null;
  countryCode: string | null;
  scheduledStartAt: string | null;
  scheduledEndAt: string | null;
  dueAt: string | null;
  startedAt: string | null;
  completedAt: string | null;
  closedAt: string | null;
  createdAt: string;
  updatedAt: string;
};

export type JobInput = {
  clientId?: number | null;
  title: string;
  description?: string | null;
  status?: string;
  priority?: string;
  addressLine1?: string | null;
  addressLine2?: string | null;
  city?: string | null;
  stateRegion?: string | null;
  postalCode?: string | null;
  countryCode?: string | null;
  scheduledStartAt?: string | null;
  scheduledEndAt?: string | null;
  dueAt?: string | null;
  startedAt?: string | null;
  completedAt?: string | null;
  closedAt?: string | null;
};

export function useJobs() {
  const [jobs, setJobs] = useState<Job[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadJobs = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const data = await request<Job[]>("/jobs", {
        method: "GET",
      });
      setJobs(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to load jobs.");
      setJobs([]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadJobs();
  }, [loadJobs]);

  const createJob = useCallback(
    async (input: JobInput) => {
      const job = await request<Job>("/jobs", {
        method: "POST",
        body: JSON.stringify(input),
      });
      setJobs((current) => [job, ...current]);
      return job;
    },
    []
  );

  const updateJob = useCallback(
    async (id: number, input: JobInput) => {
      const job = await request<Job>(`/jobs/${id}`, {
        method: "PUT",
        body: JSON.stringify(input),
      });
      setJobs((current) => current.map((existing) => (existing.id === id ? job : existing)));
      return job;
    },
    []
  );

  const archiveJob = useCallback(async (id: number) => {
    try {
      await request<void>(`/jobs/${id}`, {
        method: "DELETE",
      });
      setJobs((current) => current.filter((job) => job.id !== id));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to archive job.");
    }
  }, []);

  return useMemo(
    () => ({
      jobs,
      loading,
      error,
      reload: loadJobs,
      createJob,
      updateJob,
      archiveJob,
    }),
    [archiveJob, createJob, error, jobs, loadJobs, loading, updateJob]
  );
}
