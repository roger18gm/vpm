<script setup lang="ts">
import { onMounted, ref } from "vue";
import ConfirmDialog from "@/components/ui/ConfirmDialog.vue";
import VpButton from "@/components/ui/VpButton.vue";
import { useChecklistTemplatesStore } from "@/stores/checklistTemplates";
import { useToastStore } from "@/stores/toast";
import type { ChecklistTemplate, ChecklistTemplateItem } from "@/types/checklist";

const store = useChecklistTemplatesStore();
const toast = useToastStore();
const templates = ref<ChecklistTemplate[]>([]);
const newName = ref("");
const itemTitles = ref<Record<number, string>>({});
const error = ref<string | null>(null);
const busy = ref(false);
const pendingDelete = ref<{ kind: "template" | "item"; template: ChecklistTemplate; item?: ChecklistTemplateItem } | null>(null);

onMounted(async () => {
  try {
    templates.value = await store.fetchTemplates();
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Unable to load checklists.";
  }
});

async function addTemplate() {
  const name = newName.value.trim();
  if (!name || busy.value) return;
  busy.value = true;
  error.value = null;
  try {
    await store.createTemplate(name);
    templates.value = store.templates;
    newName.value = "";
    toast.push("Template added");
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Unable to add template.";
  } finally {
    busy.value = false;
  }
}

async function renameTemplate(template: ChecklistTemplate, event: Event) {
  const name = (event.target as HTMLInputElement).value.trim();
  if (!name || name === template.name) return;
  error.value = null;
  try {
    await store.updateTemplate(template.id, { name });
    templates.value = store.templates;
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Unable to rename template.";
  }
}

async function makeDefault(template: ChecklistTemplate) {
  error.value = null;
  try {
    await store.updateTemplate(template.id, { isDefault: true });
    templates.value = store.templates;
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Unable to set the default.";
  }
}

async function addItem(template: ChecklistTemplate) {
  const title = (itemTitles.value[template.id] ?? "").trim();
  if (!title) return;
  error.value = null;
  try {
    await store.createItem(template.id, title, true);
    templates.value = store.templates;
    itemTitles.value[template.id] = "";
    toast.push("Item added");
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Unable to add item.";
  }
}

async function renameItem(template: ChecklistTemplate, item: ChecklistTemplateItem, event: Event) {
  const title = (event.target as HTMLInputElement).value.trim();
  if (!title || title === item.title) return;
  error.value = null;
  try {
    await store.updateItem(template.id, item.id, { title });
    templates.value = store.templates;
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Unable to rename item.";
  }
}

async function toggleRequired(template: ChecklistTemplate, item: ChecklistTemplateItem, event: Event) {
  const isRequired = (event.target as HTMLInputElement).checked;
  error.value = null;
  try {
    await store.updateItem(template.id, item.id, { isRequired });
    templates.value = store.templates;
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Unable to update item.";
  }
}

async function confirmDelete() {
  if (!pendingDelete.value) return;
  error.value = null;
  try {
    if (pendingDelete.value.kind === "template") {
      await store.deleteTemplate(pendingDelete.value.template.id);
      toast.push("Template removed");
    } else if (pendingDelete.value.item) {
      await store.deleteItem(pendingDelete.value.template.id, pendingDelete.value.item.id);
      toast.push("Item removed");
    }
    templates.value = store.templates;
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Unable to delete.";
  } finally {
    pendingDelete.value = null;
  }
}
</script>

<template>
  <h1 class="text-xl font-bold mb-4">Checklists</h1>
  <p v-if="error" class="text-sm text-error mb-3">{{ error }}</p>
  <form class="flex gap-2 mb-6" @submit.prevent="addTemplate">
    <label class="flex-1">
      <span class="sr-only">Template name</span>
      <input
        v-model="newName"
        type="text"
        maxlength="80"
        class="w-full border border-border rounded-md px-3 py-2.5 text-sm min-h-[44px] bg-surface"
        placeholder="e.g. Exterior prep"
        aria-label="Template name"
      />
    </label>
    <VpButton type="submit" :disabled="busy || !newName.trim()">Add template</VpButton>
  </form>

  <section v-for="template in templates" :key="template.id" class="bg-surface border border-border rounded-lg p-4 mb-4">
    <div class="flex flex-wrap items-center gap-2 mb-3">
      <input
        :value="template.name"
        class="flex-1 border border-border rounded-md px-3 py-2 text-sm min-h-[44px]"
        :aria-label="`Name for ${template.name}`"
        @change="renameTemplate(template, $event)"
      />
      <span v-if="template.isDefault" class="text-xs font-semibold text-primary">Default</span>
      <VpButton v-else variant="secondary" @click="makeDefault(template)">Set as default</VpButton>
      <VpButton variant="ghost" @click="pendingDelete = { kind: 'template', template }">Delete</VpButton>
    </div>
    <ul class="space-y-2 mb-3">
      <li v-for="item in template.items" :key="item.id" class="flex flex-wrap items-center gap-2">
        <input
          :value="item.title"
          class="flex-1 border border-border rounded-md px-3 py-2 text-sm min-h-[44px]"
          :aria-label="`Title for ${item.title}`"
          @change="renameItem(template, item, $event)"
        />
        <label class="text-sm flex items-center gap-1">
          <input type="checkbox" :checked="item.isRequired" @change="toggleRequired(template, item, $event)" />
          Required
        </label>
        <VpButton variant="ghost" @click="pendingDelete = { kind: 'item', template, item }">Delete</VpButton>
      </li>
    </ul>
    <form class="flex gap-2" @submit.prevent="addItem(template)">
      <input
        v-model="itemTitles[template.id]"
        type="text"
        maxlength="80"
        class="flex-1 border border-border rounded-md px-3 py-2 text-sm min-h-[44px]"
        :aria-label="`New item for ${template.name}`"
        placeholder="Add an item"
      />
      <VpButton type="submit" :disabled="!(itemTitles[template.id] ?? '').trim()">Add item</VpButton>
    </form>
  </section>

  <ConfirmDialog
    :open="pendingDelete !== null"
    :title="pendingDelete?.kind === 'template' ? 'Delete template?' : 'Delete item?'"
    :message="pendingDelete?.kind === 'template'
      ? `Remove ${pendingDelete.template.name}.`
      : `Remove ${pendingDelete?.item?.title ?? 'this item'}.`"
    confirm-label="Delete"
    @confirm="confirmDelete"
    @cancel="pendingDelete = null"
  />
</template>
