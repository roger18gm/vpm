<script setup lang="ts">
import { computed, onUnmounted, watch } from "vue";
import { Icon } from "@iconify/vue";
import type { JobPhoto } from "@/types/job";

const props = defineProps<{
  open: boolean;
  photos: JobPhoto[];
  index: number;
}>();

const emit = defineEmits<{
  close: [];
  "update:index": [index: number];
}>();

const photo = computed(() => props.photos[props.index] ?? null);
const counter = computed(() => `${props.index + 1} of ${props.photos.length}`);
const canGoPrev = computed(() => props.index > 0);
const canGoNext = computed(() => props.index < props.photos.length - 1);
const showNav = computed(() => props.photos.length > 1);

let touchStartX = 0;

function kindLabel(kind: string) {
  return kind.charAt(0).toUpperCase() + kind.slice(1);
}

function goPrev() {
  if (canGoPrev.value) {
    emit("update:index", props.index - 1);
  }
}

function goNext() {
  if (canGoNext.value) {
    emit("update:index", props.index + 1);
  }
}

function onKeydown(event: KeyboardEvent) {
  if (!props.open) {
    return;
  }
  if (event.key === "Escape") {
    emit("close");
  } else if (event.key === "ArrowLeft") {
    goPrev();
  } else if (event.key === "ArrowRight") {
    goNext();
  }
}

function onTouchStart(event: TouchEvent) {
  touchStartX = event.changedTouches[0]?.clientX ?? 0;
}

function onTouchEnd(event: TouchEvent) {
  const endX = event.changedTouches[0]?.clientX ?? 0;
  const delta = endX - touchStartX;
  if (delta > 50) {
    goPrev();
  } else if (delta < -50) {
    goNext();
  }
}

watch(
  () => props.open,
  (open) => {
    if (open) {
      window.addEventListener("keydown", onKeydown);
      document.body.style.overflow = "hidden";
    } else {
      window.removeEventListener("keydown", onKeydown);
      document.body.style.overflow = "";
    }
  },
  { immediate: true }
);

onUnmounted(() => {
  window.removeEventListener("keydown", onKeydown);
  document.body.style.overflow = "";
});
</script>

<template>
  <div
    v-if="open && photo"
    class="fixed inset-0 z-50 flex flex-col bg-black/90 text-white"
    role="dialog"
    aria-modal="true"
    aria-label="Photo viewer"
    @click.self="emit('close')"
    @touchstart.passive="onTouchStart"
    @touchend.passive="onTouchEnd"
  >
    <div class="flex items-center justify-between gap-3 px-4 py-3 shrink-0">
      <p v-if="showNav" class="text-sm text-white/80">{{ counter }}</p>
      <span v-else class="text-sm text-white/80">Photo</span>
      <button
        type="button"
        class="min-h-[44px] min-w-[44px] inline-flex items-center justify-center rounded-lg hover:bg-white/10"
        aria-label="Close photo viewer"
        @click="emit('close')"
      >
        <Icon icon="mdi:close" class="text-2xl" aria-hidden="true" />
      </button>
    </div>

    <div class="relative flex-1 flex items-center justify-center px-4 min-h-0">
      <button
        v-if="showNav"
        type="button"
        class="absolute left-2 z-10 min-h-[44px] min-w-[44px] inline-flex items-center justify-center rounded-full bg-black/40 hover:bg-black/60 disabled:opacity-30 disabled:pointer-events-none"
        aria-label="Previous photo"
        :disabled="!canGoPrev"
        @click.stop="goPrev"
      >
        <Icon icon="mdi:chevron-left" class="text-3xl" aria-hidden="true" />
      </button>

      <img
        :src="photo.url"
        :alt="photo.caption ?? `${kindLabel(photo.photoKind)} photo`"
        class="max-h-[calc(100vh-12rem)] max-w-full object-contain select-none"
        @click.stop
      />

      <button
        v-if="showNav"
        type="button"
        class="absolute right-2 z-10 min-h-[44px] min-w-[44px] inline-flex items-center justify-center rounded-full bg-black/40 hover:bg-black/60 disabled:opacity-30 disabled:pointer-events-none"
        aria-label="Next photo"
        :disabled="!canGoNext"
        @click.stop="goNext"
      >
        <Icon icon="mdi:chevron-right" class="text-3xl" aria-hidden="true" />
      </button>
    </div>

    <div class="shrink-0 px-4 pb-6 pt-2 text-center max-w-lg mx-auto w-full">
      <span
        class="text-xs font-semibold"
        :class="photo.photoKind === 'progress' ? 'text-red-300' : 'text-white/70'"
      >
        {{ kindLabel(photo.photoKind) }}
      </span>
      <p class="text-sm mt-1">{{ photo.caption ?? "No caption" }}</p>
      <p class="text-xs text-white/60 mt-1">
        {{ new Date(photo.takenAt).toLocaleString() }} · {{ photo.uploadedByName }}
      </p>
    </div>
  </div>
</template>
