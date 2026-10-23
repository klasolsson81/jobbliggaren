/**
 * The attribute that marks the occupation search field, so a host can recognise it from a document
 * listener without holding a ref into the component (#2198).
 */
export const OCCUPATION_SEARCH_FIELD_ATTRIBUTE = "data-occupation-search-field";

/**
 * For a Radix host's `onEscapeKeyDown` (design-reviewer Major 5): Escape in the search field while
 * it holds text clears the text, and must not also close the dialog and drop its unsaved draft.
 *
 * Radix listens for Escape on the document in the CAPTURE phase, so this runs before the field's
 * own key handler and still sees the text. `preventDefault` here is what keeps the host open; the
 * field then clears itself, and a second Escape on the now-empty field closes the host as before.
 */
export function keepHostOpenWhileSearchHasText(event: KeyboardEvent): void {
  const target = event.target;
  if (
    target instanceof HTMLInputElement &&
    target.hasAttribute(OCCUPATION_SEARCH_FIELD_ATTRIBUTE) &&
    target.value !== ""
  ) {
    event.preventDefault();
  }
}
