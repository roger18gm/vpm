export function formatClockRange(
  startIso: string,
  endIso: string | null,
  timezoneId: string
): string {
  const opts: Intl.DateTimeFormatOptions = {
    hour: "numeric",
    minute: "2-digit",
    timeZone: timezoneId,
  };
  const start = new Date(startIso).toLocaleTimeString("en-US", opts);
  if (!endIso) return `${start} – …`;
  const end = new Date(endIso).toLocaleTimeString("en-US", opts);
  return `${start} – ${end}`;
}

export function formatBreakType(type: string): string {
  if (type === "lunch") return "Lunch";
  if (type === "rest") return "Rest";
  return "Break";
}
