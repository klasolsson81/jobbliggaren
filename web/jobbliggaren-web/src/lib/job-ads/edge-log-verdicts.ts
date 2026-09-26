import type { EdgeLogVerdicts } from "@/test/edge-log-pin";

/**
 * The edge-log verdict for every query key /jobb emits.
 *
 * A plain module rather than part of the fact beside it, so `app-surface-coverage.test.ts`
 * can require that EVERY name on the C# pin's array is judged by SOME surface. A per-surface
 * subset check cannot state that property: with one surface emitting every pinned name the
 * check was a no-op, and with several it is asserted by nobody.
 */

export const EDGE_LOG_VERDICT: EdgeLogVerdicts = {
  employer: {
    verdict: "must-not-reach-a-stored-log-post",
    reason:
      "Oversikt's summary links carry the user's WHOLE watched org.nr set in one URL, and a " +
      "recent-search replay carries the employers one captured search filtered on (#1471). " +
      "A single org.nr is public-register data any visitor can type; the " +
      "set is whom this user watches; ADR 0087 D8(b) personal data about the user, protected " +
      "there by owner-scoped access and an Art. 17 cascade, neither of which reaches an edge " +
      "log. #1547.",
  },
  q: {
    verdict: "must-not-reach-a-stored-log-post",
    reason:
      "The user's free search text, and the only key on this surface that no gate constrains: " +
      "parseQParam gates arity and clampSubMinimumQ gates length, neither gates CONTENT. A " +
      "retention purpose therefore cannot be written for it; Art. 5(1)(c), the same ground uid " +
      "was deleted on. It is the field that can carry a personnummer, a former employer or a " +
      "health-adjacent word, and nothing on the request path would know that it had. That last " +
      "case is Art. 9(1) with no 9(2) exception available to an edge log; a stronger ground " +
      "than 5(1)(c), and the one that holds if someone later wants q back for diagnostics.",
  },
  occupationGroup: {
    verdict: "kept",
    reason:
      "A JobTech taxonomy conceptId. Identical for every visitor who picks the same filter; it " +
      "names an occupation, never a person, and any visitor can type it unauthenticated.",
  },
  region: {
    verdict: "kept",
    reason:
      "A public taxonomy region code, same class as occupationGroup: it names a place, and the " +
      "same value is produced by every visitor who picks that place.",
  },
  municipality: {
    verdict: "kept",
    reason:
      "A public taxonomy municipality code, same class as region. A residence is not derivable " +
      "from it; it is the area searched, not the searcher's.",
  },
  employmentType: {
    verdict: "kept",
    reason:
      "A public taxonomy employment-type code. One of a small closed set shared by every " +
      "visitor who picks it.",
  },
  worktimeExtent: {
    verdict: "kept",
    reason: "A public taxonomy worktime-extent code, same closed-set class as employmentType.",
  },
  matchGrades: {
    verdict: "kept",
    reason:
      "Fixed enum names from a three-value set. It records the position of a UI control, not a " +
      "score and not any ad or profile the grade was computed from.",
  },
  matchning: {
    verdict: "kept",
    reason:
      "A single sentinel word recording that the matching master switch is off. One bit, " +
      "shared by everyone who flips it.",
  },
  relaterade: {
    verdict: "kept",
    reason:
      "A single sentinel word recording that the include-related toggle is on. One bit, same " +
      "class as matchning.",
  },
  doljAnsokta: {
    verdict: "kept",
    reason:
      "A single sentinel word. It only means anything for an authenticated seeker, so it does " +
      "say something about the requester's state; but it draws from a closed two-value space " +
      "that identifies nobody, and it has a stated purpose: it selects which server-side query " +
      "path ran.",
  },
  baraMatchade: {
    verdict: "kept",
    reason:
      "A single sentinel word, same authenticated-but-closed-space class as doljAnsokta.",
  },
  distans: {
    verdict: "kept",
    reason:
      "A single sentinel word recording that the remote facet is on. One bit; it describes the " +
      "ads wanted, not the searcher's location.",
  },
  sortBy: {
    verdict: "kept",
    reason: "An ordering enum from a closed set. It carries no user content.",
  },
  pageSize: {
    verdict: "kept",
    reason: "A page-size integer. It carries no user content.",
  },
  page: {
    verdict: "kept",
    reason: "A page ordinal. It carries no user content.",
  },
  commit: {
    verdict: "kept",
    reason:
      "A transient intent boolean the client strips after mount; a constant string, never state " +
      "and never user input.",
  },
};
