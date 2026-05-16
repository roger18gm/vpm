import type { Job } from "@/types/job";

export function formatJobAddress(job: Job): string | null {
  const parts = [job.addressLine1, job.city, job.stateRegion].filter(Boolean);
  return parts.length > 0 ? parts.join(", ") : null;
}

export function isJobOverdue(job: Job, now = new Date()): boolean {
  if (!job.dueAt) return false;
  if (job.status === "completed" || job.status === "cancelled") return false;
  return new Date(job.dueAt) < now;
}

export function statusLabel(status: string): string {
  return status.replace(/_/g, " ").replace(/\b\w/g, (c) => c.toUpperCase());
}

export function dateInputValue(iso: string | null | undefined): string {
  if (!iso) return "";
  return iso.slice(0, 10);
}

export function dateToIso(date: string): string | null {
  if (!date) return null;
  return new Date(`${date}T12:00:00`).toISOString();
}
