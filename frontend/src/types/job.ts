export type JobStatus = "scheduled" | "in_progress" | "completed" | "cancelled";
export type JobPriority = "low" | "normal" | "high" | "urgent";

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
};

export type PhotoKind = "before" | "after" | "progress";

export type JobPhoto = {
  id: string;
  jobId: number;
  kind: PhotoKind;
  caption: string | null;
  takenAt: string;
  uploadedBy: string;
};
