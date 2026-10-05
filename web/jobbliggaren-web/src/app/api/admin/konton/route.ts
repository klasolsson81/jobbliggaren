import type { NextRequest } from "next/server";
import { parseSearchCriteria, readAdminBody, refuse, relay } from "@/lib/admin/account-bff";
import { searchAccounts } from "@/lib/api/admin-accounts";

/**
 * The account list's search for the admin page's island (#1974, ADR 0151): the term travels here in the
 * body and on to the backend in a body, so it never enters a URL. The answer is one page of accounts and
 * the status counts.
 */
export async function POST(request: NextRequest) {
  const read = await readAdminBody(request);
  if ("refusal" in read) return read.refusal;

  const criteria = parseSearchCriteria(read.body);
  if (criteria === null) return refuse(400, "invalid");

  return relay(await searchAccounts(criteria, request.signal));
}
