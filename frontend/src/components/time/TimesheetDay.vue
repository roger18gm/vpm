<script setup lang="ts">
import { ref } from "vue";
import type { WeeklyTimesheetDay } from "@/types/timesheet";
import { formatMinutes } from "@/utils/job";
import { formatBreakType, formatClockRange } from "@/utils/time";

defineProps<{
  day: WeeklyTimesheetDay;
  timezoneId: string;
}>();

const expanded = ref(false);
</script>

<template>
  <div class="border border-border rounded-lg overflow-hidden">
    <button
      type="button"
      class="w-full flex items-center justify-between px-4 py-3 text-left bg-surface"
      @click="expanded = !expanded"
    >
      <span class="font-semibold text-sm">{{ day.dayLabel }}</span>
      <span class="text-sm text-muted">
        {{ formatMinutes(day.workMinutes) }} hrs work
        <span v-if="day.breakMinutes"> · {{ formatMinutes(day.breakMinutes) }} break</span>
      </span>
    </button>

    <div v-if="expanded" class="px-4 pb-4 border-t border-border bg-surface">
      <template v-if="day.sessions.length">
        <div v-for="session in day.sessions" :key="session.timeEntryId" class="mt-3">
          <div class="flex justify-between gap-2 text-sm">
            <div>
              <p class="font-semibold">{{ session.jobTitle }}</p>
              <p class="text-xs text-muted">
                {{ formatClockRange(session.clockInAt, session.clockOutAt, timezoneId) }}
              </p>
            </div>
            <div class="text-right shrink-0">
              <p class="font-semibold">{{ formatMinutes(session.workMinutes) }} hrs</p>
              <p v-if="session.inProgress" class="text-xs text-primary">In progress</p>
            </div>
          </div>
          <ul v-if="session.breaks.length" class="mt-2 ml-2 space-y-1">
            <li
              v-for="brk in session.breaks"
              :key="brk.id"
              class="text-xs text-muted"
            >
              {{ formatClockRange(brk.breakStartAt, brk.breakEndAt, timezoneId) }}
              · {{ formatBreakType(brk.breakType) }}
              ({{ formatMinutes(brk.minutes) }} hrs)
            </li>
          </ul>
        </div>
      </template>
      <p v-else class="text-sm text-muted mt-3">No sessions.</p>
    </div>
  </div>
</template>
