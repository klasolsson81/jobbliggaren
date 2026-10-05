import type { EmailStepState } from "@/lib/auth/challenge-action-state";
import type { ApplicationStatus } from "@/lib/dto/applications";
import type { ApplicationsView } from "@/lib/applications/view";
import type { SortDir, TableSortKey } from "@/lib/applications/table-sort";
export type InformationSnapshots = {
  modalOpener: { kind: "detail"; href: string; hasAriaLabel: boolean } | { kind: "main" } | null;
  email: { email: string; state: EmailStepState };
  applications: { query: string; statusFilter: ApplicationStatus | null; view: ApplicationsView };
  table: { selectedIds: readonly string[]; sortKey: TableSortKey; sortDir: SortDir; page: number; rowsKey: string };
} & { [key in `status:${ApplicationStatus}`]: { open: boolean; expanded: boolean } }
  & { [key in `board:${ApplicationStatus}`]: boolean }
  & { [key in `ad:${string}`]: boolean };
export type SnapshotKey = keyof InformationSnapshots;
