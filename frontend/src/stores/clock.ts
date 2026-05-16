import { defineStore } from "pinia";
import { computed, ref } from "vue";

const STORAGE_KEY = "visionpaint_active_clock";

type ActiveClock = {
  jobId: number;
  jobTitle: string;
  clockInAt: string;
  onBreak: boolean;
  breakStartedAt: string | null;
};

function loadClock(): ActiveClock | null {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY);
    return raw ? (JSON.parse(raw) as ActiveClock) : null;
  } catch {
    return null;
  }
}

function saveClock(clock: ActiveClock | null) {
  if (clock) {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(clock));
  } else {
    sessionStorage.removeItem(STORAGE_KEY);
  }
}

export const useClockStore = defineStore("clock", () => {
  const active = ref<ActiveClock | null>(loadClock());
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

  if (active.value) startTicker();

  function clockIn(jobId: number, jobTitle: string) {
    if (active.value) return;
    active.value = {
      jobId,
      jobTitle,
      clockInAt: new Date().toISOString(),
      onBreak: false,
      breakStartedAt: null,
    };
    saveClock(active.value);
    startTicker();
  }

  function startBreak() {
    if (!active.value || active.value.onBreak) return;
    active.value = { ...active.value, onBreak: true, breakStartedAt: new Date().toISOString() };
    saveClock(active.value);
  }

  function endBreak() {
    if (!active.value || !active.value.onBreak) return;
    active.value = { ...active.value, onBreak: false, breakStartedAt: null };
    saveClock(active.value);
  }

  function clockOut() {
    active.value = null;
    saveClock(null);
    stopTicker();
  }

  return {
    active,
    isClockedIn,
    elapsedDisplay,
    clockIn,
    startBreak,
    endBreak,
    clockOut,
  };
});
