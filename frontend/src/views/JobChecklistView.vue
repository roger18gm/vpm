<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { RouterLink, useRouter } from "vue-router";
import ConfirmDialog from "@/components/ui/ConfirmDialog.vue";
import VpButton from "@/components/ui/VpButton.vue";
import VpSelect from "@/components/ui/VpSelect.vue";
import { ApiRequestError } from "@/lib/api";
import { useAuthStore } from "@/stores/auth";
import { useChecklistTemplatesStore } from "@/stores/checklistTemplates";
import { useJobChecklistStore } from "@/stores/jobChecklist";
import { useJobsStore } from "@/stores/jobs";
import type { ChecklistItemStatus, ChecklistTemplate, JobChecklist, JobChecklistItem } from "@/types/checklist";
import type { Job } from "@/types/job";

const props = defineProps<{ id: number }>();
const auth = useAuthStore();
const jobsStore = useJobsStore();
const checklistStore = useJobChecklistStore();
const templatesStore = useChecklistTemplatesStore();
const router = useRouter();

const job = ref<Job | null>(null);
const checklist = ref<JobChecklist>({ templateId: null, templateName: null, items: [] });
const templates = ref<ChecklistTemplate[]>([]);
const selectedTemplateId = ref("");
const error = ref<string | null>(null);
const pendingReplaceId = ref<number | null>(null);
const cancelled = computed(() => job.value?.status === "cancelled");

onMounted(async () => {
  try {
    job.value = jobsStore.getJobFromCache(props.id) ?? (await jobsStore.fetchJob(props.id));
    checklist.value = await checklistStore.fetchChecklist(props.id);
    if (auth.isManager) {
      templates.value = await templatesStore.fetchTemplates();
      selectedTemplateId.value = String(checklist.value.templateId ?? templates.value[0]?.id ?? "");
    }
  } catch (err) {
    if (err instanceof ApiRequestError && err.status === 403) {
      await router.replace({ name: "forbidden" });
      return;
    }
    error.value = err instanceof Error ? err.message : "Unable to load checklist.";
    if (err instanceof ApiRequestError && err.status === 404) {
      error.value = "Job not found.";
    }
  }
});

function markedLabel(item: JobChecklistItem): string {
  if (!item.completedAt) return "";
  const when = new Date(item.completedAt).toLocaleString();
  return item.completedByName ? `${item.completedByName} · ${when}` : when;
}

async function apply(templateId: number) {
  error.value = null;
  try {
    checklist.value = await checklistStore.applyTemplate(props.id, templateId);
    selectedTemplateId.value = String(templateId);
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Unable to apply checklist.";
  } finally {
    pendingReplaceId.value = null;
  }
}

function requestApply() {
  const templateId = Number(selectedTemplateId.value);
  if (!templateId) return;
  if (checklist.value.templateId != null && checklist.value.templateId !== templateId) {
    pendingReplaceId.value = templateId;
    return;
  }
  void apply(templateId);
}

async function changeStatus(item: JobChecklistItem, status: string) {
  error.value = null;
  try {
    await checklistStore.updateItemStatus(props.id, item.templateItemId, status as ChecklistItemStatus);
    checklist.value = checklistStore.list(props.id);
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Unable to update item.";
  }
}
</script>

<template>
  <RouterLink :to="{ name: 'job-detail', params: { id } }" class="text-sm text-primary mb-2 inline-block">← Job</RouterLink>
  <div class="flex items-center justify-between gap-2 mb-1">
    <h1 class="text-xl font-bold">Checklist</h1>
    <RouterLink v-if="auth.isManager" :to="{ name: 'checklists' }" class="text-sm text-primary font-semibold">Templates</RouterLink>
  </div>
  <p class="text-sm text-muted mb-4">{{ job?.title ?? "…" }}</p>
  <p v-if="cancelled" class="text-sm text-muted mb-3">This job is cancelled.</p>
  <p v-if="error" class="text-sm text-error mb-3">{{ error }}</p>

  <form v-if="auth.isManager && !cancelled" class="flex flex-wrap gap-2 mb-4" @submit.prevent="requestApply">
    <div class="flex-1 min-w-[180px]">
      <VpSelect v-model="selectedTemplateId" label="Checklist template">
        <option v-for="template in templates" :key="template.id" :value="String(template.id)">{{ template.name }}</option>
      </VpSelect>
    </div>
    <VpButton type="submit" :disabled="!selectedTemplateId">Apply</VpButton>
  </form>

  <p v-if="checklist.templateName" class="text-sm font-semibold mb-3">{{ checklist.templateName }}</p>
  <ul v-if="checklist.items.length" class="space-y-3">
    <li v-for="item in checklist.items" :key="item.templateItemId" class="bg-surface border border-border rounded-lg p-3">
      <p class="text-sm font-medium">
        {{ item.title }}
        <span v-if="item.isRequired" class="text-xs text-muted ml-2">Required</span>
      </p>
      <VpSelect
        v-if="!cancelled"
        class="mt-2"
        :model-value="item.status"
        :label="`Status for ${item.title}`"
        @update:model-value="changeStatus(item, $event)"
      >
        <option value="pending">Pending</option>
        <option value="done">Done</option>
        <option value="not_applicable">Not applicable</option>
      </VpSelect>
      <p v-else class="text-sm mt-2">{{ item.status === "not_applicable" ? "Not applicable" : item.status === "done" ? "Done" : "Pending" }}</p>
      <p v-if="markedLabel(item)" class="text-xs text-muted mt-2">{{ markedLabel(item) }}</p>
    </li>
  </ul>
  <p v-else class="text-sm text-muted">No checklist yet.</p>

  <ConfirmDialog
    :open="pendingReplaceId !== null"
    title="Replace checklist?"
    message="Replace the current checklist? Item statuses will be cleared."
    confirm-label="Replace"
    @confirm="pendingReplaceId !== null && apply(pendingReplaceId)"
    @cancel="pendingReplaceId = null"
  />
</template>
