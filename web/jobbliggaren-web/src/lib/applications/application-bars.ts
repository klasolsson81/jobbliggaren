import type { ApplicationStatus } from "@/lib/dto/applications";
import { activeCount, statusCount, totalCount, type PipelineCounts } from "./pipeline-counts";

/** One bar in the Mina ansökningar card. `statuses` says which pipeline steps the bar sums. */
export interface ApplicationBar {
  readonly key: "draft" | "submitted" | "acknowledged" | "interview" | "offer";
  readonly statuses: ReadonlyArray<ApplicationStatus>;
  readonly count: number;
  /** `count / total`, 0 when there is nothing to divide by. Drives the fill width. */
  readonly fraction: number;
}

export interface ApplicationBars {
  readonly rows: ReadonlyArray<ApplicationBar>;
  readonly total: number;
  readonly active: number;
  /** The four terminal statuses rolled into one line: `total - active`. */
  readonly terminal: number;
}

const BARS: ReadonlyArray<Pick<ApplicationBar, "key" | "statuses">> = [
  { key: "draft", statuses: ["Draft"] },
  { key: "submitted", statuses: ["Submitted"] },
  { key: "acknowledged", statuses: ["Acknowledged"] },
  // The two interview steps are one bar: the card answers "where are my applications", and
  // "booked" versus "in progress" is the detail /ansokningar carries.
  { key: "interview", statuses: ["InterviewScheduled", "Interviewing"] },
  { key: "offer", statuses: ["OfferReceived"] },
];

/**
 * The bar list the Mina ansökningar card renders over the pipeline counts (ADR 0140).
 *
 * The draft bar is included only when it holds something: an unsent draft is not a step on the
 * way to an employer, so an empty one would be a row about nothing. Every other bar renders at
 * zero, because a zero there IS the answer ("no interviews yet").
 *
 * `total`/`active`/`terminal` read the same two SSOTs as `/ansokningar`'s toolbar
 * (`PIPELINE_ORDER`, `ACTIVE_PIPELINE_STATUSES`), so the card and the board cannot disagree.
 */
export function applicationBars(counts: PipelineCounts): ApplicationBars {
  const total = totalCount(counts);
  const active = activeCount(counts);
  const rows = BARS.flatMap((bar) => {
    const count = bar.statuses.reduce((sum, s) => sum + statusCount(counts, s), 0);
    if (bar.key === "draft" && count === 0) return [];
    return [{ ...bar, count, fraction: total > 0 ? count / total : 0 }];
  });
  return { rows, total, active, terminal: total - active };
}
