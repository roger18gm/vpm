<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { capturePostHogEvent } from "@/lib/posthog";
import ConfirmDialog from "@/components/ui/ConfirmDialog.vue";
import VpButton from "@/components/ui/VpButton.vue";
import VpSelect from "@/components/ui/VpSelect.vue";
import TimeEntryFormModal from "@/components/time/TimeEntryFormModal.vue";
import TimesheetDay from "@/components/time/TimesheetDay.vue";
import { useClockStore } from "@/stores/clock";
import { useTimesheetStore } from "@/stores/timesheet";
import { useJobsStore } from "@/stores/jobs";
import { useAuthStore } from "@/stores/auth";
import { isManagerRole } from "@/types/auth";
import type { WeeklyTimesheetSession } from "@/types/timesheet";
import { formatMinutes } from "@/utils/job";
import { weekRangeLabel } from "@/utils/week";

const timesheet = useTimesheetStore();
const auth = useAuthStore();
const jobsStore = useJobsStore();
const clock = useClockStore();

const formOpen = ref(false);
const formMode = ref<"create" | "edit">("create");
const editingSession = ref<WeeklyTimesheetSession | null>(null);
const defaultDate = ref("");
const formBusy = ref(false);
const formError = ref<string | null>(null);

const deleteOpen = ref(false);
const deletingSession = ref<WeeklyTimesheetSession | null>(null);
const deleteBusy = ref(false);
const gustoConfirmOpen = ref(false);

const isManager = computed(() => isManagerRole(auth.user?.companyRole));

onMounted(async () => {
  if (timesheet.selectedPersonId === null && auth.user) {
    timesheet.selectedPersonId = auth.user.personId;
  }
  if (!jobsStore.jobs.length) {
    await jobsStore.fetchJobs();
  }
  if (timesheet.canPickPerson) {
    await timesheet.fetchPeople();
  }
  await timesheet.fetchWeekly();
});

watch(
  () => [timesheet.weekStart, timesheet.selectedPersonId] as const,
  () => {
    void timesheet.fetchWeekly();
  }
);

const assignableJobs = computed(() =>
  jobsStore.jobs.filter((job) => job.status !== "cancelled")
);

function canEditSession(session: WeeklyTimesheetSession) {
  if (!timesheet.canManageTimesheet) return false;
  if (session.inProgress && !isManager.value) return false;
  return true;
}

function canDeleteSession(session: WeeklyTimesheetSession) {
  return canEditSession(session);
}

async function onPersonChange(value: string) {
  timesheet.clearGustoSyncFeedback();
  timesheet.selectedPersonId = Number(value);
}

async function confirmGustoSend() {
  await timesheet.sendWeekToGusto();
  gustoConfirmOpen.value = false;
}

function openCreate(dayDate: string) {
  formMode.value = "create";
  editingSession.value = null;
  defaultDate.value = dayDate;
  formError.value = null;
  formOpen.value = true;
}

function openEdit(session: WeeklyTimesheetSession) {
  formMode.value = "edit";
  editingSession.value = session;
  defaultDate.value = "";
  formError.value = null;
  formOpen.value = true;
}

function openDelete(session: WeeklyTimesheetSession) {
  deletingSession.value = session;
  deleteOpen.value = true;
}

async function handleSaved(payload: {
  jobId: number;
  clockInAt: string;
  clockOutAt: string;
  breakMinutes: number;
  breaks: { breakStartAt: string; breakEndAt: string; breakType: "lunch" | "rest" | "other" }[];
  notes: string | null;
}) {
  formBusy.value = true;
  formError.value = null;
  try {
    if (formMode.value === "create") {
      await timesheet.createEntry({
        ...payload,
        personId: timesheet.selectedPersonId ?? auth.user?.personId ?? null,
      });
      capturePostHogEvent("time_entry_created", { break_count: payload.breaks.length });
    } else if (editingSession.value) {
      await timesheet.updateEntry(editingSession.value.timeEntryId, payload);
      capturePostHogEvent("time_entry_updated", { break_count: payload.breaks.length });
    }
    formOpen.value = false;
    await clock.hydrateFromServer();
  } catch (err) {
    formError.value = err instanceof Error ? err.message : "Unable to save time entry.";
  } finally {
    formBusy.value = false;
  }
}

async function confirmDelete() {
  if (!deletingSession.value) return;
  deleteBusy.value = true;
  try {
    await timesheet.deleteEntry(deletingSession.value.timeEntryId);
    capturePostHogEvent("time_entry_deleted");
    deleteOpen.value = false;
    deletingSession.value = null;
    await clock.hydrateFromServer();
  } finally {
    deleteBusy.value = false;
  }
}
</script>

<template>
  <div>
    <TimeEntryFormModal
      :open="formOpen"
      :mode="formMode"
      :busy="formBusy"
      :timezone-id="timesheet.sheet?.timezoneId ?? 'America/Denver'"
      :jobs="assignableJobs"
      :default-date="defaultDate"
      :session="editingSession"
      @close="formOpen = false"
      @saved="handleSaved"
    />

    <ConfirmDialog
      :open="deleteOpen"
      title="Delete time entry?"
      message="This removes the session from the timesheet."
      confirm-label="Delete"
      :busy="deleteBusy"
      @cancel="deleteOpen = false"
      @confirm="confirmDelete"
    />

    <ConfirmDialog
      :open="gustoConfirmOpen"
      title="Send week to Gusto"
      :message="`Send ${timesheet.sheet?.personName ?? 'this worker'}'s closed shifts for this week to the Gusto demo. Shifts already sent stay as they are.`"
      confirm-label="Send week to Gusto"
      :busy="timesheet.gustoSyncing"
      @cancel="gustoConfirmOpen = false"
      @confirm="confirmGustoSend"
    />

    <div v-if="timesheet.canPickPerson" class="mb-4">
      <VpSelect
        :model-value="String(timesheet.selectedPersonId ?? '')"
        label="Worker"
        @update:model-value="onPersonChange"
      >
        <option v-for="person in timesheet.people" :key="person.personId" :value="String(person.personId)">
          {{ person.name }}
        </option>
      </VpSelect>
    </div>

    <div class="flex items-center justify-between mb-4 gap-2">
      <VpButton variant="secondary" @click="timesheet.goToPreviousWeek">←</VpButton>
      <div class="text-center flex-1">
        <p class="text-sm font-semibold">
          {{ timesheet.sheet ? weekRangeLabel(timesheet.sheet.weekStartDate) : "This week" }}
        </p>
        <p v-if="timesheet.sheet" class="text-xs text-muted">
          {{ timesheet.sheet.personName }} · {{ formatMinutes(timesheet.sheet.weekTotalWorkMinutes) }} hrs total
        </p>
      </div>
      <VpButton variant="secondary" @click="timesheet.goToNextWeek">→</VpButton>
    </div>

    <div v-if="isManager" class="mb-4">
      <VpButton
        variant="secondary"
        class="w-full"
        :disabled="timesheet.gustoSyncing"
        @click="gustoConfirmOpen = true"
      >
        Send week to Gusto
      </VpButton>
      <p v-if="timesheet.gustoSyncError" class="text-sm text-error text-center mt-2">
        {{ timesheet.gustoSyncError }}
      </p>
      <div v-if="timesheet.gustoSyncResult" class="text-sm text-center mt-2">
        <p>
          Sent {{ timesheet.gustoSyncResult.sent }}.
          Already synced {{ timesheet.gustoSyncResult.alreadySynced }}.
          Skipped open {{ timesheet.gustoSyncResult.skippedOpen }}.
          Skipped zero {{ timesheet.gustoSyncResult.skippedZero }}.
          Failed {{ timesheet.gustoSyncResult.failed.length }}.
        </p>
        <p
          v-for="failure in timesheet.gustoSyncResult.failed"
          :key="failure.timeEntryId"
          class="text-error"
        >
          Shift {{ failure.timeEntryId }}: {{ failure.message }}
        </p>
      </div>
    </div>

    <p v-if="!timesheet.canManageTimesheet && !timesheet.isCurrentWeek" class="text-xs text-muted text-center mb-4">
      Past weeks are read-only for your role.
    </p>
    <p v-if="formError" class="text-sm text-error text-center mb-4">{{ formError }}</p>

    <p v-if="timesheet.loading" class="text-sm text-muted text-center py-6">Loading…</p>
    <p v-else-if="timesheet.error" class="text-sm text-error text-center py-6">{{ timesheet.error }}</p>
    <template v-else-if="timesheet.sheet">
      <p
        v-if="timesheet.sheet.weekTotalWorkMinutes === 0"
        class="text-sm text-muted text-center mb-4"
      >
        No time logged this week.
      </p>
      <div class="space-y-2">
        <TimesheetDay
          v-for="day in timesheet.sheet.days"
          :key="day.date"
          :day="day"
          :timezone-id="timesheet.sheet.timezoneId"
          :can-manage="timesheet.canManageTimesheet"
          :show-gusto-status="isManager"
          :can-edit-session="canEditSession"
          :can-delete-session="canDeleteSession"
          @add="openCreate(day.date)"
          @edit="openEdit"
          @delete="openDelete"
        />
      </div>
    </template>
  </div>
</template>
