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

export function isoToLocalDate(iso: string, timezoneId: string): string {
  const formatter = new Intl.DateTimeFormat("en-CA", {
    timeZone: timezoneId,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  });
  const parts = formatter.formatToParts(new Date(iso));
  const year = parts.find((p) => p.type === "year")?.value ?? "0000";
  const month = parts.find((p) => p.type === "month")?.value ?? "01";
  const day = parts.find((p) => p.type === "day")?.value ?? "01";
  return `${year}-${month}-${day}`;
}

export function isoToLocalTime(iso: string, timezoneId: string): string {
  const formatter = new Intl.DateTimeFormat("en-GB", {
    timeZone: timezoneId,
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  });
  return formatter.format(new Date(iso));
}

export function localDateTimeToIso(date: string, time: string, timezoneId: string): string {
  const [year, month, day] = date.split("-").map(Number);
  const [hour, minute] = time.split(":").map(Number);
  const utcGuess = Date.UTC(year, month - 1, day, hour, minute, 0);
  const formatter = new Intl.DateTimeFormat("en-US", {
    timeZone: timezoneId,
    hour: "numeric",
    minute: "numeric",
    hour12: false,
  });
  const partsForGuess = formatter.formatToParts(new Date(utcGuess));
  const actualHour = Number(partsForGuess.find((p) => p.type === "hour")?.value ?? hour);
  const actualMinute = Number(partsForGuess.find((p) => p.type === "minute")?.value ?? minute);
  const deltaMinutes = (hour * 60 + minute) - (actualHour * 60 + actualMinute);
  return new Date(utcGuess + deltaMinutes * 60_000).toISOString();
}
