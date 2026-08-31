import type { EdgeLogVerdicts } from "@/test/edge-log-pin";

/**
 * The edge-log verdict for every query key /admin/granskning emits.
 *
 * A plain module rather than part of the fact beside it, so `app-surface-coverage.test.ts`
 * can require that EVERY name on the C# pin's array is judged by SOME surface. A per-surface
 * subset check cannot state that property: with one surface emitting every pinned name the
 * check was a no-op, and with several it is asserted by nobody.
 */

export const EDGE_LOG_VERDICT: EdgeLogVerdicts = {
  userId: {
    verdict: "must-not-reach-a-stored-log-post",
    reason:
      "A DIRECT identifier of a natural person (Art. 4(1)), and the URL discloses more than the " +
      "identifier: it says WHOSE audit records were read. The edge's existing `uid` entry does " +
      "not cover it, because the filter matches keys exactly and case-sensitively.",
  },
  from: {
    verdict: "kept",
    reason:
      "An ISO 8601 instant bounding the query window. It names a time, not a person, and the " +
      "same value is produced by anyone who picks that window.",
  },
  to: {
    verdict: "kept",
    reason: "An ISO 8601 instant, same bounded class as from.",
  },
  eventType: {
    verdict: "must-not-reach-a-stored-log-post",
    reason:
      "It reads like a closed enum and is not. The control is <input type=\"text\" " +
      "maxLength={100}> in a native GET form, and GetAuditLogEntriesQueryValidator's own comment " +
      "calls it fri-text-fält while bounding LENGTH only. A length cap is not a content cap, " +
      "which is the same distinction q is deleted on, and an admin investigating an incident has " +
      "identifiers in the clipboard and five text boxes in front of them. The hint copy is " +
      "signage, not a gate.",
  },
  aggregateType: {
    verdict: "must-not-reach-a-stored-log-post",
    reason: "Same control, same validator, same free-text class as eventType.",
  },
  page: {
    verdict: "kept",
    reason: "A page ordinal. It carries no user content.",
  },
  pageSize: {
    verdict: "kept",
    reason: "A page-size integer. It carries no user content.",
  },
};
