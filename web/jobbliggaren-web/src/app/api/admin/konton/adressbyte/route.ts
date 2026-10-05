import type { NextRequest } from "next/server";
import { readAdminBody, refuse, relay } from "@/lib/admin/account-bff";
import { getPendingEmailChange } from "@/lib/api/admin-accounts";
import type { PendingEmailChangeDto } from "@/lib/dto/admin-accounts";

/**
 * One account's pending address change for the admin page's panel (#1975, ADR 0153), read beside the account's
 * details so that a fault on the volatile instance costs only this fact (ADR 0150 D2). The island sends the account
 * id in a body, so it never enters a URL the browser sends. Nothing pending answers `{ pending: null }`.
 */
export async function POST(request: NextRequest) {
  const read = await readAdminBody(request);
  if ("refusal" in read) return read.refusal;

  const { id } = read.body;
  if (typeof id !== "string") return refuse(400, "invalid");

  const result = await getPendingEmailChange(id, request.signal);
  return relay<{ pending: PendingEmailChangeDto | null }>(
    result.kind === "ok" ? { kind: "ok", data: { pending: result.data } } : result,
  );
}
