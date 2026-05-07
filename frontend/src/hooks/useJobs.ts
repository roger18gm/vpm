import { useState, useEffect } from "react";

export interface Job {
  id: number;
  title: string;
  description?: string;
  status: string;
  createdAt: string;
  dueDate?: string;
}

const API_URL = "https://vision-paint-api.azurewebsites.net/api";

export function useJobs() {
  const [jobs, setJobs] = useState<Job[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetchJobs();
  }, []);

  const fetchJobs = async () => {
    try {
      setLoading(true);
      const response = await fetch(`${API_URL}/jobs`);
      if (!response.ok) throw new Error("Failed to fetch jobs");
      const data = await response.json();
      setJobs(data);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unknown error");
    } finally {
      setLoading(false);
    }
  };

  const createJob = async (job: Omit<Job, "id" | "createdAt">) => {
    try {
      const response = await fetch(`${API_URL}/jobs`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(job),
      });
      if (!response.ok) throw new Error("Failed to create job");
      const newJob = await response.json();
      setJobs([...jobs, newJob]);
      return newJob;
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unknown error");
    }
  };

  const updateJob = async (id: number, job: Partial<Job>) => {
    try {
      const response = await fetch(`${API_URL}/jobs/${id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(job),
      });
      if (!response.ok) throw new Error("Failed to update job");
      setJobs(jobs.map((j) => (j.id === id ? { ...j, ...job } : j)));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unknown error");
    }
  };

  const deleteJob = async (id: number) => {
    try {
      const response = await fetch(`${API_URL}/jobs/${id}`, {
        method: "DELETE",
      });
      if (!response.ok) throw new Error("Failed to delete job");
      setJobs(jobs.filter((j) => j.id !== id));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unknown error");
    }
  };

  return { jobs, loading, error, fetchJobs, createJob, updateJob, deleteJob };
}
