import { ImageResponse } from "next/og";
import { BrandMarkSvg } from "@/components/brand/brand-mark-svg";
import { OG_MARK_ACCENT, OG_MARK_PAPER, OG_MARK_PRIMARY, OG_SURFACE } from "@/lib/og-tokens";
import { SHARE_IMAGE } from "@/lib/seo/share-image";

/**
 * The square brand mark the public job page names as its link preview (ADR 0165 Amendment 2026-10-25). It reads nothing,
 * no parameter, header or API, so it is built once and is the same image for every ad: an ad's own value here would be
 * a copy a platform keeps out of reach of the page's 410. The edge gives it CORP `cross-origin` (Caddyfile).
 */
export const dynamic = "force-static";

/** `apple-icon`'s 140 in 180, on the 4 px grid: room on every side for a rounded or a round crop. */
const MARK_SIZE = 312;

export function GET(): Response {
  return new ImageResponse(
    (
      <div
        style={{
          width: "100%",
          height: "100%",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          background: OG_SURFACE,
        }}
      >
        <BrandMarkSvg
          width={MARK_SIZE}
          height={MARK_SIZE}
          primaryFill={OG_MARK_PRIMARY}
          accentFill={OG_MARK_ACCENT}
          paperFill={OG_MARK_PAPER}
        />
      </div>
    ),
    { width: SHARE_IMAGE.width, height: SHARE_IMAGE.height }
  );
}
