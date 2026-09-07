// Matches thinkschool_Shagun_Yadav/day-18/piece1/QuotesApi/BackgroundJobs/JobRecord.cs and
// JobStatus.cs exactly - verified live against the running API. System.Text.Json serializes the
// enum as its underlying int (no JsonStringEnumConverter configured), so `status` here is a
// number, not the enum member name.
export type JobStatusCode = 0 | 1 | 2 | 3 | 4;

export const JOB_STATUS_LABEL: Record<JobStatusCode, string> = {
  0: 'Queued',
  1: 'Running',
  2: 'Completed',
  3: 'Failed',
  4: 'Cancelled',
};

export interface JobRecord {
  id: string;
  type: string;
  input: string;
  status: JobStatusCode;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  result: string | null;
  error: string | null;
}
