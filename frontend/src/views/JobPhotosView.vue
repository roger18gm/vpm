<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { RouterLink } from "vue-router";
import type { Job } from "@/types/job";
import { useJobsStore } from "@/stores/jobs";
import { usePhotosStore } from "@/stores/photos";

const props = defineProps<{ id: number }>();
const jobsStore = useJobsStore();
const photosStore = usePhotosStore();
const job = ref<Job | null>(null);

onMounted(async () => {
  job.value = jobsStore.getJobFromCache(props.id) ?? (await jobsStore.fetchJob(props.id));
});

const photos = computed(() => photosStore.list(props.id));

function kindLabel(kind: string) {
  return kind.charAt(0).toUpperCase() + kind.slice(1);
}
</script>

<template>
  <RouterLink :to="{ name: 'job-detail', params: { id } }" class="text-sm text-primary mb-2 inline-block">← Job</RouterLink>
  <h1 class="text-xl font-bold mb-1">Photos</h1>
  <p class="text-sm text-muted mb-4">{{ job?.title ?? "…" }}</p>

  <ul v-if="photos.length" class="space-y-4 mb-20">
    <li v-for="photo in photos" :key="photo.id" class="flex gap-3 bg-surface border border-border rounded-lg p-3">
      <div class="w-16 h-16 rounded-md bg-page shrink-0 flex items-center justify-center text-xs text-muted">img</div>
      <div class="min-w-0">
        <span class="text-xs font-semibold" :class="photo.kind === 'progress' ? 'text-primary' : 'text-muted'">{{ kindLabel(photo.kind) }}</span>
        <p class="text-sm truncate">{{ photo.caption ?? "No caption" }}</p>
        <p class="text-xs text-muted">{{ new Date(photo.takenAt).toLocaleString() }} · {{ photo.uploadedBy }}</p>
      </div>
    </li>
  </ul>
  <p v-else class="text-sm text-muted mb-20">No photos yet.</p>

  <RouterLink
    :to="{ name: 'job-photo-upload', params: { id } }"
    class="fixed bottom-20 md:bottom-6 left-4 right-4 max-w-md mx-auto bg-primary text-white text-center font-semibold rounded-lg py-3.5 text-sm shadow-lg min-h-[48px] flex items-center justify-center z-10"
  >
    + Add photo
  </RouterLink>
</template>
