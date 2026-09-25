import type { EdgeLogVerdicts } from "@/test/edge-log-pin";

/**
 * The edge-log verdict for every query key an identity provider can put on
 * `/api/auth/oauth/{provider}/callback` (#1744, ADR 0142 D8).
 *
 * The keys are the provider's, not ours: no URL builder in this app emits them, so the inventory
 * is this declared list. Google's OpenID Connect reference (last updated 2026-03-27, read
 * 2026-09-25) documents `code`, `state`, `scope` and `iss` on success and `error` and
 * `error_description` on failure; `authuser`, `prompt` and `hd` are undocumented and have been
 * reported in practice, which is why they are judged here too.
 */
export const EDGE_LOG_VERDICT: EdgeLogVerdicts = {
  code: {
    verdict: "must-not-reach-a-stored-log-post",
    reason: "A single-use authorization code. With the flow's verifier it is exchanged for the identity.",
  },
  state: {
    verdict: "must-not-reach-a-stored-log-post",
    reason:
      "The flow's 256-bit binding: the key to its record and the value of the browser's state " +
      "cookie. A credential for the length of the flow.",
  },
  hd: {
    verdict: "must-not-reach-a-stored-log-post",
    reason:
      "The Workspace domain of the account. On a personal domain it identifies a person " +
      "(security-auditor m-2, 6a form round).",
  },
  error_description: {
    verdict: "must-not-reach-a-stored-log-post",
    reason: "Free text the provider writes. Its content is unbounded, the same class as q.",
  },
  scope: {
    verdict: "kept",
    reason: "The scopes granted, from the provider's closed published set (openid email).",
  },
  iss: {
    verdict: "kept",
    reason: "The issuer's identifier, one published URL per provider.",
  },
  error: {
    verdict: "kept",
    reason: "An OAuth 2.0 error code from the closed set RFC 6749 §4.1.2.1 defines.",
  },
  authuser: {
    verdict: "kept",
    reason: "An index into the browser's signed-in Google sessions. A small ordinal, never content.",
  },
  prompt: {
    verdict: "kept",
    reason: "The prompt the provider showed, from its closed set (none, consent, select_account).",
  },
};
