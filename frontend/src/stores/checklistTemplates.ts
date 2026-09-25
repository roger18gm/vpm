import { defineStore } from "pinia";
import { ref } from "vue";
import { request } from "@/lib/api";
import type { ChecklistTemplate, ChecklistTemplateItem } from "@/types/checklist";

function normalizeItem(raw: Record<string, unknown>): ChecklistTemplateItem {
  return {
    id: Number(raw.id ?? raw.Id ?? 0),
    title: String(raw.title ?? raw.Title ?? ""),
    sortOrder: Number(raw.sortOrder ?? raw.SortOrder ?? 0),
    isRequired: Boolean(raw.isRequired ?? raw.IsRequired ?? true),
  };
}

function normalizeTemplate(raw: Record<string, unknown>): ChecklistTemplate {
  const items = Array.isArray(raw.items ?? raw.Items) ? (raw.items ?? raw.Items) as Record<string, unknown>[] : [];
  return {
    id: Number(raw.id ?? raw.Id ?? 0),
    name: String(raw.name ?? raw.Name ?? ""),
    isDefault: Boolean(raw.isDefault ?? raw.IsDefault ?? false),
    items: items.map(normalizeItem),
  };
}

export const useChecklistTemplatesStore = defineStore("checklistTemplates", () => {
  const templates = ref<ChecklistTemplate[]>([]);

  async function fetchTemplates(): Promise<ChecklistTemplate[]> {
    const rows = await request<Record<string, unknown>[]>("/checklist-templates");
    templates.value = rows.map(normalizeTemplate);
    return templates.value;
  }

  async function createTemplate(name: string): Promise<ChecklistTemplate> {
    const raw = await request<Record<string, unknown>>("/checklist-templates", {
      method: "POST",
      body: JSON.stringify({ name, isDefault: false }),
    });
    const template = normalizeTemplate(raw);
    templates.value = [...templates.value, template].sort((a, b) => a.name.localeCompare(b.name) || a.id - b.id);
    return template;
  }

  async function updateTemplate(templateId: number, patch: { name?: string; isDefault?: boolean }): Promise<void> {
    await request(`/checklist-templates/${templateId}`, {
      method: "PATCH",
      body: JSON.stringify(patch),
    });
    await fetchTemplates();
  }

  async function deleteTemplate(templateId: number): Promise<void> {
    await request(`/checklist-templates/${templateId}`, { method: "DELETE" });
    await fetchTemplates();
  }

  async function createItem(templateId: number, title: string, isRequired = true): Promise<void> {
    await request(`/checklist-templates/${templateId}/items`, {
      method: "POST",
      body: JSON.stringify({ title, isRequired }),
    });
    await fetchTemplates();
  }

  async function updateItem(
    templateId: number,
    itemId: number,
    patch: { title?: string; isRequired?: boolean }
  ): Promise<void> {
    await request(`/checklist-templates/${templateId}/items/${itemId}`, {
      method: "PATCH",
      body: JSON.stringify(patch),
    });
    await fetchTemplates();
  }

  async function deleteItem(templateId: number, itemId: number): Promise<void> {
    await request(`/checklist-templates/${templateId}/items/${itemId}`, { method: "DELETE" });
    await fetchTemplates();
  }

  return {
    templates,
    fetchTemplates,
    createTemplate,
    updateTemplate,
    deleteTemplate,
    createItem,
    updateItem,
    deleteItem,
  };
});
