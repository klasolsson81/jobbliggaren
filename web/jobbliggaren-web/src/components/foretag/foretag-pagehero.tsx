/**
 * Shared pagehero for the /foretag surfaces (S1 #996). The v3-native banner
 * (`jp-pagehero`, the green gradient plate — ADR 0068) was inlined identically on
 * every /foretag page; this dedups it so each surface — and its `loading.tsx` —
 * renders the same banner from one place. Purely presentational: the page resolves
 * its own title/lede and passes them in.
 *
 * The `smarta-bevakningar/[id]` detail keeps its own dynamic (per-criterion)
 * pagehero and does not use this helper.
 */
export function ForetagPagehero({ title, lede }: { title: string; lede: string }) {
  return (
    <section className="jp-pagehero">
      <div className="jp-pagehero__inner">
        <div className="jp-pagehero__main">
          <h1 className="jp-pagehero__title">{title}</h1>
          <p className="jp-pagehero__lede">{lede}</p>
        </div>
      </div>
    </section>
  );
}
