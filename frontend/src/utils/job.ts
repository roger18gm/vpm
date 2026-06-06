import type { Job, JobInput } from "@/types/job";

export function jobToInput(job: Job, overrides: Partial<JobInput> = {}): JobInput {
  return {
    title: job.title,
    description: job.description,
    status: job.status,
    priority: job.priority,
    addressLine1: job.addressLine1,
    addressLine2: job.addressLine2,
    city: job.city,
    stateRegion: job.stateRegion,
    postalCode: job.postalCode,
    countryCode: job.countryCode,
    scheduledStartAt: job.scheduledStartAt,
    scheduledEndAt: job.scheduledEndAt,
    dueAt: job.dueAt,
    ...overrides,
  };
}

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

export function mapsUrl(job: Job): string | null {
  const query = [job.addressLine1, job.city, job.stateRegion, job.postalCode].filter(Boolean).join(", ");
  if (!query) return null;
  return `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(query)}`;
}

export function formatMinutes(minutes: number): string {
  return (minutes / 60).toFixed(1);
}
