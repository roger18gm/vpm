export type TimeBreakWindow = {
  id: number;
  breakStartAt: string;
  breakEndAt: string | null;
  breakType: string;
  minutes: number;
};

export type WeeklyTimesheetSession = {
  timeEntryId: number;
  jobId: number;
  jobTitle: string;
  clockInAt: string;
  clockOutAt: string | null;
  workMinutes: number;
  breakMinutes: number;
  inProgress: boolean;
  gustoSyncStatus: "pending" | "synced" | "failed" | null;
  breaks: TimeBreakWindow[];
};

export type GustoWeekSyncResult = {
  sent: number;
  alreadySynced: number;
  skippedOpen: number;
  skippedZero: number;
  failed: { timeEntryId: number; message: string }[];
};

export type WeeklyTimesheetDay = {
  date: string;
  dayLabel: string;
  workMinutes: number;
  breakMinutes: number;
  sessions: WeeklyTimesheetSession[];
};

export type WeeklyTimesheet = {
  personId: number;
  personName: string;
  weekStartDate: string;
  timezoneId: string;
  weekTotalWorkMinutes: number;
  weekTotalBreakMinutes: number;
  days: WeeklyTimesheetDay[];
};

export type PersonSummary = {
  personId: number;
  name: string;
  email: string | null;
  companyRole: string;
};

export type TimeBreakInput = {
  breakStartAt: string;
  breakEndAt: string;
  breakType: "lunch" | "rest" | "other";
};

export type TimeEntryInput = {
  jobId: number;
  clockInAt: string;
  clockOutAt: string;
  breakMinutes: number;
  breaks: TimeBreakInput[];
  notes?: string | null;
  personId?: number | null;
};
