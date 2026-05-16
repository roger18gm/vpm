<script setup lang="ts">
import { reactive } from "vue";
import VpInput from "@/components/ui/VpInput.vue";
import VpSelect from "@/components/ui/VpSelect.vue";
import VpButton from "@/components/ui/VpButton.vue";
import type { Job, JobInput } from "@/types/job";
import { dateInputValue } from "@/utils/job";

const props = defineProps<{
  initial?: Job | null;
  busy?: boolean;
}>();

const emit = defineEmits<{
  submit: [payload: JobInput];
  cancel: [];
}>();

const form = reactive({
  title: props.initial?.title ?? "",
  description: props.initial?.description ?? "",
  priority: props.initial?.priority ?? "normal",
  status: props.initial?.status ?? "scheduled",
  addressLine1: props.initial?.addressLine1 ?? "",
  city: props.initial?.city ?? "",
  stateRegion: props.initial?.stateRegion ?? "",
  postalCode: props.initial?.postalCode ?? "",
  scheduledStart: dateInputValue(props.initial?.scheduledStartAt),
  due: dateInputValue(props.initial?.dueAt),
});

function toIso(date: string) {
  if (!date) return null;
  return new Date(`${date}T12:00:00`).toISOString();
}

function onSubmit() {
  emit("submit", {
    title: form.title.trim(),
    description: form.description || null,
    priority: form.priority,
    status: form.status,
    addressLine1: form.addressLine1 || null,
    city: form.city || null,
    stateRegion: form.stateRegion || null,
    postalCode: form.postalCode || null,
    scheduledStartAt: toIso(form.scheduledStart),
    dueAt: toIso(form.due),
  });
}
</script>

<template>
  <form class="space-y-4" @submit.prevent="onSubmit">
    <fieldset class="bg-surface border border-border rounded-lg p-4 space-y-3">
      <legend class="text-sm font-semibold text-muted px-1">Basics</legend>
      <VpInput v-model="form.title" label="Job title *" required placeholder="e.g. Riverside exterior repaint" />
      <label class="block">
        <span class="text-xs text-muted mb-1 block">Description</span>
        <textarea
          v-model="form.description"
          rows="3"
          class="w-full border border-border rounded-md px-3 py-2 text-sm bg-surface"
          placeholder="Scope, notes for crew…"
        />
      </label>
      <div class="grid grid-cols-2 gap-3">
        <VpSelect v-model="form.priority" label="Priority">
          <option value="low">Low</option>
          <option value="normal">Normal</option>
          <option value="high">High</option>
          <option value="urgent">Urgent</option>
        </VpSelect>
        <VpSelect v-model="form.status" label="Status">
          <option value="scheduled">Scheduled</option>
          <option value="in_progress">In progress</option>
          <option value="completed">Completed</option>
          <option value="cancelled">Cancelled</option>
        </VpSelect>
      </div>
    </fieldset>

    <fieldset class="bg-surface border border-border rounded-lg p-4 space-y-3">
      <legend class="text-sm font-semibold text-muted px-1">Job site</legend>
      <VpInput v-model="form.addressLine1" label="Street address" placeholder="124 Oak St" />
      <div class="grid grid-cols-2 gap-3">
        <VpInput v-model="form.city" label="City" />
        <VpInput v-model="form.stateRegion" label="State" />
      </div>
      <VpInput v-model="form.postalCode" label="ZIP" />
    </fieldset>

    <fieldset class="bg-surface border border-border rounded-lg p-4 space-y-3">
      <legend class="text-sm font-semibold text-muted px-1">Schedule</legend>
      <div class="grid grid-cols-2 gap-3">
        <label class="block">
          <span class="text-xs text-muted mb-1 block">Start date</span>
          <input v-model="form.scheduledStart" type="date" class="w-full border border-border rounded-md px-3 py-2.5 text-sm min-h-[44px] bg-surface" />
        </label>
        <label class="block">
          <span class="text-xs text-muted mb-1 block">Due date</span>
          <input v-model="form.due" type="date" class="w-full border border-border rounded-md px-3 py-2.5 text-sm min-h-[44px] bg-surface" />
        </label>
      </div>
    </fieldset>

    <div class="flex gap-3">
      <VpButton type="submit" block :disabled="busy || !form.title.trim()">
        {{ busy ? "Saving…" : "Save job" }}
      </VpButton>
      <VpButton type="button" variant="secondary" block @click="emit('cancel')">Cancel</VpButton>
    </div>
  </form>
</template>
