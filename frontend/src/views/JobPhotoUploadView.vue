<script setup lang="ts">
import { ref } from "vue";
import { useRouter } from "vue-router";
import { RouterLink } from "vue-router";
import VpButton from "@/components/ui/VpButton.vue";
import VpSelect from "@/components/ui/VpSelect.vue";
import type { PhotoKind } from "@/types/job";
import { usePhotosStore } from "@/stores/photos";

const props = defineProps<{ id: number }>();
const router = useRouter();
const photosStore = usePhotosStore();

const kind = ref<PhotoKind>("progress");
const caption = ref("");
const file = ref<File | null>(null);
const busy = ref(false);
const error = ref<string | null>(null);

function onFileChange(event: Event) {
  const input = event.target as HTMLInputElement;
  file.value = input.files?.[0] ?? null;
}

async function onSubmit() {
  if (!file.value) return;
  busy.value = true;
  error.value = null;
  try {
    await photosStore.upload(props.id, file.value, kind.value, caption.value || null);
    await router.push({ name: "job-photos", params: { id: props.id } });
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Upload failed.";
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <RouterLink :to="{ name: 'job-photos', params: { id } }" class="text-sm text-primary mb-3 inline-block">← Photos</RouterLink>
  <h1 class="text-xl font-bold mb-6">Add photo</h1>
  <p v-if="error" class="text-sm text-error mb-3">{{ error }}</p>

  <form class="space-y-4" @submit.prevent="onSubmit">
    <label class="flex flex-col items-center justify-center border-2 border-dashed border-border rounded-lg bg-surface p-8 min-h-[160px] cursor-pointer">
      <span class="text-3xl mb-2">📷</span>
      <span class="text-sm font-semibold">{{ file?.name ?? "Take photo or choose file" }}</span>
      <input type="file" accept="image/*" capture="environment" class="sr-only" @change="onFileChange" />
    </label>

    <div class="bg-surface border border-border rounded-lg p-4">
      <VpSelect v-model="kind" label="Photo type">
        <option value="progress">Progress</option>
        <option value="before">Before</option>
        <option value="after">After</option>
      </VpSelect>
    </div>

    <label class="block bg-surface border border-border rounded-lg p-4">
      <span class="text-xs text-muted mb-1 block">Caption (optional)</span>
      <input
        v-model="caption"
        type="text"
        class="w-full border border-border rounded-md px-3 py-2.5 text-sm min-h-[44px]"
        placeholder="e.g. North wall — first coat"
      />
    </label>

    <VpButton type="submit" block :disabled="busy || !file">{{ busy ? "Uploading…" : "Save photo" }}</VpButton>
  </form>
</template>
