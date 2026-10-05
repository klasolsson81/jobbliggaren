"use server";

import { AUTH_ERROR_CODES } from "@/lib/auth/auth-error-codes";
import { checkAddressChange, type AddressChangeState } from "@/lib/auth/address-change";
import { accountEmailChangeNotYetSchema } from "@/lib/dto/account-email-change";
import { env } from "@/lib/env";
import { forwardedHeaders } from "@/lib/http/forwarded-headers";
import { readProblemTitle } from "@/lib/http/problem";

const COMPLETE_PATH = "/api/v1/auth/account-email-change/complete";

const formString = (formData: FormData, name: string): string => {
  const value = formData.get(name);
  return typeof value === "string" ? value : "";
};

/** The earliest instant a "not yet" carries, or null when the answer does not read as one. */
async function completableFromOf(res: Response): Promise<string | null> {
  try {
    const parsed = accountEmailChangeNotYetSchema.safeParse(await res.json());
    return parsed.success ? parsed.data.completableFrom : null;
  } catch {
    return null;
  }
}

/**
 * #1975 (ADR 0153) — the owner completes the address change an administrator started, on the public `/adressbyte`.
 * Anonymous: no session and no cookie is read or written. The forwarded headers keep the route's per-IP budget per
 * client (`challenge-actions.ts`, #1202). Nothing here logs; the addresses go back to the form so it can re-seed them,
 * the code never does.
 *
 * The answers: a 204 is done; one 410 is every refusal; a 409 with an instant is a full match too early, which spends
 * nothing; a 429 and a 503 come before anything is spent, so trying again is true. Every other answer, a lost response
 * and one that does not read among them, may sit over a change that happened, since the backend answers 500 for
 * anything that fails after the swap: it claims nothing and advises no retry.
 */
export async function completeAddressChange(
  _prev: AddressChangeState,
  formData: FormData
): Promise<AddressChangeState> {
  const typed = {
    currentEmail: formString(formData, "currentEmail"),
    newEmail: formString(formData, "newEmail"),
    code: formString(formData, "code"),
  };
  const values = { currentEmail: typed.currentEmail, newEmail: typed.newEmail };
  const checked = checkAddressChange(typed);
  if (!checked.ok) return { kind: "invalid", errors: checked.errors, values };

  let res: Response;
  try {
    res = await fetch(`${env.BACKEND_URL}${COMPLETE_PATH}`, {
      method: "POST",
      headers: { ...(await forwardedHeaders()), "Content-Type": "application/json" },
      body: JSON.stringify(checked.input),
      cache: "no-store",
    });
  } catch {
    return { kind: "unknown", values };
  }

  switch (res.status) {
    case 204:
      return { kind: "done" };
    case 409: {
      const completableFrom = await completableFromOf(res);
      return completableFrom === null
        ? { kind: "unknown", values }
        : { kind: "notYet", completableFrom, values };
    }
    case 410:
      return (await readProblemTitle(res)) === AUTH_ERROR_CODES.AccountEmailChangeUnusable
        ? { kind: "refused", values }
        : { kind: "unknown", values };
    case 429:
      return { kind: "tooManyAttempts", values };
    case 503:
      return { kind: "unavailable", values };
    default:
      return { kind: "unknown", values };
  }
}
