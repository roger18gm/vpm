<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { RouterLink } from "vue-router";
import PhotoLightbox from "@/components/photo/PhotoLightbox.vue";
import type { Job, JobPhoto } from "@/types/job";
import { useJobsStore } from "@/stores/jobs";
import { usePhotosStore } from "@/stores/photos";

const props = defineProps<{ id: number }>();
const jobsStore = useJobsStore();
const photosStore = usePhotosStore();
const job = ref<Job | null>(null);
const photos = ref(photosStore.list(props.id));
const loading = ref(true);
const lightboxOpen = ref(false);
const lightboxIndex = ref(0);

const viewablePhotos = computed(() => photos.value.filter((photo) => photo.url));

onMounted(async () => {
  try {
    job.value = jobsStore.getJobFromCache(props.id) ?? (await jobsStore.fetchJob(props.id));
    photos.value = await photosStore.fetchPhotos(props.id);
  } finally {
    loading.value = false;
  }
});

function kindLabel(kind: string) {
  return kind.charAt(0).toUpperCase() + kind.slice(1);
}

function openLightbox(photo: JobPhoto) {
  if (!photo.url) {
    return;
  }

  const index = viewablePhotos.value.findIndex((item) => item.id === photo.id);
  if (index < 0) {
    return;
  }

  lightboxIndex.value = index;
  lightboxOpen.value = true;
}

function closeLightbox() {
  lightboxOpen.value = false;
}
</script>

<template>
  <RouterLink :to="{ name: 'job-detail', params: { id } }" class="text-sm text-primary mb-2 inline-block">← Job</RouterLink>
  <h1 class="text-xl font-bold mb-1">Photos</h1>
  <p class="text-sm text-muted mb-4">{{ job?.title ?? "…" }}</p>

  <p v-if="loading" class="text-sm text-muted mb-20">Loading…</p>
  <ul v-else-if="photos.length" class="space-y-4 mb-20">
    <li v-for="photo in photos" :key="photo.id" class="flex gap-3 bg-surface border border-border rounded-lg p-3">
      <button
        v-if="photo.url"
        type="button"
        class="shrink-0 rounded-md focus:outline-none focus-visible:ring-2 focus-visible:ring-primary"
        aria-label="View photo full size"
        @click="openLightbox(photo)"
      >
        <img
          :src="photo.url"
          alt=""
          class="w-16 h-16 rounded-md object-cover bg-page"
        />
      </button>
      <div
        v-else
        class="w-16 h-16 rounded-md bg-page border border-border shrink-0 flex items-center justify-center text-[10px] text-muted text-center px-1"
      >
        Unavailable
      </div>
      <div class="min-w-0">
        <span class="text-xs font-semibold" :class="photo.photoKind === 'progress' ? 'text-primary' : 'text-muted'">{{ kindLabel(photo.photoKind) }}</span>
        <p class="text-sm truncate">{{ photo.caption ?? "No caption" }}</p>
        <p class="text-xs text-muted">{{ new Date(photo.takenAt).toLocaleString() }} · {{ photo.uploadedByName }}</p>
      </div>
    </li>
  </ul>
  <p v-else class="text-sm text-muted mb-20">No photos yet.</p>

  <PhotoLightbox
    :open="lightboxOpen"
    :photos="viewablePhotos"
    :index="lightboxIndex"
    @close="closeLightbox"
    @update:index="lightboxIndex = $event"
  />

  <RouterLink
    :to="{ name: 'job-photo-upload', params: { id } }"
    class="fixed bottom-20 md:bottom-6 left-4 right-4 max-w-md mx-auto bg-primary text-white text-center font-semibold rounded-lg py-3.5 text-sm shadow-lg min-h-[48px] flex items-center justify-center z-10"
  >
    + Add photo
  </RouterLink>
</template>
