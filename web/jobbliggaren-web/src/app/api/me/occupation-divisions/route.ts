import { NextResponse, type NextRequest } from "next/server";
import { getOccupationDivisions } from "@/lib/api/company-criteria";

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
