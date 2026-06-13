import { defineStore } from "pinia";
import { computed, ref } from "vue";
import { request } from "@/lib/api";
import type { PersonSummary, TimeEntryInput, WeeklyTimesheet } from "@/types/timesheet";
import { isManagerRole } from "@/types/auth";
import { useAuthStore } from "@/stores/auth";
import { plainDateToIso, parsePlainDate, shiftWeek, sundayOfWeek } from "@/utils/week";

function normalizeWeekly(raw: Record<string, unknown>): WeeklyTimesheet {
  const days = (raw.days ?? raw.Days ?? []) as Record<string, unknown>[];
  return {
    personId: Number(raw.personId ?? raw.PersonId ?? 0),
    personName: String(raw.personName ?? raw.PersonName ?? ""),
    weekStartDate: String(raw.weekStartDate ?? raw.WeekStartDate ?? ""),
    timezoneId: String(raw.timezoneId ?? raw.TimezoneId ?? "America/Denver"),
    weekTotalWorkMinutes: Number(raw.weekTotalWorkMinutes ?? raw.WeekTotalWorkMinutes ?? 0),
    weekTotalBreakMinutes: Number(raw.weekTotalBreakMinutes ?? raw.WeekTotalBreakMinutes ?? 0),
    days: days.map((day) => ({
      date: String(day.date ?? day.Date ?? ""),
      dayLabel: String(day.dayLabel ?? day.DayLabel ?? ""),
      workMinutes: Number(day.workMinutes ?? day.WorkMinutes ?? 0),
      breakMinutes: Number(day.breakMinutes ?? day.BreakMinutes ?? 0),
      sessions: ((day.sessions ?? day.Sessions ?? []) as Record<string, unknown>[]).map((s) => ({
        timeEntryId: Number(s.timeEntryId ?? s.TimeEntryId ?? 0),
        jobId: Number(s.jobId ?? s.JobId ?? 0),
        jobTitle: String(s.jobTitle ?? s.JobTitle ?? ""),
        clockInAt: String(s.clockInAt ?? s.ClockInAt ?? ""),
        clockOutAt: (s.clockOutAt ?? s.ClockOutAt ?? null) as string | null,
        workMinutes: Number(s.workMinutes ?? s.WorkMinutes ?? 0),
        breakMinutes: Number(s.breakMinutes ?? s.BreakMinutes ?? 0),
        inProgress: Boolean(s.inProgress ?? s.InProgress ?? false),
        breaks: ((s.breaks ?? s.Breaks ?? []) as Record<string, unknown>[]).map((b) => ({
          id: Number(b.id ?? b.Id ?? 0),
          breakStartAt: String(b.breakStartAt ?? b.BreakStartAt ?? ""),
          breakEndAt: (b.breakEndAt ?? b.BreakEndAt ?? null) as string | null,
          breakType: String(b.breakType ?? b.BreakType ?? "other"),
          minutes: Number(b.minutes ?? b.Minutes ?? 0),
        })),
      })),
    })),
  };
}

function normalizePerson(raw: Record<string, unknown>): PersonSummary {
  return {
    personId: Number(raw.personId ?? raw.PersonId ?? 0),
    name: String(raw.name ?? raw.Name ?? ""),
    email: (raw.email ?? raw.Email ?? null) as string | null,
    companyRole: String(raw.companyRole ?? raw.CompanyRole ?? ""),
  };
}

export const useTimesheetStore = defineStore("timesheet", () => {
  const auth = useAuthStore();
  const sheet = ref<WeeklyTimesheet | null>(null);
  const people = ref<PersonSummary[]>([]);
  const weekStart = ref(plainDateToIso(sundayOfWeek()));
  const selectedPersonId = ref<number | null>(null);
  const loading = ref(false);
  const error = ref<string | null>(null);

  const canPickPerson = computed(() => isManagerRole(auth.user?.companyRole));

  const isCurrentWeek = computed(
    () => weekStart.value === plainDateToIso(sundayOfWeek())
  );

  const canManageTimesheet = computed(() => {
    if (canPickPerson.value) return true;
    const viewingSelf =
      selectedPersonId.value === auth.user?.personId || selectedPersonId.value === null;
    return viewingSelf && isCurrentWeek.value;
  });

  async function createEntry(input: TimeEntryInput) {
    await request("/time/entries", {
      method: "POST",
      body: JSON.stringify({
        jobId: input.jobId,
        clockInAt: input.clockInAt,
        clockOutAt: input.clockOutAt,
        breakMinutes: input.breakMinutes,
        notes: input.notes ?? null,
        personId: input.personId ?? null,
      }),
    });
    await fetchWeekly();
  }

  async function updateEntry(entryId: number, input: Omit<TimeEntryInput, "personId">) {
    await request(`/time/entries/${entryId}`, {
      method: "PUT",
      body: JSON.stringify({
        jobId: input.jobId,
        clockInAt: input.clockInAt,
        clockOutAt: input.clockOutAt,
        breakMinutes: input.breakMinutes,
        notes: input.notes ?? null,
      }),
    });
    await fetchWeekly();
  }

  async function deleteEntry(entryId: number) {
    await request<void>(`/time/entries/${entryId}`, { method: "DELETE" });
    await fetchWeekly();
  }

  async function fetchPeople() {
    if (!canPickPerson.value) return;
    const raw = await request<Record<string, unknown>[]>("/people");
    people.value = raw.map(normalizePerson);
  }

  async function fetchWeekly() {
    loading.value = true;
    error.value = null;
    try {
      const params = new URLSearchParams({ weekStart: weekStart.value });
      const personId = selectedPersonId.value ?? auth.user?.personId;
      if (personId) params.set("personId", String(personId));
      const raw = await request<Record<string, unknown>>(`/time/weekly?${params.toString()}`);
      sheet.value = normalizeWeekly(raw);
    } catch (err) {
      error.value = err instanceof Error ? err.message : "Unable to load timesheet.";
      sheet.value = null;
    } finally {
      loading.value = false;
    }
  }

  function goToPreviousWeek() {
    weekStart.value = plainDateToIso(shiftWeek(parsePlainDate(weekStart.value), -1));
  }

  function goToNextWeek() {
    weekStart.value = plainDateToIso(shiftWeek(parsePlainDate(weekStart.value), 1));
  }

  function resetToCurrentWeek() {
    weekStart.value = plainDateToIso(sundayOfWeek());
  }

  return {
    sheet,
    people,
    weekStart,
    selectedPersonId,
    loading,
    error,
    canPickPerson,
    canManageTimesheet,
    isCurrentWeek,
    fetchPeople,
    fetchWeekly,
    createEntry,
    updateEntry,
    deleteEntry,
    goToPreviousWeek,
    goToNextWeek,
    resetToCurrentWeek,
  };
});
