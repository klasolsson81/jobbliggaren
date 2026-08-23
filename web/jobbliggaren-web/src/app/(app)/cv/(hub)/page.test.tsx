import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svPages from "../../../../../messages/sv/pages.json";
import type { PendingParsedResumeResult } from "@/lib/api/resumes";
import type { ApiResult } from "@/lib/dto/_helpers";
import type { GetResumesResult } from "@/lib/dto/resumes";
import CvListPage from "./page";

type ResumeListResult = ApiResult<GetResumesResult>;

/**
 * /cv — hubben (#1060 PR C).
 *
 * Vad som pinnas: åtgärdskortet och tomt-tillståndet får ALDRIG renderas samtidigt.
 * De var oberoende conditionals, så en användare med ett inläst men osparat CV fick
 * "Kräver åtgärd" direkt ovanför "Inga CV ännu" — två motsägande besked om samma fil,
 * och exakt det Klas rapporterade i #1060.
 */

const redirect = vi.fn();
const getServerSession = vi.fn();
const getResumes = vi.fn<() => Promise<ResumeListResult>>();
const getLatestPendingParsedResume = vi.fn<() => Promise<PendingParsedResumeResult>>();

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: string) =>
    createTranslator({
      locale: "sv",
      messages: { pages: svPages },
      namespace: namespace as "pages" | undefined,
    }),
  getFormatter: async () => ({
    dateTime: (value: Date) =>
      new Intl.DateTimeFormat("sv-SE", {
        hour: "2-digit",
        minute: "2-digit",
      }).format(value),
  }),
}));

vi.mock("@/lib/auth/session", () => ({
  getServerSession: () => getServerSession(),
}));

vi.mock("@/lib/api/resumes", () => ({
  getResumes: () => getResumes(),
  getLatestPendingParsedResume: () => getLatestPendingParsedResume(),
}));

vi.mock("next/navigation", () => ({
  redirect: (url: string) => {
    redirect(url);
    throw new Error(`NEXT_REDIRECT:${url}`);
  },
}));

// The discard control is a client island with its own dialog; the hub test is about
// which blocks render, not about that interaction (it has its own suite).
vi.mock("@/components/resumes/discard-draft-button", () => ({
  DiscardDraftButton: () => null,
}));

const PENDING = {
  id: "11111111-1111-4111-8111-111111111111",
  sourceFileName: "cv.pdf",
  uploadedAt: "2026-07-28T09:00:00Z",
  gaps: null,
};

beforeEach(() => {
  redirect.mockClear();
  getServerSession.mockReset();
  getResumes.mockReset();
  getLatestPendingParsedResume.mockReset();
  getServerSession.mockResolvedValue({ email: "a@b.se", roles: [] });
});

// Shaped, not cast: a cast would have hidden that GetResumesResult carries paging fields, and
// a fixture that does not typecheck against the real contract is a fixture that can drift from
// it silently.
function emptyList(): ResumeListResult {
  return {
    kind: "ok",
    data: { items: [], totalCount: 0, page: 1, pageSize: 20 },
  };
}

function listWith(name: string): ResumeListResult {
  return {
    kind: "ok",
    data: {
      items: [
        {
          id: "22222222-2222-4222-8222-222222222222",
          name,
          versionCount: 1,
          createdAt: "2026-07-01T09:00:00Z",
          updatedAt: "2026-07-20T09:00:00Z",
          isPrimary: true,
          language: "Sv",
          latestRole: "Backend-utvecklare",
          sectionCount: 4,
          topSkills: ["C#"],
          openFindingCount: null,
          origin: "Import",
          template: "Standard",
        },
      ],
      totalCount: 1,
      page: 1,
      pageSize: 20,
    },
  };
}

describe("/cv — the pending card and the empty state are mutually exclusive", () => {
  it("shows the action card and SUPPRESSES the empty state when a pending artifact exists", async () => {
    getResumes.mockResolvedValue(emptyList());
    getLatestPendingParsedResume.mockResolvedValue({ kind: "ok", data: PENDING });

    render(await CvListPage());

    // The truthful block: the file exists, it is read, it is not saved.
    expect(screen.getByText("Ditt CV är inläst")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Öppna granskningen/ })).toHaveAttribute(
      "href",
      `/cv/granska/${PENDING.id}`,
    );

    // The contradiction that must be gone. "Inga CV ännu" next to "Kräver åtgärd" told the
    // user two different things about one file.
    expect(screen.queryByText("Inga CV ännu")).not.toBeInTheDocument();
    expect(screen.queryByText("Skapa första CV")).not.toBeInTheDocument();
  });

  it("still shows the empty state when the list is empty AND nothing is pending", async () => {
    // The suppression must be conditional on the pending artifact, not on the list being
    // empty — otherwise the fix would delete a legitimate empty state for every new user.
    getResumes.mockResolvedValue(emptyList());
    getLatestPendingParsedResume.mockResolvedValue({ kind: "ok", data: null });

    render(await CvListPage());

    expect(screen.getByText("Inga CV ännu")).toBeInTheDocument();
    expect(screen.queryByText("Ditt CV är inläst")).not.toBeInTheDocument();
  });

  it("still shows the empty state when the pending fetch FAILED (degrades civilly)", async () => {
    // A non-ok pending result means "we do not know", and the page already treats that as
    // "no card". The empty state must come back with it, or a transient backend blip would
    // render a hub with no content and no explanation at all.
    getResumes.mockResolvedValue(emptyList());
    getLatestPendingParsedResume.mockResolvedValue({ kind: "error" });

    render(await CvListPage());

    expect(screen.getByText("Inga CV ännu")).toBeInTheDocument();
  });

  it("renders the CV grid when the user HAS CVs, with or without a pending artifact", async () => {
    // The branch I changed and did not test (code-reviewer + test-writer, independently). The
    // new conditional is nested, so two different polarity slips hide the whole list from every
    // user who has one: `sorted.length === 0 ? null :` inverted, or the first arm collapsed back
    // to `pendingCv === null`. Every other test in this file seeds an EMPTY list, so none of
    // them can see it. Both states are asserted because both reach this branch.
    getResumes.mockResolvedValue(listWith("Mitt CV"));
    getLatestPendingParsedResume.mockResolvedValue({ kind: "ok", data: null });

    const { unmount } = render(await CvListPage());
    expect(screen.getByText("Mitt CV")).toBeInTheDocument();
    expect(screen.queryByText("Inga CV ännu")).not.toBeInTheDocument();
    unmount();

    // …and a saved CV plus a pending artifact shows both: the list is real and the pending file
    // still needs attention. Suppressing the grid here would lose the user's actual CVs.
    getResumes.mockResolvedValue(listWith("Mitt CV"));
    getLatestPendingParsedResume.mockResolvedValue({ kind: "ok", data: PENDING });

    render(await CvListPage());
    expect(screen.getByText("Mitt CV")).toBeInTheDocument();
    expect(screen.getByText("Ditt CV är inläst")).toBeInTheDocument();
    expect(screen.queryByText("Inga CV ännu")).not.toBeInTheDocument();
  });

  it("routes to the review, and no longer promises a re-upload is the only way forward", async () => {
    // The old body ended "…innan du laddar upp den igen", which presumes the fix is always in
    // the file. It is not (a personnummer in the CV NAME is fixed at the name field), and it
    // was the copy that made the reason unknowable without a second upload.
    getResumes.mockResolvedValue(emptyList());
    getLatestPendingParsedResume.mockResolvedValue({ kind: "ok", data: PENDING });

    render(await CvListPage());

    expect(
      screen.getByText(/Öppna granskningen så ser du varför/),
    ).toBeInTheDocument();
    expect(screen.queryByText(/innan du laddar upp den igen/)).not.toBeInTheDocument();
    // "vad som saknas" was my first rewrite and it was also wrong: the first gate evaluated is
    // about something that EXISTS (a personnummer), so "missing" mis-describes it before the
    // user has read a word of the reason (design-reviewer).
    expect(screen.queryByText(/vad som saknas/)).not.toBeInTheDocument();
  });
});

describe("/cv — the create-from-scratch affordances are gone (#1061)", () => {
  // The deferral is only real if NO navigation reaches the create form. These pins are the
  // /cv half; `cv/ny/page.test.tsx` pins the route half. Splitting them matters: a guard that
  // only checks the links would stay green against a bookmarked URL, which is the exact
  // "dold nav" outcome the issue exists to prevent.

  it("renders no link to /cv/ny in the page-hero when the user HAS CVs", async () => {
    getResumes.mockResolvedValue(listWith("Mitt CV"));
    getLatestPendingParsedResume.mockResolvedValue({ kind: "ok", data: null });

    render(await CvListPage());

    // Asserted on the href, not the label: renaming the button would slip past a name-based
    // check while still routing into the deferred feature.
    const toCreate = screen
      .getAllByRole("link")
      .filter((a) => a.getAttribute("href") === "/cv/ny");
    expect(toCreate).toHaveLength(0);
    expect(screen.queryByText("Nytt CV")).not.toBeInTheDocument();

    // Import survives as the hub's only entrance, and it must not be deleted along with them.
    expect(screen.getByRole("link", { name: /Importera CV/ })).toHaveAttribute(
      "href",
      "/cv/importera",
    );
  });

  it("renders no link to /cv/ny in the EMPTY state, which still offers import", async () => {
    // The empty state is the second, independent home. A fix that landed in one of the two is
    // not a fix, and this is the arm a user with no CVs actually meets.
    getResumes.mockResolvedValue(emptyList());
    getLatestPendingParsedResume.mockResolvedValue({ kind: "ok", data: null });

    render(await CvListPage());

    expect(screen.getByText("Inga CV ännu")).toBeInTheDocument();
    const toCreate = screen
      .getAllByRole("link")
      .filter((a) => a.getAttribute("href") === "/cv/ny");
    expect(toCreate).toHaveLength(0);
    expect(screen.queryByText("Skapa första CV")).not.toBeInTheDocument();

    // Two import links render in this state — the page-hero's and the empty state's own.
    // A count, not `.first()`: a first-match check would still pass if the empty block's
    // action were dropped, because the hero's would satisfy it. The count cannot say WHICH
    // two rendered, so the empty block's own action is asserted separately below.
    const toImport = screen
      .getAllByRole("link")
      .filter((a) => a.getAttribute("href") === "/cv/importera");
    expect(toImport).toHaveLength(2);
    // The empty state's action specifically — scoped by its own class, so this fails if the
    // hero's link is the only survivor.
    expect(
      document.querySelector('.jp-empty__actions a[href="/cv/importera"]'),
    ).not.toBeNull();
  });

  it("promises creation in NO prose either, in the lede or the empty body", async () => {
    // Chrome and copy are two different homes for the same false promise. Deleting the buttons
    // while the lede still advertises "eller skapa ett nytt från grunden" ships a page that
    // says in prose what the same commit removed in chrome.
    getResumes.mockResolvedValue(emptyList());
    getLatestPendingParsedResume.mockResolvedValue({ kind: "ok", data: null });

    render(await CvListPage());

    expect(screen.queryByText(/skapa ett nytt från grunden/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/Skapa ditt första CV/i)).not.toBeInTheDocument();
    // …and the replacement is present and true: import is how you get your first CV in.
    expect(screen.getByText(/Importera ditt första CV/i)).toBeInTheDocument();
  });
});


/**
 * #1383 — the hub's heading outline.
 *
 * `heading-order` is an axe BEST-PRACTICE rule, not `wcag2a`/`wcag2aa`, so a WCAG-tagged run
 * cannot fail on it and reported 0 violations the whole time the skip was live.
 *
 * ⚠ SCOPE: `render()` mounts the page WITHOUT the app shell, so this is the page's outline,
 * not the document's. The live document also carries the site footer's h2 AFTER the cards;
 * a jump back up is never a skip, which is why the two readings agree. The document-level
 * property is verified by an axe `best-practice` run, not here.
 */
function outline(): number[] {
  return screen.getAllByRole("heading").map((el) => {
    // Tag-derived, so an ARIA-only heading would yield NaN and pass every comparison in
    // firstSkip silently. Fail loud instead: this helper may not quietly measure nothing.
    const level = Number(el.tagName.slice(1));
    if (!Number.isInteger(level)) {
      throw new Error(`outline(): <${el.tagName}> has no tag-derived heading level`);
    }
    return level;
  });
}

/** The first skipped level, as a readable string — or null when the outline is sound.
 *  A fold, not an index walk: `noUncheckedIndexedAccess` types `levels[i]` as possibly
 *  undefined, so an index walk needs a per-pair guard, and the obvious one skips the
 *  comparison rather than making it. A fold has no index to be unsure about. */
function firstSkip(levels: number[]): string | null {
  const [first, ...rest] = levels;
  if (first === undefined) return null;
  let prev = first;
  for (const [i, here] of rest.entries()) {
    if (here > prev + 1) return `h${prev} -> h${here} at position ${i + 1}`;
    prev = here;
  }
  return null;
}

describe("/cv — the heading outline skips no level (WCAG 1.3.1, #1383)", () => {
  it("goes h1 -> h2 -> h3 when the grid renders, and the h2 is the list's own", async () => {
    getResumes.mockResolvedValue(listWith("Mitt CV"));
    getLatestPendingParsedResume.mockResolvedValue({ kind: "ok", data: null });

    render(await CvListPage());

    expect(firstSkip(outline())).toBeNull();
    // The levels are NAMED, not merely counted. A bare skip check also passes the wrong fix,
    // where the card is promoted to h2 and the outline reads [1,2,2] with no skip in it.
    expect(
      screen.getByRole("heading", { level: 2, name: "Sparade CV" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 3, name: "Mitt CV" }),
    ).toBeInTheDocument();
  });

  it("keeps both sections labelled when the pending card and the grid render together", async () => {
    getResumes.mockResolvedValue(listWith("Mitt CV"));
    getLatestPendingParsedResume.mockResolvedValue({ kind: "ok", data: PENDING });

    render(await CvListPage());

    expect(firstSkip(outline())).toBeNull();
    expect(
      screen.getByRole("heading", { level: 2, name: "Ditt CV är inläst" }),
    ).toBeInTheDocument();
    // By ROLE AND NAME, never by the attribute: a selector for `[aria-labelledby="x"]` passes
    // even when the id it points at is gone — and that is exactly the state where the region
    // loses its name and stops being a landmark, which is what these two lines exist to pin.
    expect(screen.getByRole("region", { name: "Ditt CV är inläst" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Sparade CV" })).toBeInTheDocument();
  });

  it("skips no level in the two states that render no grid", async () => {
    // Pending only: the list heading must not render without a list to head.
    getResumes.mockResolvedValue(emptyList());
    getLatestPendingParsedResume.mockResolvedValue({ kind: "ok", data: PENDING });

    const { unmount } = render(await CvListPage());
    expect(firstSkip(outline())).toBeNull();
    // Positive first: `firstSkip([])` is null, so the two negations below would also pass a
    // page that rendered no headings at all.
    expect(
      screen.getByRole("heading", { level: 2, name: "Ditt CV är inläst" }),
    ).toBeInTheDocument();
    expect(screen.queryByText("Sparade CV")).not.toBeInTheDocument();
    unmount();

    // `.jp-empty__title` stays a div. It is visually a heading and carries a 1.3.1 exposure of
    // its own, but the class is shared by every empty state in the tree, so promoting it here
    // would be a fix in one place out of N. Out of scope: this state skips nothing. Count the
    // homes with (the filter is load-bearing — without it this file, which names the class
    // in this very comment, counts itself):
    //   grep -rl 'jp-empty__title' web/jobbliggaren-web/src --include=*.tsx | grep -v '\.test\.'
    getResumes.mockResolvedValue(emptyList());
    getLatestPendingParsedResume.mockResolvedValue({ kind: "ok", data: null });

    render(await CvListPage());
    expect(firstSkip(outline())).toBeNull();
    expect(screen.getByRole("heading", { level: 1, name: "CV" })).toBeInTheDocument();
  });
});
