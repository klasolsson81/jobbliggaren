"use server";

import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { AUTH_ERROR_CODES } from "@/lib/auth/auth-error-codes";
import type {
  CodeStepState,
  ConsentStepState,
  EmailStepState,
  LinkStepState,
  ResendState,
} from "@/lib/auth/challenge-action-state";
import {
  acceptTermsInputSchema,
  codeInputSchema,
  emailInputSchema,
  linkTokenInputSchema,
} from "@/lib/auth/challenge-schemas";
import { cookieSafeNext, resendCooldownRemaining } from "@/lib/auth/login-flow";
import {
  clearLoginFlow,
  nowEpochSeconds,
  readLoginFlow,
  writeLoginFlow,
} from "@/lib/auth/login-flow-cookie";
import { DEFAULT_REDIRECT_PATH, safeRedirectPath } from "@/lib/auth/safe-redirect";
import { setSessionCookie } from "@/lib/auth/session";
import { parseResponse } from "@/lib/dto/_helpers";
import { loginChallengeResponseSchema, loginOutcomeSchema } from "@/lib/dto/login-challenge";
import { env } from "@/lib/env";
import { forwardedHeaders } from "@/lib/http/forwarded-headers";
import { readProblemBody, readProblemTitle } from "@/lib/http/problem";

// The login flow's Server Actions (ADR 0142 "Page form").
//
// ONE RULE holds the flow together, and `challenge-actions.test.ts` pins it per action: a return
// that writes the flow cookie ends in `redirect()`, and a state return never touches the cookie.
// Writing a cookie makes Next re-render the current route in the same response, so a panel held in
// action state would be lost to the page's own phase check. `resendCode` is the one exception: the
// phase it writes is the phase the page is already on.
//
// Nothing here logs. The address, the code, the link token and the grant never reach a console.

const ENTRY = "/logga-in";
const CODE_STEP = "/logga-in/kod";
const CONSENT_STEP = "/logga-in/villkor";

/** `forwardedHeaders()` on every call: all four routes are rate-limited per IP, and without the
 *  relay that partition collapses into one bucket behind Next. No browser cookie is forwarded. */
async function post(path: string, body: unknown): Promise<Response> {
  return fetch(`${env.BACKEND_URL}/api/v1/auth${path}`, {
    method: "POST",
    headers: { ...(await forwardedHeaders()), "Content-Type": "application/json" },
    body: JSON.stringify(body),
    cache: "no-store",
  });
}

const formString = (formData: FormData, name: string): string => {
  const value = formData.get(name);
  return typeof value === "string" ? value : "";
};

export async function requestCode(
  _prev: EmailStepState,
  formData: FormData
): Promise<EmailStepState> {
  const t = await getTranslations("pages");
  const typed = formString(formData, "email");
  const parsed = emailInputSchema.safeParse(typed);
  if (!parsed.success) {
    return {
      error: t("auth.passwordless.entry.emailRequired"),
      channel: "field",
      values: { email: typed },
    };
  }
  const email = parsed.data;

  // The same address again while its code is still live mints nothing. Inside the server's
  // cooldown a second request answers with a challenge id that has NO record, and storing that id
  // would make the code already mailed unverifiable. The comparison is deliberately exact: the
  // backend's fold (NFC, upper-invariant) is authoritative and is not mirrored here, so a
  // different spelling of the same address simply mints. "Skicka ny kod" is the one way to ask
  // for a fresh code, and nothing is written here, so re-submitting cannot extend the cookie.
  const flow = await readLoginFlow();
  if (flow?.phase === "code" && !flow.dead && flow.email === email) {
    redirect(CODE_STEP);
  }

  let challengeId: string;
  try {
    const res = await post("/challenge", { email });
    if (res.status === 400) {
      return {
        error: t("auth.passwordless.entry.emailRequired"),
        channel: "field",
        values: { email: typed },
      };
    }
    if (res.status === 429) {
      return {
        error: t("auth.passwordless.errors.tooManyAttempts"),
        channel: "status",
        values: { email: typed },
      };
    }
    if (res.status !== 202) {
      // Every 503 on this route means "try again later" (the store is down, or mail cannot be
      // delivered), so they need no telling apart here.
      return {
        error: t("auth.passwordless.errors.unavailable"),
        channel: "status",
        values: { email: typed },
      };
    }
    ({ challengeId } = await parseResponse(
      res,
      loginChallengeResponseSchema,
      "POST /api/v1/auth/challenge"
    ));
  } catch {
    return {
      error: t("auth.passwordless.errors.unavailable"),
      channel: "status",
      values: { email: typed },
    };
  }

  await writeLoginFlow({
    phase: "code",
    challengeId,
    email,
    next: cookieSafeNext(safeRedirectPath(formString(formData, "next"))),
    sentAt: nowEpochSeconds(),
  });
  redirect(CODE_STEP);
}

export async function verifyCode(
  _prev: CodeStepState,
  formData: FormData
): Promise<CodeStepState> {
  const t = await getTranslations("pages");
  const flow = await readLoginFlow();

  // The cookie and the challenge are born together and live equally long, so a code submitted
  // with no cookie left is a login that ran out. There is no id to verify and no address to
  // resend to; the entry page says so.
  if (!flow) {
    await writeLoginFlow({ phase: "notice", notice: "codeExpired" });
    redirect(ENTRY);
  }
  // Another phase, or a code phase already marked dead: the page renders what the cookie says.
  if (flow.phase !== "code" || flow.dead) {
    redirect(CODE_STEP);
  }

  const parsed = codeInputSchema.safeParse(formString(formData, "code"));
  if (!parsed.success) {
    return { error: t("auth.passwordless.code.wrongCode"), channel: "field" };
  }

  let res: Response;
  try {
    res = await post("/challenge/verify", { challengeId: flow.challengeId, code: parsed.data });
  } catch {
    return { error: t("auth.passwordless.errors.unavailable"), channel: "status" };
  }

  if (res.status === 400) {
    const title = await readProblemTitle(res);
    return title === AUTH_ERROR_CODES.LoginCodeWrongLastAttempt
      ? { error: t("auth.passwordless.code.wrongCode"), channel: "field", lastAttempt: true }
      : { error: t("auth.passwordless.code.wrongCode"), channel: "field" };
  }
  if (res.status === 410) {
    const title = await readProblemTitle(res);
    await writeLoginFlow({
      ...flow,
      dead: title === AUTH_ERROR_CODES.LoginCodeBurned ? "burned" : "expired",
    });
    redirect(CODE_STEP);
  }
  if (res.status === 429) {
    return { error: t("auth.passwordless.errors.tooManyAttempts"), channel: "status" };
  }
  if (!res.ok) {
    return { error: t("auth.passwordless.errors.unavailable"), channel: "status" };
  }

  let outcome;
  try {
    outcome = await parseResponse(res, loginOutcomeSchema, "POST /api/v1/auth/challenge/verify");
  } catch {
    return { error: t("auth.passwordless.errors.unavailable"), channel: "status" };
  }

  if (outcome.outcome === "signedIn") {
    // Always persistent (ADR 0142 D4). The flow cookie goes in the same action, or a logged-in
    // user would carry the typed address on every request for the rest of the 15 minutes.
    await setSessionCookie(outcome.sessionId, true);
    await clearLoginFlow();
    redirect(safeRedirectPath(flow.next));
  }
  if (outcome.outcome === "consentRequired") {
    await writeLoginFlow({ phase: "consent", grantToken: outcome.grantToken, next: flow.next });
    redirect(CONSENT_STEP);
  }
  await writeLoginFlow({ phase: "outcome", result: outcome });
  redirect(CODE_STEP);
}

/** Takes nothing from the form: the address to resend to is the cookie's. */
export async function resendCode(): Promise<ResendState> {
  const t = await getTranslations("pages");
  const flow = await readLoginFlow();
  if (!flow) {
    await writeLoginFlow({ phase: "notice", notice: "codeExpired" });
    redirect(ENTRY);
  }
  if (flow.phase !== "code") {
    redirect(CODE_STEP);
  }
  // The button is disabled for this long; this is the same rule for a POST that skipped the
  // button. Inside the server's window a request would come back with a record-less id and kill
  // the code already mailed, and that holds for a burned code's link too.
  if (resendCooldownRemaining(flow, nowEpochSeconds()) > 0) {
    return { status: "cooling" };
  }

  let challengeId: string;
  try {
    const res = await post("/challenge", { email: flow.email });
    if (res.status === 429) {
      return { status: "error", error: t("auth.passwordless.errors.tooManyAttempts") };
    }
    if (res.status !== 202) {
      return { status: "error", error: t("auth.passwordless.errors.unavailable") };
    }
    ({ challengeId } = await parseResponse(
      res,
      loginChallengeResponseSchema,
      "POST /api/v1/auth/challenge (resend)"
    ));
  } catch {
    return { status: "error", error: t("auth.passwordless.errors.unavailable") };
  }

  // The one state return that writes the cookie: the phase written is the phase the page is on,
  // so the re-render it causes lands on the same step (with `dead` gone and the cooldown restarted).
  await writeLoginFlow({
    phase: "code",
    challengeId,
    email: flow.email,
    next: flow.next,
    sentAt: nowEpochSeconds(),
  });
  return { status: "sent" };
}

/** "Byt e-postadress": a submit and not a link, because a GET cannot clear the typed address. */
export async function changeEmail(): Promise<void> {
  await clearLoginFlow();
  redirect(ENTRY);
}

export async function completeRegistration(
  _prev: ConsentStepState,
  formData: FormData
): Promise<ConsentStepState> {
  const t = await getTranslations("pages");
  const flow = await readLoginFlow();
  // The consent cookie lives as long as the grant, so no cookie means no usable grant.
  if (!flow) {
    await writeLoginFlow({ phase: "notice", notice: "grantUnusable" });
    redirect(ENTRY);
  }
  if (flow.phase !== "consent") {
    redirect(CONSENT_STEP);
  }

  // Refused BEFORE the fetch. The grant comes from the cookie only, never from the form.
  if (!acceptTermsInputSchema.safeParse(formData.get("acceptTerms")).success) {
    return { error: t("auth.passwordless.consent.termsRequired"), channel: "field" };
  }

  let res: Response;
  try {
    res = await post("/challenge/complete", { grantToken: flow.grantToken, acceptTerms: true });
  } catch {
    return { error: t("auth.passwordless.errors.unavailable"), channel: "status" };
  }

  if (res.status === 400) {
    // The refusal of an unticked box is a validation shape, `{ errors: { AcceptTerms: [...] } }`,
    // not a title, and it leaves the grant usable. The key's casing is not pinned by the backend.
    const body = await readProblemBody(res);
    const refusedTerms = Object.keys(body?.errors ?? {}).some(
      (key) => key.toLowerCase() === "acceptterms"
    );
    return refusedTerms
      ? { error: t("auth.passwordless.consent.termsRequired"), channel: "field" }
      : { error: t("auth.passwordless.errors.unavailable"), channel: "status" };
  }
  if (res.status === 410) {
    // One answer for every unusable grant: unknown, expired, used, or claimed by another tab.
    await writeLoginFlow({ phase: "notice", notice: "grantUnusable" });
    redirect(ENTRY);
  }
  if (res.status === 503) {
    // Two 503s mean different things here. Closed registration is a terminal outcome; a store or
    // mail outage is "try again", and carries no title or another one. Never told apart by status.
    const title = await readProblemTitle(res);
    if (title === AUTH_ERROR_CODES.RegistrationsClosed) {
      await writeLoginFlow({ phase: "outcome", result: { outcome: "registrationClosed" } });
      redirect(CONSENT_STEP);
    }
    return { error: t("auth.passwordless.errors.unavailable"), channel: "status" };
  }
  if (res.status === 429) {
    return { error: t("auth.passwordless.errors.tooManyAttempts"), channel: "status" };
  }
  if (!res.ok) {
    return { error: t("auth.passwordless.errors.unavailable"), channel: "status" };
  }

  let outcome;
  try {
    outcome = await parseResponse(res, loginOutcomeSchema, "POST /api/v1/auth/challenge/complete");
  } catch {
    return { error: t("auth.passwordless.errors.unavailable"), channel: "status" };
  }

  if (outcome.outcome === "signedIn") {
    await setSessionCookie(outcome.sessionId, true);
    await clearLoginFlow();
    redirect(safeRedirectPath(flow.next));
  }
  if (outcome.outcome === "consentRequired") {
    // `complete` never answers this; a grant cannot lead to another grant.
    return { error: t("auth.passwordless.errors.unavailable"), channel: "status" };
  }
  await writeLoginFlow({ phase: "outcome", result: outcome });
  redirect(CONSENT_STEP);
}

/**
 * The link landing's one button. The token is read from the form body only, never from the URL the
 * POST arrives on, and the landing never reads the flow cookie: the click comes from a mail client,
 * cross-site, and a Strict cookie is not sent with it (ADR 0142 D2).
 *
 * Its outcomes are action state, which is safe here and only here: the page has no phase check to
 * lose them to. A link never leads to the consent step, and it carries no `next`.
 */
export async function consumeLink(
  _prev: LinkStepState,
  formData: FormData
): Promise<LinkStepState> {
  const t = await getTranslations("pages");
  const parsed = linkTokenInputSchema.safeParse(formString(formData, "token"));
  if (!parsed.success) {
    return { kind: "unusable" };
  }

  let res: Response;
  try {
    res = await post("/link", { token: parsed.data });
  } catch {
    return { kind: "error", error: t("auth.passwordless.errors.unavailable") };
  }

  if (res.status === 410 || res.status === 400) {
    return { kind: "unusable" };
  }
  if (res.status === 429) {
    return { kind: "error", error: t("auth.passwordless.errors.tooManyAttempts") };
  }
  if (!res.ok) {
    return { kind: "error", error: t("auth.passwordless.errors.unavailable") };
  }

  let outcome;
  try {
    outcome = await parseResponse(res, loginOutcomeSchema, "POST /api/v1/auth/link");
  } catch {
    return { kind: "error", error: t("auth.passwordless.errors.unavailable") };
  }

  if (outcome.outcome === "signedIn") {
    // A session that already existed in this browser is replaced here and left alive on the
    // server: revoking it would let anyone's link sign another account out everywhere. A code
    // login for another address may be half-way in the same browser, so its cookie goes too.
    await setSessionCookie(outcome.sessionId, true);
    await clearLoginFlow();
    redirect(DEFAULT_REDIRECT_PATH);
  }
  if (outcome.outcome === "consentRequired") {
    return { kind: "unusable" };
  }
  return { kind: "outcome", result: outcome };
}
