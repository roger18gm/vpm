<script setup lang="ts">
import { computed, ref, watch } from "vue";
import VpButton from "@/components/ui/VpButton.vue";
import VpCard from "@/components/ui/VpCard.vue";
import VpInput from "@/components/ui/VpInput.vue";
import VpSelect from "@/components/ui/VpSelect.vue";
import type { Job } from "@/types/job";
import type { WeeklyTimesheetSession } from "@/types/timesheet";
import { isoToLocalDate, isoToLocalTime, localDateTimeToIso } from "@/utils/time";

const props = defineProps<{
  open: boolean;
  mode: "create" | "edit";
  busy?: boolean;
  timezoneId: string;
  jobs: Job[];
  defaultDate?: string;
  session?: WeeklyTimesheetSession | null;
}>();

const emit = defineEmits<{
  close: [];
  saved: [
    payload: {
      jobId: number;
      clockInAt: string;
      clockOutAt: string;
      breakMinutes: number;
      notes: string | null;
    },
  ];
}>();

const jobId = ref("");
const date = ref("");
const clockInTime = ref("08:00");
const clockOutTime = ref("17:00");
const breakMinutes = ref("0");
const notes = ref("");
const error = ref<string | null>(null);

const title = computed(() => (props.mode === "create" ? "Add time" : "Edit time"));

watch(
  () => props.open,
  (isOpen) => {
    if (!isOpen) return;
    error.value = null;
    if (props.mode === "edit" && props.session) {
      jobId.value = String(props.session.jobId);
      date.value = isoToLocalDate(props.session.clockInAt, props.timezoneId);
      clockInTime.value = isoToLocalTime(props.session.clockInAt, props.timezoneId);
      clockOutTime.value = props.session.clockOutAt
        ? isoToLocalTime(props.session.clockOutAt, props.timezoneId)
        : "17:00";
      breakMinutes.value = String(props.session.breakMinutes);
      notes.value = "";
    } else {
      jobId.value = props.jobs[0] ? String(props.jobs[0].id) : "";
      date.value = props.defaultDate ?? "";
      clockInTime.value = "08:00";
      clockOutTime.value = "17:00";
      breakMinutes.value = "0";
      notes.value = "";
    }
  }
);

function submit() {
  error.value = null;
  if (!jobId.value || !date.value || !clockInTime.value || !clockOutTime.value) {
    error.value = "Job, date, and times are required.";
    return;
  }

  const breakValue = Number(breakMinutes.value);
  if (Number.isNaN(breakValue) || breakValue < 0) {
    error.value = "Break minutes must be zero or greater.";
    return;
  }

  const clockInAt = localDateTimeToIso(date.value, clockInTime.value, props.timezoneId);
  const clockOutAt = localDateTimeToIso(date.value, clockOutTime.value, props.timezoneId);
  if (new Date(clockOutAt) <= new Date(clockInAt)) {
    error.value = "Clock out must be after clock in.";
    return;
  }

  emit("saved", {
    jobId: Number(jobId.value),
    clockInAt,
    clockOutAt,
    breakMinutes: breakValue,
    notes: notes.value.trim() || null,
  });
}
</script>

<template>
  <div
    v-if="open"
    class="fixed inset-0 z-40 flex items-center justify-center bg-black/40 p-4"
    @click.self="emit('close')"
  >
    <VpCard class="w-full max-w-md space-y-3">
      <template #title>{{ title }}</template>

      <VpSelect v-model="jobId" label="Job">
        <option v-for="job in jobs" :key="job.id" :value="String(job.id)">
          {{ job.title }}
        </option>
      </VpSelect>

      <VpInput v-model="date" label="Date" type="date" />

      <div class="grid grid-cols-2 gap-3">
        <VpInput v-model="clockInTime" label="Clock in" type="time" />
        <VpInput v-model="clockOutTime" label="Clock out" type="time" />
      </div>

      <VpInput v-model="breakMinutes" label="Break (minutes)" type="number" />

      <VpInput v-model="notes" label="Notes (optional)" />

      <p v-if="error" class="text-sm text-error">{{ error }}</p>

      <div class="grid grid-cols-2 gap-3 pt-1">
        <VpButton variant="secondary" :disabled="busy" @click="emit('close')">Cancel</VpButton>
        <VpButton :disabled="busy || !jobs.length" @click="submit">Save</VpButton>
      </div>
    </VpCard>
  </div>
</template>
