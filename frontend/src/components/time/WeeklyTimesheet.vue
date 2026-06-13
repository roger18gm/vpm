<script setup lang="ts">
import { onMounted, watch } from "vue";
import VpButton from "@/components/ui/VpButton.vue";
import VpSelect from "@/components/ui/VpSelect.vue";
import TimesheetDay from "@/components/time/TimesheetDay.vue";
import { useTimesheetStore } from "@/stores/timesheet";
import { useAuthStore } from "@/stores/auth";
import { formatMinutes } from "@/utils/job";
import { weekRangeLabel } from "@/utils/week";

const timesheet = useTimesheetStore();
const auth = useAuthStore();

onMounted(async () => {
  if (timesheet.selectedPersonId === null && auth.user) {
    timesheet.selectedPersonId = auth.user.personId;
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

async function onPersonChange(value: string) {
  timesheet.selectedPersonId = Number(value);
}
</script>

<template>
  <div>
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
        />
      </div>
    </template>
  </div>
</template>
