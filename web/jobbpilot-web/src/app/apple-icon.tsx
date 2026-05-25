import { ImageResponse } from "next/og";
import { BrandMarkSvg } from "@/components/brand/brand-mark-svg";

// Next.js 16 file convention: dynamiskt genererad apple-touch-icon för iOS home screen.
// Storlek per Apple HIG: 180×180. Renderas server-side via ImageResponse (satori).
// Geometri från BrandMarkSvg SSOT (CTO M1-triage 2026-05-25 Variant B).

export const size = { width: 180, height: 180 };
export const contentType = "image/png";
export const runtime = "edge";

export default function AppleIcon() {
  return new ImageResponse(
    (
      <div
        style={{
          width: "100%",
          height: "100%",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          background: "#0A2647",
        }}
      >
        <BrandMarkSvg width={130} height={130} primaryFill="#FFFFFF" accentFill="#FFCD00" />
      </div>
    ),
    { ...size }
  );
}
