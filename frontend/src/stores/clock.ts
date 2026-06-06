import { defineStore } from "pinia";
import { computed, ref } from "vue";
import { request } from "@/lib/api";

type ActiveClock = {
  timeEntryId: number;
  jobId: number;
  jobTitle: string;
  clockInAt: string;
  onBreak: boolean;
  breakStartedAt: string | null;
};

function normalizeActive(raw: Record<string, unknown> | null): ActiveClock | null {
  if (!raw) return null;
  const jobId = Number(raw.jobId ?? raw.JobId ?? 0);
  if (!jobId) return null;
  return {
    timeEntryId: Number(raw.timeEntryId ?? raw.TimeEntryId ?? 0),
    jobId,
    jobTitle: String(raw.jobTitle ?? raw.JobTitle ?? ""),
    clockInAt: String(raw.clockInAt ?? raw.ClockInAt ?? new Date().toISOString()),
    onBreak: Boolean(raw.onBreak ?? raw.OnBreak ?? false),
    breakStartedAt: (raw.breakStartedAt ?? raw.BreakStartedAt ?? null) as string | null,
  };
}

export type ClockOutSummary = {
  workMinutes: number;
  breakMinutes: number;
};

export const useClockStore = defineStore("clock", () => {
  const active = ref<ActiveClock | null>(null);
  const now = ref(Date.now());
  let timer: ReturnType<typeof setInterval> | null = null;

  function startTicker() {
    if (timer) return;
    timer = setInterval(() => {
      now.value = Date.now();
    }, 1000);
  }

  function stopTicker() {
    if (timer) {
      clearInterval(timer);
      timer = null;
    }
  }

  const isClockedIn = computed(() => active.value !== null);

  const elapsedSeconds = computed(() => {
    if (!active.value) return 0;
    const start = new Date(active.value.clockInAt).getTime();
    return Math.max(0, Math.floor((now.value - start) / 1000));
  });

  const elapsedDisplay = computed(() => {
    const total = elapsedSeconds.value;
    const h = Math.floor(total / 3600);
    const m = Math.floor((total % 3600) / 60);
    const s = total % 60;
    return [h, m, s].map((n) => String(n).padStart(2, "0")).join(":");
  });

  async function hydrateFromServer() {
    const raw = await request<Record<string, unknown> | null>("/time/active");
    active.value = normalizeActive(raw);
    if (active.value) {
      startTicker();
    } else {
      stopTicker();
    }
  }

  async function clockIn(jobId: number, jobTitle: string) {
    if (active.value) return;
    const raw = await request<Record<string, unknown>>("/time/clock-in", {
      method: "POST",
      body: JSON.stringify({ jobId }),
    });
    const hydrated = normalizeActive(raw);
    if (hydrated) {
      active.value = hydrated;
    } else {
      active.value = {
        timeEntryId: 0,
        jobId,
        jobTitle,
        clockInAt: new Date().toISOString(),
        onBreak: false,
        breakStartedAt: null,
      };
    }
    startTicker();
  }

  async function startBreak() {
    if (!active.value || active.value.onBreak) return;
    await request<void>("/time/break/start", { method: "POST" });
    active.value = { ...active.value, onBreak: true, breakStartedAt: new Date().toISOString() };
  }

  async function endBreak() {
    if (!active.value || !active.value.onBreak) return;
    await request<void>("/time/break/end", { method: "POST" });
    active.value = { ...active.value, onBreak: false, breakStartedAt: null };
  }

  async function clockOut(): Promise<ClockOutSummary> {
    const raw = await request<Record<string, unknown>>("/time/clock-out", {
      method: "POST",
      body: JSON.stringify({}),
    });
    active.value = null;
    stopTicker();
    return {
      workMinutes: Number(raw.workMinutes ?? raw.WorkMinutes ?? 0),
      breakMinutes: Number(raw.breakMinutes ?? raw.BreakMinutes ?? 0),
    };
  }

  return {
    active,
    isClockedIn,
    elapsedDisplay,
    hydrateFromServer,
    clockIn,
    startBreak,
    endBreak,
    clockOut,
  };
});
