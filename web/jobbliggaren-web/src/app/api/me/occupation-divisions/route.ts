import { NextResponse, type NextRequest } from "next/server";
import { getOccupationDivisions } from "@/lib/api/company-criteria";

/**
 * #1682 — BFF for the bransch picker's occupation block. GET with `q`; the backend validates the
 * word (2–100 characters) and answers 400 below the floor, which this route passes through as an
 * empty-bodied 400 rather than inventing a message. Same result mapping as the preview-count route.
 */
export async function GET(request: NextRequest) {
  const q = request.nextUrl.searchParams.get("q") ?? "";
  const result = await getOccupationDivisions(q);
  switch (result.kind) {
    case "ok":
      return NextResponse.json(result.data);
    case "unauthorized":
      return NextResponse.json({}, { status: 401 });
    case "rateLimited":
      return NextResponse.json(
        {},
        {
          status: 429,
          headers: { "Retry-After": String(result.retryAfterSeconds) },
        },
      );
    case "error":
      return NextResponse.json({}, { status: 502 });
    default:
      return NextResponse.json({}, { status: 502 });
  }
}
