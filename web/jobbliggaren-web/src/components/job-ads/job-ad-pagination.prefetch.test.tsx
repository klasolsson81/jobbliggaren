import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { JobAdPagination } from "./job-ad-pagination";

// The pager's links are next/link. jsdom never prefetches, so the prop itself is what is pinned: this stub
// renders an <a> and writes the prefetch value it was given onto it.
vi.mock("next/link", () => ({
  default: ({ href, prefetch, children, ...rest }: { href: string; prefetch?: boolean | null; children: ReactNode }) => (
    <a href={href} data-prefetch={prefetch === undefined ? "default" : String(prefetch)} {...rest}>
      {children}
    </a>
  ),
}));

const buildHref = (p: number) => `/lediga-jobb?sida=${p}`;

describe("JobAdPagination prefetch (ADR 0176 F10)", () => {
  it("passes prefetch={false} to every link when the caller turns it off", () => {
    render(<JobAdPagination page={5} pageSize={20} totalCount={400} buildHref={buildHref} prefetch={false} />);

    const links = screen.getAllByRole("link");
    expect(links.length).toBeGreaterThan(3);
    for (const link of links) expect(link).toHaveAttribute("data-prefetch", "false");
  });

  it("leaves Next's default on every link when the caller says nothing, so /jobb is unchanged", () => {
    render(<JobAdPagination page={5} pageSize={20} totalCount={400} buildHref={buildHref} />);

    for (const link of screen.getAllByRole("link")) expect(link).toHaveAttribute("data-prefetch", "default");
  });
});
