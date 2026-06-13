import { Temporal } from "@js-temporal/polyfill";

export function sundayOfWeek(date = Temporal.Now.plainDateISO()): Temporal.PlainDate {
  const daysFromSunday = date.dayOfWeek % 7;
  return date.subtract({ days: daysFromSunday });
}

export function plainDateToIso(date: Temporal.PlainDate): string {
  return date.toString();
}

export function parsePlainDate(iso: string): Temporal.PlainDate {
  return Temporal.PlainDate.from(iso);
}

export function shiftWeek(date: Temporal.PlainDate, weeks: number): Temporal.PlainDate {
  return date.add({ weeks });
}

export function weekRangeLabel(weekStartIso: string): string {
  const start = parsePlainDate(weekStartIso);
  const end = start.add({ days: 6 });
  const fmt = (d: Temporal.PlainDate) =>
    d.toLocaleString("en-US", { month: "short", day: "numeric" });
  return `${fmt(start)} – ${fmt(end)}`;
}
