import { NextResponse, type NextRequest } from "next/server";
import { loadAdminOverview } from "@/lib/api/admin-overview";

const HEADERS = { "Cache-Control": "private, no-store" } as const;

export async function GET(request: NextRequest) {
  const result = await loadAdminOverview(request.signal);
  if (result.kind !== "ok") {
    return NextResponse.json({ error: result.kind }, {
      status: result.kind === "unauthorized" ? 401 : 403,
      headers: HEADERS,
    });
  }
  return NextResponse.json(result.data, { headers: HEADERS });
}