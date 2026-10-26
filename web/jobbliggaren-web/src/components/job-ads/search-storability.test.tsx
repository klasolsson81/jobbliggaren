import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import {
  ReportSearchStorability,
  SearchStorability,
  useIsSearchStorable,
} from "./search-storability";
import type { JobbUrlState } from "@/lib/job-ads/search-params";

/**
 * #2199 PR 2b — the verdict bridge on its own. `jobb-results.test.tsx` pins that every results branch
 * reports, and `page.test.tsx` that the page provides; this pins what the hero reads back.
 */
const search = (overrides: Partial<JobbUrlState> = {}): JobbUrlState => ({
  q: "backend",
  occupationGroup: [],
  region: [],
  municipality: [],
  remote: false,
  employmentType: [],
  worktimeExtent: [],
  matchGrades: [],
  employer: [],
  sortBy: "PublishedAtDesc",
  ...overrides,
});

function Probe({ shown }: { shown: JobbUrlState }) {
  return <span data-testid="storable">{String(useIsSearchStorable(shown))}</span>;
}

const storable = () => screen.getByTestId("storable");

describe("search storability", () => {
  it("is false with no provider at all", () => {
    render(<Probe shown={search()} />);
    expect(storable()).toHaveTextContent("false");
  });

  it("is false before any verdict arrives", () => {
    render(
      <SearchStorability>
        <Probe shown={search()} />
      </SearchStorability>,
    );
    expect(storable()).toHaveTextContent("false");
  });

  it("is true when the verdict for the shown search says yes", () => {
    render(
      <SearchStorability>
        <ReportSearchStorability search={search()} storable />
        <Probe shown={search()} />
      </SearchStorability>,
    );
    expect(storable()).toHaveTextContent("true");
  });

  it("is false when the verdict says no", () => {
    render(
      <SearchStorability>
        <ReportSearchStorability search={search()} storable={false} />
        <Probe shown={search()} />
      </SearchStorability>,
    );
    expect(storable()).toHaveTextContent("false");
  });

  // The transition case: the previous search's results are still mounted with their yes while the
  // hero already shows the next search.
  it.each([
    ["the text", { q: "imam" }],
    ["an occupation group", { occupationGroup: ["2L2s_EU8_PzR"] }],
    ["the employer", { employer: ["5566010101"] }],
  ])("is false when the yes answers another search (%s differs)", (_, change) => {
    render(
      <SearchStorability>
        <ReportSearchStorability search={search()} storable />
        <Probe shown={search(change)} />
      </SearchStorability>,
    );
    expect(storable()).toHaveTextContent("false");
  });

  it("follows the latest verdict", () => {
    const { rerender } = render(
      <SearchStorability>
        <ReportSearchStorability search={search()} storable />
        <Probe shown={search()} />
      </SearchStorability>,
    );
    rerender(
      <SearchStorability>
        <ReportSearchStorability search={search()} storable={false} />
        <Probe shown={search()} />
      </SearchStorability>,
    );
    expect(storable()).toHaveTextContent("false");
  });
});
