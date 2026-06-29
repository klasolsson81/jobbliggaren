// Re-export från lib/dto/applications.ts. Zod-schemat är single source of
// truth per ADR 0020. Nya konsumenter bör importera från `@/lib/dto/applications`.
export type {
  ApplicationStatus,
  FollowUpChannel,
  FollowUpOutcome,
  JobAdSummaryDto,
  AdSnapshotDto,
  ApplicationDto,
  FollowUpDto,
  NoteDto,
  ApplicationDetailDto,
  PipelineGroupDto,
  GetApplicationsResult,
} from "@/lib/dto/applications";
