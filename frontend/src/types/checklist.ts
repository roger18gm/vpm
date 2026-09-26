export type ChecklistItemStatus = "pending" | "done" | "not_applicable";

export type ChecklistTemplateItem = {
  id: number;
  title: string;
  sortOrder: number;
  isRequired: boolean;
};

export type ChecklistTemplate = {
  id: number;
  name: string;
  isDefault: boolean;
  items: ChecklistTemplateItem[];
};

export type JobChecklistItem = {
  templateItemId: number;
  title: string;
  sortOrder: number;
  isRequired: boolean;
  status: ChecklistItemStatus;
  completedByPersonId: number | null;
  completedByName: string | null;
  completedAt: string | null;
};

export type JobChecklist = {
  templateId: number | null;
  templateName: string | null;
  items: JobChecklistItem[];
};
