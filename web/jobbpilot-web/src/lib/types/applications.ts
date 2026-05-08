export type ApplicationStatus =
  | "Draft"
  | "Submitted"
  | "Acknowledged"
  | "InterviewScheduled"
  | "Interviewing"
  | "OfferReceived"
  | "Accepted"
  | "Rejected"
  | "Withdrawn"
  | "Ghosted";

export type FollowUpChannel = "Email" | "LinkedIn" | "Phone" | "Other";

export type FollowUpOutcome = "Pending" | "Positive" | "Negative" | "Neutral";

export interface ApplicationDto {
  id: string;
  jobSeekerId: string;
  jobAdId: string | null;
  status: ApplicationStatus;
  createdAt: string;
  updatedAt: string;
}

export interface FollowUpDto {
  id: string;
  channel: FollowUpChannel;
  scheduledAt: string;
  note: string | null;
  outcome: FollowUpOutcome;
  outcomeAt: string | null;
  createdAt: string;
}

export interface NoteDto {
  id: string;
  content: string | null;
  createdAt: string;
}

export interface ApplicationDetailDto extends ApplicationDto {
  coverLetter: string | null;
  followUps: FollowUpDto[];
  notes: NoteDto[];
}

export interface PipelineGroupDto {
  status: ApplicationStatus;
  count: number;
  applications: ApplicationDto[];
}

export interface GetApplicationsResult {
  items: ApplicationDto[];
  totalCount: number;
  page: number;
  pageSize: number;
}
