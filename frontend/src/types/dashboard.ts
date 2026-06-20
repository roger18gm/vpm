export type ClockedInWorker = {
  personId: number;
  name: string;
  jobId: number;
  jobTitle: string;
  clockInAt: string;
  onBreak: boolean;
};

export type DashboardSummary = {
  hoursThisWeekMinutes: number;
  completedThisWeekCount: number;
  clockedInWorkers: ClockedInWorker[];
};
