import { useCallback, useEffect, useMemo, useState } from "react";

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

const API_URL = import.meta.env.VITE_API_URL ?? "https://vision-paint-api.azurewebsites.net/api";

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_URL}${path}`, {
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
      ...(init?.headers ?? {}),
    },
    ...init,
  });

  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || `Request failed with ${response.status}`);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

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
        headers: {},
      });
      setJobs(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to load jobs.");
      setJobs([]);
      throw err;
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
    await request<void>(`/jobs/${id}`, {
      method: "DELETE",
      headers: {},
    });
    setJobs((current) => current.filter((job) => job.id !== id));
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
