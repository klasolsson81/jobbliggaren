import type { NextRequest } from "next/server";
import { readAdminBody, refuse, relay } from "@/lib/admin/account-bff";
import { getAccountDetails } from "@/lib/api/admin-accounts";

/**
 * One account's details for the admin page's panel (#1974, ADR 0151). The island sends the account id in
 * a body, so it never enters a URL the browser sends; the backend reads it from its own path.
 */
export async function POST(request: NextRequest) {
  const read = await readAdminBody(request);
  if ("refusal" in read) return read.refusal;

  const { id } = read.body;
  if (typeof id !== "string") return refuse(400, "invalid");

  return relay(await getAccountDetails(id, request.signal));
}
