import { defineStore } from "pinia";
import { ref } from "vue";
import { request, resolveAssetUrl, uploadForm } from "@/lib/api";
import { useJobsStore } from "@/stores/jobs";
import type { JobPhoto, PhotoKind } from "@/types/job";

export const usePhotosStore = defineStore("photos", () => {
  const byJobId = ref<Record<number, JobPhoto[]>>({});
  const loading = ref(false);

  function normalizePhoto(raw: Record<string, unknown>): JobPhoto {
    return {
      id: Number(raw.id ?? raw.Id ?? 0),
      jobId: Number(raw.jobId ?? raw.JobId ?? 0),
      photoKind: (raw.photoKind ?? raw.PhotoKind ?? "progress") as PhotoKind,
      caption: (raw.caption ?? raw.Caption ?? null) as string | null,
      takenAt: String(raw.takenAt ?? raw.TakenAt ?? new Date().toISOString()),
      uploadedByName: String(raw.uploadedByName ?? raw.UploadedByName ?? "Unknown"),
      url: resolveAssetUrl(String(raw.url ?? raw.Url ?? "")),
    };
  }

  async function fetchPhotos(jobId: number): Promise<JobPhoto[]> {
    loading.value = true;
    try {
      const rows = await request<Record<string, unknown>[]>(`/jobs/${jobId}/photos`);
      const photos = rows.map(normalizePhoto);
      byJobId.value[jobId] = photos;
      return photos;
    } finally {
      loading.value = false;
    }
  }

  function list(jobId: number): JobPhoto[] {
    return byJobId.value[jobId] ?? [];
  }

  async function upload(
    jobId: number,
    file: File,
    kind: PhotoKind,
    caption: string | null
  ): Promise<JobPhoto> {
    const form = new FormData();
    form.append("file", file);
    form.append("photoKind", kind);
    if (caption) {
      form.append("caption", caption);
    }

    const raw = await uploadForm<Record<string, unknown>>(`/jobs/${jobId}/photos`, form);
    const photo = normalizePhoto(raw);
    const current = byJobId.value[jobId] ?? [];
    byJobId.value[jobId] = [photo, ...current];
    useJobsStore().incrementPhotoCount(jobId);
    return photo;
  }

  return { byJobId, loading, fetchPhotos, list, upload };
});
