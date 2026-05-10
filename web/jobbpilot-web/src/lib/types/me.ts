export interface JobSeekerProfileDto {
  id: string;
  displayName: string;
  language: string;
  emailNotifications: boolean;
  weeklySummary: boolean;
  /** ISO 8601 — DateTimeOffset serialiserad */
  createdAt: string;
}
