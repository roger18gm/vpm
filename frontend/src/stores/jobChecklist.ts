import { defineStore } from "pinia";
import { ref } from "vue";
import { request } from "@/lib/api";
import type { ChecklistItemStatus, JobChecklist, JobChecklistItem } from "@/types/checklist";

function normalizeItem(raw: Record<string, unknown>): JobChecklistItem {
  return {
    templateItemId: Number(raw.templateItemId ?? raw.TemplateItemId ?? 0),
    title: String(raw.title ?? raw.Title ?? ""),
    sortOrder: Number(raw.sortOrder ?? raw.SortOrder ?? 0),
    isRequired: Boolean(raw.isRequired ?? raw.IsRequired ?? false),
    status: String(raw.status ?? raw.Status ?? "pending") as ChecklistItemStatus,
    completedByPersonId: (raw.completedByPersonId ?? raw.CompletedByPersonId ?? null) as number | null,
    completedByName: (raw.completedByName ?? raw.CompletedByName ?? null) as string | null,
    completedAt: (raw.completedAt ?? raw.CompletedAt ?? null) as string | null,
  };
}

function normalizeChecklist(raw: Record<string, unknown>): JobChecklist {
  const items = Array.isArray(raw.items ?? raw.Items) ? (raw.items ?? raw.Items) as Record<string, unknown>[] : [];
  const templateId = raw.templateId ?? raw.TemplateId;
  const templateName = raw.templateName ?? raw.TemplateName;
  return {
    templateId: templateId == null ? null : Number(templateId),
    templateName: templateName == null ? null : String(templateName),
    items: items.map(normalizeItem),
  };
}

export const useJobChecklistStore = defineStore("jobChecklist", () => {
  const byJobId = ref<Record<number, JobChecklist>>({});

  function list(jobId: number): JobChecklist {
    return byJobId.value[jobId] ?? { templateId: null, templateName: null, items: [] };
  }

  async function fetchChecklist(jobId: number): Promise<JobChecklist> {
    const raw = await request<Record<string, unknown>>(`/jobs/${jobId}/checklist`);
    const checklist = normalizeChecklist(raw);
    byJobId.value[jobId] = checklist;
    return checklist;
  }

  async function applyTemplate(jobId: number, templateId: number): Promise<JobChecklist> {
    const raw = await request<Record<string, unknown>>(`/jobs/${jobId}/checklist`, {
      method: "POST",
      body: JSON.stringify({ templateId }),
    });
    const checklist = normalizeChecklist(raw);
    byJobId.value[jobId] = checklist;
    return checklist;
  }

  async function updateItemStatus(
    jobId: number,
    templateItemId: number,
    status: ChecklistItemStatus
  ): Promise<void> {
    const raw = await request<Record<string, unknown>>(`/jobs/${jobId}/checklist/items/${templateItemId}`, {
      method: "PATCH",
      body: JSON.stringify({ status }),
    });
    const updated = normalizeItem(raw);
    const current = list(jobId);
    byJobId.value[jobId] = {
      ...current,
      items: current.items.map((item) => (item.templateItemId === templateItemId ? updated : item)),
    };
  }

  return { byJobId, list, fetchChecklist, applyTemplate, updateItemStatus };
});
