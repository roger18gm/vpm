<script setup lang="ts">
import { onMounted, ref } from "vue";
import { RouterLink, useRouter } from "vue-router";
import StatusBadge from "@/components/job/StatusBadge.vue";
import ConfirmDialog from "@/components/ui/ConfirmDialog.vue";
import VpButton from "@/components/ui/VpButton.vue";
import VpSelect from "@/components/ui/VpSelect.vue";
import type { Job, JobArea, JobAreaStatus } from "@/types/job";
import { ApiRequestError } from "@/lib/api";
import { useAuthStore } from "@/stores/auth";
import { useJobsStore } from "@/stores/jobs";
import { useJobAreasStore } from "@/stores/jobAreas";
import { useToastStore } from "@/stores/toast";

const props = defineProps<{ id: number }>();
const auth = useAuthStore();
const jobsStore = useJobsStore();
const areasStore = useJobAreasStore();
const toast = useToastStore();
const router = useRouter();

const job = ref<Job | null>(null);
const areas = ref<JobArea[]>([]);
const newName = ref("");
const error = ref<string | null>(null);
const busy = ref(false);
const pendingDelete = ref<JobArea | null>(null);

onMounted(async () => {
  try {
    job.value = jobsStore.getJobFromCache(props.id) ?? (await jobsStore.fetchJob(props.id));
    areas.value = await areasStore.fetchAreas(props.id);
  } catch (err) {
    if (err instanceof ApiRequestError && err.status === 403) {
      await router.replace({ name: "forbidden" });
      return;
    }
    error.value = err instanceof Error ? err.message : "Unable to load areas.";
  }
});

async function addArea() {
  const name = newName.value.trim();
  if (!name || busy.value) return;
  busy.value = true;
  error.value = null;
  try {
    await areasStore.createArea(props.id, name);
    areas.value = areasStore.list(props.id);
    newName.value = "";
    toast.push("Area added");
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Unable to add area.";
  } finally {
    busy.value = false;
  }
}

async function changeStatus(area: JobArea, status: string) {
  error.value = null;
  try {
    await areasStore.updateArea(props.id, area.id, { status: status as JobAreaStatus });
    areas.value = areasStore.list(props.id);
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Unable to update area.";
  }
}

async function renameArea(area: JobArea, event: Event) {
  const name = (event.target as HTMLInputElement).value.trim();
  if (!name || name === area.name) return;
  error.value = null;
  try {
    await areasStore.updateArea(props.id, area.id, { name });
    areas.value = areasStore.list(props.id);
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Unable to rename area.";
  }
}

async function confirmDelete() {
  if (!pendingDelete.value) return;
  error.value = null;
  try {
    await areasStore.deleteArea(props.id, pendingDelete.value.id);
    areas.value = areasStore.list(props.id);
    toast.push("Area removed");
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Unable to delete area.";
  } finally {
    pendingDelete.value = null;
  }
}
</script>

<template>
  <RouterLink :to="{ name: 'job-detail', params: { id } }" class="text-sm text-primary mb-2 inline-block">← Job</RouterLink>
  <h1 class="text-xl font-bold mb-1">Areas</h1>
  <p class="text-sm text-muted mb-4">{{ job?.title ?? "…" }}</p>
  <p v-if="error" class="text-sm text-error mb-3">{{ error }}</p>

  <form v-if="auth.isManager" class="flex gap-2 mb-4" @submit.prevent="addArea">
    <label class="flex-1">
      <span class="sr-only">Area name</span>
      <input
        v-model="newName"
        type="text"
        maxlength="80"
        class="w-full border border-border rounded-md px-3 py-2.5 text-sm min-h-[44px] bg-surface"
        placeholder="e.g. Kitchen"
        aria-label="Area name"
      />
    </label>
    <VpButton type="submit" :disabled="busy || !newName.trim()">Add area</VpButton>
  </form>
  <p v-if="auth.isManager && !areas.length" class="text-sm text-muted mb-4">
    Add rooms or surfaces such as Kitchen, Exterior Trim, or Bedroom 1.
  </p>

  <ul v-if="areas.length" class="space-y-3 mb-8">
    <li
      v-for="area in areas"
      :key="area.id"
      class="bg-surface border border-border rounded-lg p-3 flex flex-col gap-2"
    >
      <div class="flex items-center gap-2">
        <input
          v-if="auth.isManager"
          :value="area.name"
          class="flex-1 border border-border rounded-md px-3 py-2 text-sm min-h-[44px]"
          :aria-label="`Name for ${area.name}`"
          @change="renameArea(area, $event)"
        />
        <p v-else class="flex-1 font-medium text-sm">{{ area.name }}</p>
        <StatusBadge :status="area.status" />
      </div>
      <div v-if="auth.isManager" class="flex flex-wrap gap-2 items-end">
        <div class="flex-1 min-w-[140px]">
          <VpSelect
            :model-value="area.status"
            :label="`Status for ${area.name}`"
            @update:model-value="changeStatus(area, $event)"
          >
            <option value="not_started">Not started</option>
            <option value="in_progress">In progress</option>
            <option value="completed">Completed</option>
            <option value="blocked">Blocked</option>
          </VpSelect>
        </div>
        <VpButton variant="ghost" @click="pendingDelete = area">Delete</VpButton>
      </div>
    </li>
  </ul>
  <p v-else class="text-sm text-muted">No areas yet.</p>

  <ConfirmDialog
    :open="pendingDelete !== null"
    title="Delete area?"
    :message="pendingDelete ? `Remove ${pendingDelete.name} from this job.` : ''"
    confirm-label="Delete"
    @confirm="confirmDelete"
    @cancel="pendingDelete = null"
  />
</template>
