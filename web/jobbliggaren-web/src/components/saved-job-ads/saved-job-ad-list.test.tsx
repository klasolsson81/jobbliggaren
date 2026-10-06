import { StrictMode } from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { NextIntlClientProvider } from "next-intl";
import enMessages from "../../../messages/en";
import { SavedJobAdList } from "./saved-job-ad-list";
import type { SavedJobAdDto } from "@/lib/dto/saved-job-ads";

const unsaveActionMock = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn() }),
}));

vi.mock("@/lib/actions/saved-job-ads", () => ({
  unsaveJobAdAction: (...args: unknown[]) => unsaveActionMock(...args),
}));

function makeDto(
  id: string,
  jobAdId: string,
  title: string,
  withJobAd = true
): SavedJobAdDto {
  return {
    id,
    jobAdId,
    savedAt: "2026-05-23T15:00:00Z",
    jobAd: withJobAd
      ? {
          jobAdId,
          title,
          company: "Acme AB",
          url: "https://example.com/jobs/1",
          source: "Platsbanken",
          publishedAt: "2026-05-20T08:00:00Z",
          expiresAt: "2026-06-20T08:00:00Z",
        }
      : null,
  };
}

const HEADING_ID = "sparade-heading";

/** The list as the page renders it: inside `main`, under the page's focusable h1. */
function Page({ items }: { items: ReadonlyArray<SavedJobAdDto> }) {
  return (
    <main id="main" tabIndex={-1}>
      <h1 id={HEADING_ID} tabIndex={-1}>
        Sparade annonser
      </h1>
      <SavedJobAdList items={items} headingId={HEADING_ID} />
    </main>
  );
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((r) => {
    resolve = r;
  });
  return { promise, resolve };
}

const A = makeDto("s1", "j1", "Backendutvecklare");
const B = makeDto("s2", "j2", "Frontendutvecklare");
const C = makeDto("s3", "j3", "Testledare");
const ERASED = makeDto("s4", "j4", "", false);

const link = (title: string) => screen.getByRole("link", { name: title });
const trash = (title: string) =>
  screen.getByRole("button", { name: `Ta bort bokmärke för ${title}` });
const region = () => document.querySelector('p[role="status"]');

async function removeByKeyboard(button: HTMLElement) {
  button.focus();
  await userEvent.setup().keyboard("{Enter}");
}

beforeEach(() => {
  unsaveActionMock.mockReset();
});

describe("SavedJobAdList", () => {
  it("renders empty-state when items is empty", () => {
    render(<SavedJobAdList items={[]} headingId={HEADING_ID} />);
    expect(screen.getByText("Inga sparade annonser")).toBeInTheDocument();
  });

  it("renders saved jobs with title and company", () => {
    const items = [makeDto("s1", "j1", "Backendutvecklare")];
    render(<SavedJobAdList items={items} headingId={HEADING_ID} />);
    expect(screen.getByText("Backendutvecklare")).toBeInTheDocument();
    expect(screen.getByText("Acme AB")).toBeInTheDocument();
  });

  it("renders fallback when jobAd is null (soft-deletad)", () => {
    const items = [makeDto("s1", "j1", "Borttagen", false)];
    render(<SavedJobAdList items={items} headingId={HEADING_ID} />);
    expect(screen.getByText("Annonsen är borttagen")).toBeInTheDocument();
  });

  it("optimistically removes a row on successful unsave", async () => {
    unsaveActionMock.mockResolvedValue({ success: true });
    const items = [
      makeDto("s1", "j1", "Backendutvecklare"),
      makeDto("s2", "j2", "Frontendutvecklare"),
    ];
    render(<SavedJobAdList items={items} headingId={HEADING_ID} />);

    const user = userEvent.setup();
    const removeBtn = screen.getByRole("button", {
      name: /Ta bort bokmärke för Backendutvecklare/i,
    });
    await user.click(removeBtn);

    expect(unsaveActionMock).toHaveBeenCalledWith("j1");
    expect(screen.queryByText("Backendutvecklare")).not.toBeInTheDocument();
    expect(screen.getByText("Frontendutvecklare")).toBeInTheDocument();
  });

  it("shows error and keeps row when unsave fails", async () => {
    unsaveActionMock.mockResolvedValue({
      success: false,
      error: "Kunde inte ta bort bokmärket. Försök igen.",
    });
    const items = [makeDto("s1", "j1", "Backendutvecklare")];
    render(<SavedJobAdList items={items} headingId={HEADING_ID} />);

    const user = userEvent.setup();
    await user.click(
      screen.getByRole("button", {
        name: /Ta bort bokmärke för Backendutvecklare/i,
      })
    );

    expect(
      await screen.findByText(/Kunde inte ta bort bokmärket/i)
    ).toBeInTheDocument();
    expect(screen.getByText("Backendutvecklare")).toBeInTheDocument();
  });
});

// #2029 (WCAG 2.1 SC 2.4.3, 4.1.3): the remove button leaves with its row, so focus has to be put
// somewhere on purpose, and the removal has to be said.
describe("focus and status after a removal (#2029)", () => {
  it.each([
    ["without StrictMode", false],
    ["under StrictMode", true],
  ])("a removed middle row hands focus to the next row's link (%s)", async (_mode, strict) => {
    unsaveActionMock.mockResolvedValue({ success: true });
    const page = <Page items={[A, B, C]} />;
    render(strict ? <StrictMode>{page}</StrictMode> : page);

    await removeByKeyboard(trash("Frontendutvecklare"));

    expect(screen.queryByRole("link", { name: "Frontendutvecklare" })).not.toBeInTheDocument();
    expect(link("Testledare")).toHaveFocus();
  });

  it("a removed last row hands focus to the previous row's link", async () => {
    unsaveActionMock.mockResolvedValue({ success: true });
    render(<Page items={[A, B]} />);

    await removeByKeyboard(trash("Frontendutvecklare"));

    expect(link("Backendutvecklare")).toHaveFocus();
  });

  it("an erased ad's row is reached by its remove button and says the bookmark is gone", async () => {
    unsaveActionMock.mockResolvedValue({ success: true });
    render(<Page items={[A, ERASED, C]} />);

    await removeByKeyboard(trash("Backendutvecklare"));
    const erasedTrash = screen.getByRole("button", { name: "Ta bort bokmärke" });
    expect(erasedTrash).toHaveFocus();

    await removeByKeyboard(erasedTrash);
    expect(region()).toHaveTextContent("Bokmärket har tagits bort.");
    expect(link("Testledare")).toHaveFocus();
  });

  it("the last removal hands focus to the page's h1 and says so in the region that was there before it", async () => {
    unsaveActionMock.mockResolvedValue({ success: true });
    render(<Page items={[A]} />);
    const before = region();
    expect(before).toHaveTextContent("");

    await removeByKeyboard(trash("Backendutvecklare"));

    expect(screen.getByText("Inga sparade annonser")).toBeInTheDocument();
    expect(region()).toBe(before);
    expect(before).toHaveTextContent("Bokmärket har tagits bort: Backendutvecklare");
    expect(screen.getByRole("heading", { level: 1 })).toHaveFocus();
  });

  it("keeps the remove button enabled while the action runs, and a second press removes nothing more", async () => {
    const pending = deferred<{ success: true }>();
    unsaveActionMock.mockReturnValue(pending.promise);
    render(<Page items={[A, B]} />);
    const user = userEvent.setup();

    await user.click(trash("Backendutvecklare"));
    expect(trash("Backendutvecklare")).not.toBeDisabled();
    expect(trash("Backendutvecklare")).toHaveAttribute("aria-disabled", "true");
    await user.click(trash("Backendutvecklare"));
    expect(unsaveActionMock).toHaveBeenCalledTimes(1);

    await act(async () => pending.resolve({ success: true }));
  });

  it("a failed removal keeps focus on its button and announces nothing in the receipt region", async () => {
    unsaveActionMock.mockResolvedValue({
      success: false,
      error: "Kunde inte ta bort bokmärket. Försök igen.",
    });
    render(<Page items={[A, B]} />);

    await removeByKeyboard(trash("Backendutvecklare"));

    expect(screen.getByRole("alert")).toHaveTextContent("Kunde inte ta bort bokmärket.");
    expect(trash("Backendutvecklare")).toHaveFocus();
    expect(region()).toHaveTextContent("");
  });

  it("moves focus on the commit that drops the row, also when the refreshed page arrives first", async () => {
    const pending = deferred<{ success: true }>();
    unsaveActionMock.mockReturnValue(pending.promise);
    const { rerender } = render(<Page items={[A, B]} />);

    await removeByKeyboard(trash("Backendutvecklare"));
    // revalidatePath's payload, committed before the action's own result reaches the list.
    rerender(<Page items={[B]} />);
    expect(link("Frontendutvecklare")).toHaveFocus();

    await act(async () => pending.resolve({ success: true }));
    expect(region()).toHaveTextContent("Bokmärket har tagits bort: Backendutvecklare");
    expect(link("Frontendutvecklare")).toHaveFocus();
  });

  it("leaves focus where the user moved it while the removal ran", async () => {
    const pending = deferred<{ success: true }>();
    unsaveActionMock.mockReturnValue(pending.promise);
    render(<Page items={[A, B, C]} />);

    await removeByKeyboard(trash("Backendutvecklare"));
    link("Testledare").focus();
    await act(async () => pending.resolve({ success: true }));

    expect(link("Testledare")).toHaveFocus();
  });

  it("skips a row whose own removal is still running", async () => {
    const forB = deferred<{ success: true }>();
    const forA = deferred<{ success: true }>();
    unsaveActionMock.mockImplementation((jobAdId: string) =>
      jobAdId === "j2" ? forB.promise : forA.promise
    );
    render(<Page items={[A, B, C]} />);

    await removeByKeyboard(trash("Frontendutvecklare"));
    await removeByKeyboard(trash("Backendutvecklare"));
    await act(async () => forA.resolve({ success: true }));

    expect(link("Testledare")).toHaveFocus();
    await act(async () => forB.resolve({ success: true }));
  });

  it("a row whose removal failed takes focus again when its neighbour goes", async () => {
    unsaveActionMock.mockImplementation((jobAdId: string) =>
      Promise.resolve(
        jobAdId === "j1"
          ? { success: false, error: "Kunde inte ta bort bokmärket. Försök igen." }
          : { success: true }
      )
    );
    render(<Page items={[B, A]} />);

    await removeByKeyboard(trash("Backendutvecklare"));
    await removeByKeyboard(trash("Frontendutvecklare"));

    expect(link("Backendutvecklare")).toHaveFocus();
  });

  it("says the removal in English on the English page", async () => {
    unsaveActionMock.mockResolvedValue({ success: true });
    render(
      <NextIntlClientProvider locale="en" messages={enMessages} timeZone="Europe/Stockholm">
        <Page items={[A]} />
      </NextIntlClientProvider>
    );

    await removeByKeyboard(
      screen.getByRole("button", { name: "Remove bookmark for Backendutvecklare" })
    );

    expect(region()).toHaveTextContent("The bookmark has been removed: Backendutvecklare");
  });
});
