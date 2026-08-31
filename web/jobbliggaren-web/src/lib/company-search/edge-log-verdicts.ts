import type { EdgeLogVerdicts } from "@/test/edge-log-pin";

/**
 * The edge-log verdict for every query key /foretag/sok emits.
 *
 * A plain module rather than part of the fact beside it, so `app-surface-coverage.test.ts`
 * can require that EVERY name on the C# pin's array is judged by SOME surface. A per-surface
 * subset check cannot state that property: with one surface emitting every pinned name the
 * check was a no-op, and with several it is asserted by nobody.
 */

export const EDGE_LOG_VERDICT: EdgeLogVerdicts = {
  namn: {
    verdict: "must-not-reach-a-stored-log-post",
    reason:
      "Unbounded free text, the same class as /jobb's q. Worse in one respect: proxy.ts washes " +
      "an org.nr-shaped value out of this field, which is the app stating that it EXPECTS org.nr " +
      "here, and for an enskild firma the org.nr IS the holder's personnummer (#841). The wash " +
      "fires only on org.nr-shaped values; every other free-text string passes through it.",
  },
  sni: {
    verdict: "kept",
    reason:
      "SNI branch codes from the public SCB taxonomy, joined on one axis. A closed published " +
      "value space that names an industry, never a person.",
  },
  kommun: {
    verdict: "kept",
    reason: "Municipality codes from the public taxonomy, same closed published class as sni.",
  },
  sida: {
    verdict: "kept",
    reason: "A page ordinal. It carries no user content.",
  },
  avvisat: {
    verdict: "kept",
    reason:
      "A single sentinel recording that an org.nr-shaped name was refused. One bit, and it is " +
      "set precisely when the value that triggered it was NOT carried forward.",
  },
};
