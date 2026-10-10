"use client";

import { useSearchParams } from "next/navigation";
import { CvProfileToggle } from "@/components/resumes/cv-profile-toggle";
import {
  DEFAULT_OUTCOME_FILTER,
  OUTCOME_PARAM,
  parseOutcomeFilter,
} from "@/lib/resumes/review-filter";
import type { RenderProfile } from "@/lib/dto/parsed-resume";

/**
 * The profile switch on the review ledger (#2083). The outcome filter is a lens that is valid in
 * both profiles, so it carries across the switch; the dimension names an item of this profile's
 * set and does not. The filter lives in the URL and changes without a navigation, so the hrefs
 * are read from the live query here rather than fixed when the server rendered the page.
 */
export function CvReviewProfileToggle({
  basePath,
  profile,
}: {
  basePath: string;
  profile: RenderProfile;
}) {
  const visa = parseOutcomeFilter(useSearchParams().get(OUTCOME_PARAM));
  return (
    <CvProfileToggle
      basePath={basePath}
      profile={profile}
      query={visa === DEFAULT_OUTCOME_FILTER ? undefined : { [OUTCOME_PARAM]: visa }}
    />
  );
}
