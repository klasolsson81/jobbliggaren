import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import {
  ReportSearchStorability,
  SearchStorability,
  useIsSearchSaved,
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
        <ReportSearchStorability search={search()} storable committed={false} />
        <Probe shown={search()} />
      </SearchStorability>,
    );
    expect(storable()).toHaveTextContent("true");
  });

  it("is false when the verdict says no", () => {
    render(
      <SearchStorability>
        <ReportSearchStorability search={search()} storable={false} committed={false} />
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
        <ReportSearchStorability search={search()} storable committed={false} />
        <Probe shown={search(change)} />
      </SearchStorability>,
    );
    expect(storable()).toHaveTextContent("false");
  });

  it("follows the latest verdict", () => {
    const { rerender } = render(
      <SearchStorability>
        <ReportSearchStorability search={search()} storable committed={false} />
        <Probe shown={search()} />
      </SearchStorability>,
    );
    rerender(
      <SearchStorability>
        <ReportSearchStorability search={search()} storable={false} committed={false} />
        <Probe shown={search()} />
      </SearchStorability>,
    );
    expect(storable()).toHaveTextContent("false");
  });
});

/**
 * The receipt's half: "Sökningen är sparad" waits for the answer to the request that carried the commit
 * flag, the one that stored or refused the search.
 */
function SavedProbe({ shown }: { shown: JobbUrlState }) {
  return <span data-testid="saved">{String(useIsSearchSaved(shown))}</span>;
}

const saved = () => screen.getByTestId("saved");

describe("search saved", () => {
  it("is false for a preview's yes, which stored nothing", () => {
    render(
      <SearchStorability>
        <ReportSearchStorability search={search()} storable committed={false} />
        <SavedProbe shown={search()} />
      </SearchStorability>,
    );
    expect(saved()).toHaveTextContent("false");
  });

  it("is true for the committed answer's yes for the shown search", () => {
    render(
      <SearchStorability>
        <ReportSearchStorability search={search()} storable committed />
        <SavedProbe shown={search()} />
      </SearchStorability>,
    );
    expect(saved()).toHaveTextContent("true");
  });

  it("is false for the committed answer's no, as after a rate limit or an error", () => {
    render(
      <SearchStorability>
        <ReportSearchStorability search={search()} storable={false} committed />
        <SavedProbe shown={search()} />
      </SearchStorability>,
    );
    expect(saved()).toHaveTextContent("false");
  });

  it("is false when the committed yes answers another search", () => {
    render(
      <SearchStorability>
        <ReportSearchStorability search={search()} storable committed />
        <SavedProbe shown={search({ q: "imam" })} />
      </SearchStorability>,
    );
    expect(saved()).toHaveTextContent("false");
  });
});
