import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { JobAdPagination, buildPageItems } from "./job-ad-pagination";

describe("buildPageItems", () => {
  it("returns all pages when totalPages <= 7", () => {
    expect(buildPageItems(3, 5)).toEqual([1, 2, 3, 4, 5]);
    expect(buildPageItems(1, 7)).toEqual([1, 2, 3, 4, 5, 6, 7]);
  });

  it("collapses with ellipsis when current is in the middle", () => {
    expect(buildPageItems(7, 12)).toEqual([1, "ellipsis", 6, 7, 8, "ellipsis", 12]);
  });

  it("collapses right-side only when current is near start", () => {
    expect(buildPageItems(2, 12)).toEqual([1, 2, 3, "ellipsis", 12]);
  });

  it("collapses left-side only when current is near end", () => {
    expect(buildPageItems(11, 12)).toEqual([1, "ellipsis", 10, 11, 12]);
  });
});

describe("JobAdPagination", () => {
  const buildHref = (p: number) => `/jobb?page=${p}`;

  it("returns null when totalPages <= 1", () => {
    const { container } = render(
      <JobAdPagination
        page={1}
        pageSize={20}
        totalCount={10}
        buildHref={buildHref}
      />
    );
    expect(container.firstChild).toBeNull();
  });

  it("renders nav with aria-label 'Paginering'", () => {
    render(
      <JobAdPagination
        page={2}
        pageSize={20}
        totalCount={100}
        buildHref={buildHref}
      />
    );
    expect(
      screen.getByRole("navigation", { name: "Paginering" })
    ).toBeInTheDocument();
  });

  it("marks current page with aria-current", () => {
    render(
      <JobAdPagination
        page={3}
        pageSize={20}
        totalCount={100}
        buildHref={buildHref}
      />
    );
    const current = screen.getByText(/^3$/);
    expect(current.closest("[aria-current='page']")).not.toBeNull();
  });

  it("renders Föregående link when not on first page", () => {
    render(
      <JobAdPagination
        page={3}
        pageSize={20}
        totalCount={100}
        buildHref={buildHref}
      />
    );
    expect(screen.getByRole("link", { name: "Föregående" })).toHaveAttribute(
      "href",
      "/jobb?page=2"
    );
  });

  it("renders Nästa link when not on last page", () => {
    render(
      <JobAdPagination
        page={3}
        pageSize={20}
        totalCount={100}
        buildHref={buildHref}
      />
    );
    expect(screen.getByRole("link", { name: "Nästa" })).toHaveAttribute(
      "href",
      "/jobb?page=4"
    );
  });

  it("hides Föregående on first page", () => {
    render(
      <JobAdPagination
        page={1}
        pageSize={20}
        totalCount={100}
        buildHref={buildHref}
      />
    );
    expect(screen.queryByRole("link", { name: "Föregående" })).toBeNull();
  });

  it("hides Nästa on last page", () => {
    render(
      <JobAdPagination
        page={5}
        pageSize={20}
        totalCount={100}
        buildHref={buildHref}
      />
    );
    expect(screen.queryByRole("link", { name: "Nästa" })).toBeNull();
  });

  it("renders summary line in Swedish", () => {
    render(
      <JobAdPagination
        page={2}
        pageSize={20}
        totalCount={45}
        buildHref={buildHref}
      />
    );
    expect(
      screen.getByText("Sida 2 av 3 (45 träffar totalt)")
    ).toBeInTheDocument();
  });

  // #1149 — the total is a CLAIM, and on the register surfaces `totalCount` saturates at a
  // servable cap, so the word "totalt" turns a ceiling into a completeness statement. Both
  // polarities are pinned: without the default case above, flipping the default to false would
  // silently strip the true total from /jobb and nothing would fail.
  it("omits the total when showTotalCount is false, keeping the page position", () => {
    render(
      <JobAdPagination
        page={2}
        pageSize={20}
        totalCount={45}
        buildHref={buildHref}
        showTotalCount={false}
      />
    );

    expect(screen.getByText("Sida 2 av 3")).toBeInTheDocument();
    // The page count survives — it is a navigation quantity (how far you can go), and
    // `TotalPages <= MaxPage` holds by construction. Only the total claim goes.
    expect(screen.queryByText(/träffar totalt/)).toBeNull();
  });

  it("states the total by default, so a caller must opt OUT deliberately", () => {
    render(
      <JobAdPagination
        page={1}
        pageSize={20}
        totalCount={45}
        buildHref={buildHref}
      />
    );
    expect(screen.getByText(/träffar totalt/)).toBeInTheDocument();
  });

  // The total is a NUMBER, so it is grouped. ICU `{totalCount}` is plain substitution — the
  // argument has to carry `, number` or the string renders "3391" against §10's thousands rule,
  // which is what it did until #1149. Every assertion above uses a two-digit count and so cannot
  // see this: a grouping defect is invisible below 1 000.
  it("groups the thousands separator in the total", () => {
    render(
      <JobAdPagination
        page={2}
        pageSize={20}
        totalCount={3391}
        buildHref={buildHref}
      />
    );

    // Non-breaking space, per §10. The DOM normalizer folds it to a plain space for matching, so
    // assert the ungrouped form is absent too — otherwise this passes on "3391".
    expect(
      screen.getByText("Sida 2 av 170 (3 391 träffar totalt)")
    ).toBeInTheDocument();
    expect(screen.queryByText(/3391/)).toBeNull();
  });
});
