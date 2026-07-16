import { NextResponse, type NextRequest } from "next/server";
import { z } from "zod";
import { previewCriterionCount } from "@/lib/api/company-criteria";

/**
 * #560 PR-3 — live magnitude-preview proxy for the criterion picker. The client-borne dialog cannot
 * call the `server-only` `previewCriterionCount` fetcher directly (session cookie + BACKEND_URL are
 * server context), so this mirrors `/api/me/match-count-preview`: coerce the body cheaply here,
 * delegate to the server fetcher, map `ApiResult` → HTTP status.
 *
 * Degradation contract (same as match-count-preview): any failure → a NON-2xx so the hook's
 * `!res.ok` branch nulls the count (a neutral placeholder, never a false 0). A 200 + `{ magnitude: 0 }`
 * on a backend error would be disinformation. The authoritative validation (code existence + caps)
 * lives in the backend; here only the shape is coerced to string lists with a defensive size cap.
 */
const codeList = z.array(z.string().max(32)).max(2000).default([]);
const bodySchema = z.object({
  sniCodes: codeList,
  municipalityCodes: codeList,
});

export async function POST(request: NextRequest) {
  let raw: unknown;
  try {
    raw = await request.json();
  } catch {
    return NextResponse.json({ error: "Ogiltig body." }, { status: 400 });
  }

  const parsed = bodySchema.safeParse(raw);
  if (!parsed.success) {
    return NextResponse.json({ error: "Ogiltig body." }, { status: 400 });
  }

  const result = await previewCriterionCount({
    sniCodes: parsed.data.sniCodes,
    municipalityCodes: parsed.data.municipalityCodes,
  });

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
    // forbidden/notFound/error (incl. a 400 for a missing axis) → 502: the hook nulls the count.
    default:
      return NextResponse.json({}, { status: 502 });
  }
}
