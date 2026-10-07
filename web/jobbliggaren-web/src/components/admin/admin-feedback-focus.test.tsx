import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { FeedbackFocusLink, FeedbackFocusReceiver, type FeedbackFocusTarget } from "./admin-feedback-focus";

// #1979 — focus after a navigation that removes the line the pressed link stood in. jsdom does not
// navigate, so a test renders the page the link opened by giving the receiver its new location.

const BASE = "/admin/feedback";
const ROW = "00000000-0000-4000-8000-000000000502";

/** jsdom would otherwise try to follow the link; listening on the window runs after React's handler. */
const stayOnPage = (event: MouseEvent) => event.preventDefault();

beforeEach(() => window.addEventListener("click", stayOnPage));
afterEach(() => window.removeEventListener("click", stayOnPage));

function Page({
  location,
  scope = false,
  rows = [],
}: {
  readonly location: string;
  readonly scope?: boolean;
  readonly rows?: ReadonlyArray<string>;
}) {
  return (
    <>
      {scope ? (
        <p tabIndex={-1} data-feedback-focus="scope">
          Sida: Jobb
        </p>
      ) : null}
      <section tabIndex={-1} data-feedback-focus="list" aria-label="Inskick">
        <ol className="jp-adminfeedback__list">
          {rows.map((id) => (
            <li key={id}>
              <a href={`${BASE}?id=${id}`} data-feedback-item={id}>
                {id}
              </a>
            </li>
          ))}
        </ol>
      </section>
      <FeedbackFocusReceiver location={location} />
    </>
  );
}

function pressLink(href: string, focusTo: FeedbackFocusTarget, init: MouseEventInit = {}) {
  const { unmount } = render(
    <FeedbackFocusLink href={href} focusTo={focusTo}>
      Länken
    </FeedbackFocusLink>,
  );
  fireEvent.click(screen.getByRole("link", { name: "Länken" }), init);
  unmount();
}

describe("FeedbackFocusLink and FeedbackFocusReceiver (#1979)", () => {
  it("moves focus to the page filter's line once the page the link named has rendered", () => {
    const { rerender } = render(<Page location={BASE} rows={[ROW]} />);
    pressLink(`${BASE}?sida=jobs`, "scope");

    rerender(<Page location={`${BASE}?sida=jobs`} scope rows={[ROW]} />);
    expect(screen.getByText("Sida: Jobb")).toHaveFocus();
  });

  it("moves focus to the row the submission was opened from, and to the list's first one when it is not on the page", () => {
    const { rerender } = render(<Page location={`${BASE}?id=${ROW}`} />);
    pressLink(BASE, `item:${ROW}`);
    rerender(<Page location={BASE} rows={["first", ROW]} />);
    expect(screen.getByRole("link", { name: ROW })).toHaveFocus();

    pressLink(`${BASE}?sidnr=2`, `item:${ROW}`);
    rerender(<Page location={`${BASE}?sidnr=2`} rows={["first", "second"]} />);
    expect(screen.getByRole("link", { name: "first" })).toHaveFocus();
  });

  it("lands on the list itself when it has no submission to focus", () => {
    const { rerender } = render(<Page location={`${BASE}?sida=jobs`} scope />);
    pressLink(BASE, "list");

    rerender(<Page location={BASE} />);
    expect(screen.getByRole("region", { name: "Inskick" })).toHaveFocus();
  });

  it("waits for the page it named, and leaves focus alone on any other", () => {
    const { rerender } = render(<Page location={BASE} rows={[ROW]} />);
    pressLink(`${BASE}?sida=jobs`, "scope");

    rerender(<Page location={`${BASE}?status=ny`} scope rows={[ROW]} />);
    expect(document.body).toHaveFocus();

    rerender(<Page location={`${BASE}?sida=jobs`} scope rows={[ROW]} />);
    expect(screen.getByText("Sida: Jobb")).toHaveFocus();
  });

  it.each([
    ["a press that opens a new tab", { ctrlKey: true }],
    ["a press with another button", { button: 1 }],
  ])("names nothing for %s", (_label, init) => {
    const { rerender } = render(<Page location={BASE} rows={[ROW]} />);
    pressLink(`${BASE}?sida=jobs`, "scope", init);

    rerender(<Page location={`${BASE}?sida=jobs`} scope rows={[ROW]} />);
    expect(document.body).toHaveFocus();
  });

  it("keeps the control that was pressed where it stays on the page", () => {
    const { rerender } = render(
      <>
        <Page location={BASE} rows={[ROW]} />
        <FeedbackFocusLink href={`${BASE}?sidnr=2`} focusTo="keep">
          Nästa
        </FeedbackFocusLink>
      </>,
    );
    const next = screen.getByRole("link", { name: "Nästa" });
    next.focus();
    fireEvent.click(next);

    rerender(
      <>
        <Page location={`${BASE}?sidnr=2`} rows={[ROW]} />
        <FeedbackFocusLink href={`${BASE}?sidnr=3`} focusTo="keep">
          Nästa
        </FeedbackFocusLink>
      </>,
    );
    expect(screen.getByRole("link", { name: "Nästa" })).toHaveFocus();
  });
});
