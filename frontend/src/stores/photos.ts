import { defineStore } from "pinia";
import { ref } from "vue";
import type { JobPhoto, PhotoKind } from "@/types/job";

/** Local MVP store until job_photo API + storage exist. */
export const usePhotosStore = defineStore("photos", () => {
  const byJobId = ref<Record<number, JobPhoto[]>>({});

  function list(jobId: number): JobPhoto[] {
    return byJobId.value[jobId] ?? [];
  }

  function add(jobId: number, kind: PhotoKind, caption: string | null, uploadedBy: string) {
    const photo: JobPhoto = {
      id: crypto.randomUUID(),
      jobId,
      kind,
      caption,
      takenAt: new Date().toISOString(),
      uploadedBy,
    };
    const current = byJobId.value[jobId] ?? [];
    byJobId.value[jobId] = [photo, ...current];
    return photo;
  }

  return { list, add };
});
