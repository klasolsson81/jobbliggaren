import type { EdgeLogVerdicts } from "@/test/edge-log-pin";

/**
 * The edge-log verdict for every query key /admin/feedback emits (#1979).
 *
 * A plain module rather than part of the fact beside it, so `app-surface-coverage.test.ts`
 * can require that EVERY name on the C# pin's array is judged by SOME surface. A per-surface
 * subset check cannot state that property: with one surface emitting every pinned name the
 * check was a no-op, and with several it is asserted by nobody.
 */

export const EDGE_LOG_VERDICT: EdgeLogVerdicts = {
  id: {
    verdict: "must-not-reach-a-stored-log-post",
    reason:
      "It names one submission, and that row holds the reporter's address and their own words, " +
      "so the id identifies a natural person indirectly (Art. 4(1)) for as long as the row " +
      "exists. A log post carrying it says whose feedback an administrator opened, and when. " +
      "The notice mail's link writes it too, so it reaches the edge from outside the page's own " +
      "links. parseFeedbackQuery keeps only a GUID, so what the page writes back is never more " +
      "than that id.",
  },
  status: {
    verdict: "kept",
    reason:
      "One of four fixed slugs (ny, pagar, atgardad, avstar). The page writes it from the closed " +
      "set alone, and parseFeedbackQuery reads any other value as no filter, so it names a status, " +
      "never a person.",
  },
  sida: {
    verdict: "kept",
    reason:
      "One of the fixed page keys the backend collects feedback for (FEEDBACK_PAGE_KEYS). A " +
      "value outside that set is read as no filter and never written back.",
  },
  sidnr: {
    verdict: "kept",
    reason: "A page ordinal, bounded to 10 000. It carries no user content.",
  },
  fonster: {
    verdict: "kept",
    reason: "The summary's window, one of 7, 30 and 90 days. It carries no user content.",
  },
};
