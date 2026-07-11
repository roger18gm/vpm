<script setup lang="ts">
import { computed, ref, watch } from "vue";
import VpButton from "@/components/ui/VpButton.vue";
import VpCard from "@/components/ui/VpCard.vue";
import VpInput from "@/components/ui/VpInput.vue";
import VpSelect from "@/components/ui/VpSelect.vue";
import type { Job } from "@/types/job";
import type { TimeBreakInput, WeeklyTimesheetSession } from "@/types/timesheet";
import { formatMinutes } from "@/utils/job";
import { isoToLocalDate, isoToLocalTime, localDateTimeToIso } from "@/utils/time";

type BreakRow = { start: string; end: string; breakType: "lunch" | "rest" | "other" };

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
      breaks: TimeBreakInput[];
      notes: string | null;
    },
  ];
}>();

const jobId = ref("");
const date = ref("");
const clockInTime = ref("08:00");
const clockOutTime = ref("17:00");
const breakRows = ref<BreakRow[]>([]);
const notes = ref("");
const error = ref<string | null>(null);

const title = computed(() => (props.mode === "create" ? "Add time" : "Edit time"));

const previewBreakMinutes = computed(() =>
  breakRows.value.reduce((sum, row) => {
    if (!date.value || !row.start || !row.end) return sum;
    const start = new Date(localDateTimeToIso(date.value, row.start, props.timezoneId)).getTime();
    const end = new Date(localDateTimeToIso(date.value, row.end, props.timezoneId)).getTime();
    if (Number.isNaN(start) || Number.isNaN(end) || end <= start) return sum;
    return sum + Math.round((end - start) / 60000);
  }, 0)
);

const workBreakPreview = computed(() => {
  if (!date.value || !clockInTime.value || !clockOutTime.value) {
    return "Work: — · Break: —";
  }
  const clockInAt = localDateTimeToIso(date.value, clockInTime.value, props.timezoneId);
  const clockOutAt = localDateTimeToIso(date.value, clockOutTime.value, props.timezoneId);
  const span = Math.round(
    (new Date(clockOutAt).getTime() - new Date(clockInAt).getTime()) / 60000
  );
  if (span <= 0) {
    return "Work: — · Break: —";
  }
  const breakMins = previewBreakMinutes.value;
  const workMins = Math.max(0, span - breakMins);
  return `Work: ${formatMinutes(workMins)} hrs · Break: ${formatMinutes(breakMins)} hrs`;
});

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
      breakRows.value = props.session.breaks
        .filter((b) => b.breakEndAt)
        .map((b) => ({
          start: isoToLocalTime(b.breakStartAt, props.timezoneId),
          end: isoToLocalTime(b.breakEndAt!, props.timezoneId),
          breakType: normalizeBreakType(b.breakType),
        }));
      notes.value = "";
    } else {
      jobId.value = props.jobs[0] ? String(props.jobs[0].id) : "";
      date.value = props.defaultDate ?? "";
      clockInTime.value = "08:00";
      clockOutTime.value = "17:00";
      breakRows.value = [];
      notes.value = "";
    }
  }
);

function normalizeBreakType(type: string): BreakRow["breakType"] {
  if (type === "lunch" || type === "rest" || type === "other") return type;
  return "other";
}

function addBreak() {
  breakRows.value.push({ start: "12:00", end: "12:30", breakType: "lunch" });
}

function removeBreak(index: number) {
  breakRows.value.splice(index, 1);
}

function submit() {
  error.value = null;
  if (!jobId.value || !date.value || !clockInTime.value || !clockOutTime.value) {
    error.value = "Job, date, and times are required.";
    return;
  }

  const clockInAt = localDateTimeToIso(date.value, clockInTime.value, props.timezoneId);
  const clockOutAt = localDateTimeToIso(date.value, clockOutTime.value, props.timezoneId);
  const clockInMs = new Date(clockInAt).getTime();
  const clockOutMs = new Date(clockOutAt).getTime();
  if (clockOutMs <= clockInMs) {
    error.value = "Clock out must be after clock in.";
    return;
  }

  const breaks: TimeBreakInput[] = [];
  for (const row of breakRows.value) {
    if (!row.start || !row.end) {
      error.value = "Each break needs a start and end time.";
      return;
    }
    const breakStartAt = localDateTimeToIso(date.value, row.start, props.timezoneId);
    const breakEndAt = localDateTimeToIso(date.value, row.end, props.timezoneId);
    const startMs = new Date(breakStartAt).getTime();
    const endMs = new Date(breakEndAt).getTime();
    if (endMs <= startMs) {
      error.value = "Break end must be after break start.";
      return;
    }
    if (startMs < clockInMs || endMs > clockOutMs) {
      error.value = "Breaks must fall within the shift.";
      return;
    }
    breaks.push({
      breakStartAt,
      breakEndAt,
      breakType: row.breakType,
    });
  }

  const ordered = [...breaks].sort(
    (a, b) => new Date(a.breakStartAt).getTime() - new Date(b.breakStartAt).getTime()
  );
  for (let i = 1; i < ordered.length; i++) {
    if (new Date(ordered[i].breakStartAt).getTime() < new Date(ordered[i - 1].breakEndAt).getTime()) {
      error.value = "Breaks cannot overlap.";
      return;
    }
  }

  const breakMinutes = breaks.reduce((sum, b) => {
    return (
      sum +
      Math.max(
        0,
        Math.round((new Date(b.breakEndAt).getTime() - new Date(b.breakStartAt).getTime()) / 60000)
      )
    );
  }, 0);

  if (breakMinutes >= Math.round((clockOutMs - clockInMs) / 60000)) {
    error.value = "Break time must be less than total shift length.";
    return;
  }

  emit("saved", {
    jobId: Number(jobId.value),
    clockInAt,
    clockOutAt,
    breakMinutes,
    breaks,
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
    <VpCard class="w-full max-w-md space-y-3 max-h-[90vh] overflow-y-auto">
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

      <div class="space-y-2">
        <div class="flex items-center justify-between">
          <p class="text-sm font-medium">Breaks</p>
          <button type="button" class="text-sm text-primary" @click="addBreak">+ Add break</button>
        </div>
        <p class="text-xs text-muted">Add each break separately. Totals update when you save.</p>
        <div
          v-for="(row, i) in breakRows"
          :key="i"
          class="grid grid-cols-[1fr_1fr_1fr_auto] gap-2 items-end"
        >
          <VpInput v-model="row.start" label="Start" type="time" />
          <VpInput v-model="row.end" label="End" type="time" />
          <VpSelect v-model="row.breakType" label="Type">
            <option value="lunch">Lunch</option>
            <option value="rest">Rest</option>
            <option value="other">Other</option>
          </VpSelect>
          <button type="button" class="text-xs text-error pb-2" @click="removeBreak(i)">Remove</button>
        </div>
        <p class="text-xs text-muted">{{ workBreakPreview }}</p>
      </div>

      <VpInput v-model="notes" label="Notes (optional)" />

      <p v-if="error" class="text-sm text-error">{{ error }}</p>

      <div class="grid grid-cols-2 gap-3 pt-1">
        <VpButton variant="secondary" :disabled="busy" @click="emit('close')">Cancel</VpButton>
        <VpButton :disabled="busy || !jobs.length" @click="submit">Save</VpButton>
      </div>
    </VpCard>
  </div>
</template>
