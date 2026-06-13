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
  breaks: TimeBreakWindow[];
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
