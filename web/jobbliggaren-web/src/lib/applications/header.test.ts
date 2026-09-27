import { describe, it, expect } from "vitest";
import { createFormatter, createTranslator } from "next-intl";
import svPages from "../../../messages/sv/pages.json";
import { applicationDetailHeader } from "./header";
import type { ApplicationDetailDto, JobAdSummaryDto } from "@/lib/dto/applications";

const t = createTranslator({ locale: "sv", messages: { pages: svPages }, namespace: "pages" });
const format = createFormatter({ locale: "sv", timeZone: "Europe/Stockholm" });

const liveAd: JobAdSummaryDto = {
  jobAdId: "ad-1",
  title: "Backend-utvecklare",
  company: "Volvo",
  url: "https://example.com/ad",
  source: "Platsbanken",
  publishedAt: "2026-05-01",
  expiresAt: "2026-06-01",
  status: "Active",
};

function header(jobAd: JobAdSummaryDto | null) {
  const application: Pick<ApplicationDetailDto, "id" | "createdAt" | "jobAd"> = {
    id: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
    createdAt: "2026-05-01T08:00:00Z",
    jobAd,
  };
  return applicationDetailHeader(application, t, format);
}

describe("applicationDetailHeader", () => {
  it("names the ad and its company, with no internal id in the subtitle", () => {
    expect(header(liveAd)).toEqual({ title: "Backend-utvecklare", subtitle: "Volvo" });
  });

  it("keeps an archived ad's own identity and adds no removed marker", () => {
    expect(header({ ...liveAd, status: "Archived" })).toEqual({
      title: "Backend-utvecklare",
      subtitle: "Volvo",
    });
  });

  it("marks an erased ad, whose row carries the preserved identity, as removed", () => {
    expect(header({ ...liveAd, status: "Erased" })).toEqual({
      title: "Backend-utvecklare",
      subtitle: "Volvo · Annonsen är borttagen",
    });
  });

  // An application created before #315 has no snapshot, so the backend sends an erased ad
  // with empty identity (`adIdentityOf` normalises it to null).
  it("falls back to the short id and the created date for an erased ad without identity", () => {
    expect(header({ ...liveAd, status: "Erased", title: "", company: "" })).toEqual({
      title: "Ansökan #aaaaaaaa",
      subtitle: "Skapad 1 maj 2026 · Annonsen är borttagen",
    });
  });

  it("falls back to the short id and the created date when there is no ad row", () => {
    expect(header(null)).toEqual({
      title: "Ansökan #aaaaaaaa",
      subtitle: "Skapad 1 maj 2026",
    });
  });
});
