// jobb.jsx — Sök-hero, filter-popovers, resultatlista
(() => {
  const { useState, useRef, useMemo, useEffect } = React;
  const I = window.JpIcons;
  const { MOCK_JOBS, REGIONS, OCCUPATION_FIELDS, RECENT_SEARCHES } = window.JpData;
  const useOutside = window.useJpOutside;

  // ── Filter popover: två kolumner ──────────────────
  function FilterPopover({ open, anchor, title, leftItems, rightFor, rightLabel, allLabelTemplate, selected, onChange, onClose, onClear }) {
    const [activeLeft, setActiveLeft] = useState(leftItems[0]?.id || null);
    const ref = useOutside(open, onClose);

    useEffect(() => {
      if (open && leftItems[0]) setActiveLeft(leftItems[0].id);
    }, [open]);

    if (!open) return null;
    const right = rightFor(activeLeft) || [];
    const activeItem = leftItems.find((it) => it.id === activeLeft);

    const toggle = (val) => {
      const next = selected.includes(val)
        ? selected.filter((v) => v !== val)
        : [...selected, val];
      onChange(next);
    };

    const allSelectedInGroup = right.length > 0 && right.every((v) => selected.includes(v));
    const toggleAll = () => {
      if (allSelectedInGroup) {
        onChange(selected.filter((v) => !right.includes(v)));
      } else {
        const next = [...selected];
        right.forEach((v) => { if (!next.includes(v)) next.push(v); });
        onChange(next);
      }
    };
    const clearGroup = () => {
      onChange(selected.filter((v) => !right.includes(v)));
    };

    const r = anchor?.getBoundingClientRect();
    const top = r ? r.bottom + 8 + window.scrollY : 200;
    const left = r ? r.left + window.scrollX : 200;

    const allLabel = allLabelTemplate
      ? allLabelTemplate.replace("{X}", activeItem?.name || "")
      : `Välj alla`;

    return (
      <div
        ref={ref}
        className="jp-popover"
        style={{ top, left, width: 580 }}
      >
        <div className="jp-popover__body">
          <div className="jp-popover__col" role="listbox">
            <div className="jp-popover__colhead">
              <span className="jp-popover__title">{title}</span>
              {selected.length > 0 && (
                <button className="jp-popover__clear" onClick={onClear}>Rensa</button>
              )}
            </div>
            {leftItems.map((it) => {
              const hasSel = it.occs
                ? it.occs.some((o) => selected.includes(o))
                : it.komm?.some((k) => selected.includes(k));
              const active = activeLeft === it.id;
              return (
                <div
                  key={it.id}
                  className="jp-popover-row"
                  role="option"
                  aria-selected={active}
                  onClick={() => setActiveLeft(it.id)}
                >
                  <span style={{ display: "flex", alignItems: "center", gap: 8 }}>
                    {hasSel && !active && (
                      <span
                        style={{
                          width: 8, height: 8, borderRadius: 999,
                          background: "var(--jp-leaf-600)",
                        }}
                      />
                    )}
                    {it.name}
                  </span>
                  <I.ChevronRight size={14} className="jp-popover-row__chev" />
                </div>
              );
            })}
          </div>
          <div className="jp-popover__col">
            <div className="jp-popover__colhead">
              <span className="jp-popover__title">{rightLabel || "Val"}</span>
              {right.some((v) => selected.includes(v)) && (
                <button className="jp-popover__clear" onClick={clearGroup}>Rensa</button>
              )}
            </div>
            {right.length === 0 ? (
              <div style={{ padding: "12px 16px", color: "var(--jp-ink-2)", fontSize: 14 }}>
                Välj en kategori till vänster.
              </div>
            ) : (
              <>
                <div
                  className="jp-checkitem jp-checkitem--all"
                  role="checkbox"
                  aria-checked={allSelectedInGroup}
                  onClick={toggleAll}
                >
                  <span className="jp-checkitem__box">
                    {allSelectedInGroup && <I.Check size={14} />}
                  </span>
                  {allLabel}
                </div>
                {right.map((val) => {
                  const checked = selected.includes(val);
                  return (
                    <div
                      key={val}
                      className="jp-checkitem"
                      role="checkbox"
                      aria-checked={checked}
                      onClick={() => toggle(val)}
                    >
                      <span className="jp-checkitem__box">
                        {checked && <I.Check size={14} />}
                      </span>
                      {val}
                    </div>
                  );
                })}
              </>
            )}
          </div>
        </div>
      </div>
    );
  }

  // Senaste sökningar / Sparade annonser dropdown chip
  function HeroChip({ icon, label, count, onClick, items, onNav }) {
    const [open, setOpen] = useState(false);
    const ref = useOutside(open, () => setOpen(false));
    return (
      <div ref={ref} style={{ position: "relative" }}>
        <button
          className="jp-hero-chip"
          aria-expanded={open}
          onClick={() => setOpen((v) => !v)}
        >
          {icon} {label}
          {count != null && <span className="jp-hero-chip__count">({count})</span>}
          <I.ChevronDown size={14} />
        </button>
        {open && (
          <div
            className="jp-popover"
            style={{ top: "calc(100% + 6px)", right: 0, width: 320, color: "var(--jp-ink-1)" }}
          >
            <div className="jp-popover__head">
              <span className="jp-popover__title">{label}</span>
            </div>
            <div style={{ padding: "6px 0", maxHeight: 320, overflow: "auto" }}>
              {items.length === 0 ? (
                <div style={{ padding: "14px 16px", color: "var(--jp-ink-2)", fontSize: 14 }}>
                  {label === "Sparade annonser"
                    ? "När du sparar en annons visas den här."
                    : "Inga senaste sökningar."}
                </div>
              ) : items.map((it) => (
                <a
                  key={it.id}
                  href="#"
                  onClick={(e) => { e.preventDefault(); onClick && onClick(it); setOpen(false); }}
                  style={{
                    display: "flex", alignItems: "center", justifyContent: "space-between",
                    padding: "10px 16px", textDecoration: "none", color: "var(--jp-ink-1)",
                    fontSize: 14.5, gap: 12,
                  }}
                  onMouseOver={(e) => e.currentTarget.style.background = "var(--jp-surface-3)"}
                  onMouseOut={(e) => e.currentTarget.style.background = "transparent"}
                >
                  <span>{it.label || it.title}</span>
                  <span style={{
                    display: "inline-flex", alignItems: "center", gap: 6,
                    fontFamily: "var(--jp-font-mono)", fontSize: 12, color: "var(--jp-ink-2)",
                  }}>
                    {it.isNew && (
                      <span style={{
                        background: "var(--jp-leaf-50)", color: "var(--jp-leaf-600)",
                        padding: "1px 6px", borderRadius: 4, fontWeight: 700,
                      }}>NY</span>
                    )}
                    {it.count != null && <span>({it.count})</span>}
                  </span>
                </a>
              ))}
            </div>
          </div>
        )}
      </div>
    );
  }

  // ── Job row ──────────────────────────────────────
  function JobRow({ job, savedSet, onOpen, onToggleSave }) {
    const saved = savedSet.has(job.id);
    const match = job.match;
    const matchClass =
      match >= 75 ? "jp-matchchip--high"
      : match < 40 ? "jp-matchchip--low"
      : "jp-matchchip--mid";
    return (
      <article className="jp-job" onClick={() => onOpen(job)}>
        <div className="jp-job__body">
          <h3 className="jp-job__title">
            {job.isNew && <span className="jp-job__newflag">Ny</span>}
            <span>{job.title}</span>
          </h3>
          <div className="jp-job__company">{job.company}</div>
          <div className="jp-job__meta">
            <span style={{ display: "inline-flex", alignItems: "center", gap: 6 }}>
              <I.MapPin size={13} /> {job.location}
            </span>
            <span>{job.occupation}</span>
            <span>Publicerad <b>{job.published}</b></span>
            <span>Sista ansökan <b>{job.deadline}</b></span>
          </div>
        </div>
        <div className="jp-job__actions" onClick={(e) => e.stopPropagation()}>
          <button
            className="jp-save"
            data-saved={saved}
            aria-pressed={saved}
            onClick={() => onToggleSave(job.id)}
          >
            {saved ? <I.BookmarkFill size={14} /> : <I.Bookmark size={14} />}
            {saved ? "Sparad" : "Spara"}
          </button>
        </div>
      </article>
    );
  }

  // ── Job detail modal ─────────────────────────────
  function JobModal({ job, onClose, savedSet, onToggleSave, onApply, alreadyApplied }) {
    useEffect(() => {
      if (!job) return;
      const onKey = (e) => { if (e.key === "Escape") onClose(); };
      document.addEventListener("keydown", onKey);
      document.body.style.overflow = "hidden";
      return () => {
        document.removeEventListener("keydown", onKey);
        document.body.style.overflow = "";
      };
    }, [job]);
    if (!job) return null;
    const saved = savedSet.has(job.id);
    const matchColor = job.match >= 75 ? "var(--jp-success)"
      : job.match < 40 ? "var(--jp-ink-3)"
      : "var(--jp-navy-700)";
    const matchBg = job.match >= 75 ? "var(--jp-success-bg)"
      : job.match < 40 ? "var(--jp-surface-3)"
      : "var(--jp-navy-50)";
    const matchExpl = job.match >= 75
      ? "Stark matchning. Annonsens krav stämmer väl mot ditt valda CV."
      : job.match >= 40
      ? "Delvis matchning. Vissa krav saknas — överväg att anpassa CV:t innan du skickar."
      : "Svag matchning. Annonsen kräver kompetenser som inte finns i ditt CV.";
    return (
      <div
        className="jp-modal-scrim"
        role="dialog"
        aria-modal="true"
        aria-label={job.title}
        onClick={onClose}
      >
        <div className="jp-modal" onClick={(e) => e.stopPropagation()}>
          <div className="jp-modal__head">
            <div style={{ flex: 1 }}>
              <h2 className="jp-modal__title">{job.title}</h2>
              <p className="jp-modal__company">
                {job.company} · {job.location}
              </p>
            </div>
            <button className="jp-icon-btn" aria-label="Stäng" onClick={onClose}>
              <I.X size={20} />
            </button>
          </div>
          <div className="jp-modal__body">
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: 16,
                padding: "14px 16px",
                background: "var(--jp-surface-2)",
                border: "1px solid var(--jp-border-soft)",
                borderRadius: "var(--jp-r-md)",
              }}
            >
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{
                  fontSize: 12, color: "var(--jp-ink-2)",
                  textTransform: "uppercase", letterSpacing: "0.06em",
                  fontWeight: 700, marginBottom: 6,
                }}>
                  Match mot ditt CV
                </div>
                <div style={{
                  display: "flex", alignItems: "baseline", gap: 12,
                }}>
                  <span style={{
                    fontFamily: "var(--jp-font-mono)", fontSize: 20,
                    fontWeight: 700, color: matchColor, lineHeight: 1,
                  }}>
                    {job.match}% match
                  </span>
                </div>
                <div style={{ marginTop: 8, fontSize: 13.5, color: "var(--jp-ink-2)", lineHeight: 1.5 }}>
                  {matchExpl}
                </div>
              </div>
            </div>

            <dl className="jp-modal__metarow">
              <div className="jp-modal__metaitem">
                <dt>Yrkesområde</dt>
                <dd style={{ fontFamily: "inherit", fontWeight: 500 }}>{job.occupation}</dd>
              </div>
              <div className="jp-modal__metaitem">
                <dt>Publicerad</dt>
                <dd>{job.published}</dd>
              </div>
              <div className="jp-modal__metaitem">
                <dt>Sista ansökan</dt>
                <dd>{job.deadline}</dd>
              </div>
              <div className="jp-modal__metaitem">
                <dt>Annons-ID</dt>
                <dd>{job.id}</dd>
              </div>
            </dl>

            {job.requirements && job.requirements.length > 0 && (
              <div>
                <div style={{
                  fontSize: 13, fontWeight: 700, textTransform: "uppercase",
                  letterSpacing: "0.06em", color: "var(--jp-ink-2)", marginBottom: 8,
                }}>
                  Krav & meriter
                </div>
                <div style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
                  {job.requirements.map((r) => (
                    <span key={r} style={{
                      padding: "5px 10px",
                      background: "var(--jp-surface-2)",
                      border: "1px solid var(--jp-border)",
                      borderRadius: 6,
                      fontSize: 13,
                    }}>
                      {r}
                    </span>
                  ))}
                </div>
              </div>
            )}

            <div>
              <div style={{
                fontSize: 13, fontWeight: 700, textTransform: "uppercase",
                letterSpacing: "0.06em", color: "var(--jp-ink-2)", marginBottom: 8,
              }}>
                Annonsbeskrivning
              </div>
              <div className="jp-modal__description">{job.description}</div>
            </div>
          </div>
          <div className="jp-modal__foot">
            <a
              href={job.url || "#"}
              target="_blank"
              rel="noopener noreferrer"
              className="jp-btn jp-btn--secondary"
            >
              <I.ExternalLink size={14} /> Öppna annonsen
            </a>
            <span className="jp-modal__foot__spacer" />
            <button
              className="jp-btn jp-btn--secondary"
              onClick={() => onToggleSave(job.id)}
            >
              {saved ? <I.BookmarkFill size={14} /> : <I.Bookmark size={14} />}
              {saved ? "Sparad" : "Spara annons"}
            </button>
            <button
              className="jp-btn jp-btn--primary"
              disabled={alreadyApplied}
              onClick={() => onApply(job)}
            >
              {alreadyApplied ? <><I.Check size={14} /> Redan ansökt</> : <><I.Send size={14} /> Har ansökt</>}
            </button>
          </div>
        </div>
      </div>
    );
  }

  // ── Page ─────────────────────────────────────────
  function JobbPage({ onNav, onApply, savedSet, onToggleSave, appliedSet, toast }) {
    const [q, setQ] = useState("");
    const [openPop, setOpenPop] = useState(null); // 'ort' | 'yrke' | 'filter' | null
    const ortBtnRef = useRef(null);
    const yrkeBtnRef = useRef(null);
    const filterBtnRef = useRef(null);

    const [komm, setKomm] = useState([]);
    const [yrken, setYrken] = useState([]);
    const [sort, setSort] = useState("Relevans");
    const [openJob, setOpenJob] = useState(null);

    const visible = useMemo(() => {
      let list = [...MOCK_JOBS];
      if (q.trim().length > 0) {
        const t = q.toLowerCase();
        list = list.filter(
          (j) => j.title.toLowerCase().includes(t) ||
                 j.company.toLowerCase().includes(t) ||
                 j.occupation.toLowerCase().includes(t)
        );
      }
      if (komm.length > 0) list = list.filter((j) => komm.includes(j.location));
      if (yrken.length > 0) list = list.filter((j) =>
        yrken.some((y) => j.occupation.toLowerCase().includes(y.toLowerCase().split(" ")[0]))
      );
      if (sort === "Relevans") list.sort((a, b) => b.match - a.match);
      else if (sort === "Nyast") list.sort((a, b) => b.published.localeCompare(a.published));
      else if (sort === "Sista ansökan") list.sort((a, b) => a.deadline.localeCompare(b.deadline));
      return list;
    }, [q, komm, yrken, sort]);

    const removeKomm = (k) => setKomm(komm.filter((x) => x !== k));
    const removeYrke = (y) => setYrken(yrken.filter((x) => x !== y));

    const savedJobs = MOCK_JOBS.filter((j) => savedSet.has(j.id));

    return (
      <>
        <section className="jp-hero">
          <div className="jp-hero__inner">
            <div className="jp-hero__topbar">
              <HeroChip
                icon={<I.Clock size={14} />}
                label="Senaste sökningar"
                items={RECENT_SEARCHES}
              />
              <HeroChip
                icon={<I.BookmarkFill size={14} />}
                label="Sparade annonser"
                count={savedJobs.length || null}
                items={savedJobs.map((j) => ({ id: j.id, label: j.title, count: null }))}
                onClick={(it) => {
                  const j = MOCK_JOBS.find((x) => x.id === it.id);
                  if (j) setOpenJob(j);
                }}
              />
            </div>

            <h1 className="jp-hero__title">Sök bland aktiva annonser</h1>
            <p className="jp-hero__lede">
              Sök bland aktiva annonser. Filtrera, jämför mot ditt CV och spara dina favoriter.
            </p>

            <div className="jp-hero__searchblock">
              <div className="jp-hero__searchlabels">Sök på ett eller flera ord</div>
              <div className="jp-hero__searchrow">
                <input
                  className="jp-hero__input"
                  type="search"
                  value={q}
                  onChange={(e) => setQ(e.target.value)}
                  placeholder="t.ex. backend Stockholm"
                  aria-label="Sökord"
                />
                <button className="jp-hero__searchbtn">
                  <I.Search size={18} /> Sök
                </button>
              </div>

              <div className="jp-hero__pills">
                <button
                  ref={ortBtnRef}
                  className="jp-hero-pill"
                  data-active={openPop === "ort" || komm.length > 0}
                  onClick={() => setOpenPop(openPop === "ort" ? null : "ort")}
                >
                  {komm.length > 0 && <span className="jp-hero-pill__dot" />}
                  Ort
                  {komm.length > 0 && <span className="jp-hero-pill__count">{komm.length}</span>}
                  <I.ChevronDown size={14} />
                </button>
                <button
                  ref={yrkeBtnRef}
                  className="jp-hero-pill"
                  data-active={openPop === "yrke" || yrken.length > 0}
                  onClick={() => setOpenPop(openPop === "yrke" ? null : "yrke")}
                >
                  {yrken.length > 0 && <span className="jp-hero-pill__dot" />}
                  Yrke
                  {yrken.length > 0 && <span className="jp-hero-pill__count">{yrken.length}</span>}
                  <I.ChevronDown size={14} />
                </button>
                <button
                  ref={filterBtnRef}
                  className="jp-hero-pill"
                  data-active={openPop === "filter"}
                  onClick={() => setOpenPop(openPop === "filter" ? null : "filter")}
                >
                  <I.Filter size={14} /> Filter <I.ChevronDown size={14} />
                </button>
              </div>
            </div>
          </div>
        </section>

        <FilterPopover
          open={openPop === "ort"}
          anchor={ortBtnRef.current}
          title="Län"
          rightLabel="Kommuner"
          allLabelTemplate="Välj alla kommuner"
          leftItems={REGIONS}
          rightFor={(id) => REGIONS.find((r) => r.id === id)?.komm}
          selected={komm}
          onChange={setKomm}
          onClose={() => setOpenPop(null)}
          onClear={() => setKomm([])}
        />
        <FilterPopover
          open={openPop === "yrke"}
          anchor={yrkeBtnRef.current}
          title="Yrkesområde"
          rightLabel="Yrken"
          allLabelTemplate="Välj alla yrken"
          leftItems={OCCUPATION_FIELDS}
          rightFor={(id) => OCCUPATION_FIELDS.find((o) => o.id === id)?.occs}
          selected={yrken}
          onChange={setYrken}
          onClose={() => setOpenPop(null)}
          onClear={() => setYrken([])}
        />
        {openPop === "filter" && (
          <FilterDrawer
            anchor={filterBtnRef.current}
            sort={sort}
            onSort={setSort}
            onClose={() => setOpenPop(null)}
          />
        )}

        <div className="jp-container jp-page">
          <div className="jp-results-toolbar">
            <div>
              <div className="jp-results-count">
                <b>{visible.length}</b> {visible.length === 1 ? "träff" : "träffar"}
              </div>
              {(komm.length > 0 || yrken.length > 0) && (
                <div className="jp-filterchips">
                  {komm.map((k) => (
                    <span key={k} className="jp-filterchip">
                      <I.MapPin size={12} /> {k}
                      <button className="jp-filterchip__rm" onClick={() => removeKomm(k)} aria-label={`Ta bort ${k}`}>
                        <I.X size={12} />
                      </button>
                    </span>
                  ))}
                  {yrken.map((y) => (
                    <span key={y} className="jp-filterchip">
                      <I.Briefcase size={12} /> {y}
                      <button className="jp-filterchip__rm" onClick={() => removeYrke(y)} aria-label={`Ta bort ${y}`}>
                        <I.X size={12} />
                      </button>
                    </span>
                  ))}
                </div>
              )}
            </div>
            <div style={{ display: "flex", gap: 12, alignItems: "center" }}>
              <span style={{ fontSize: 14, color: "var(--jp-ink-2)" }}>Sortera</span>
              <select
                className="jp-select"
                style={{ height: 40, width: "auto", minWidth: 180 }}
                value={sort}
                onChange={(e) => setSort(e.target.value)}
              >
                <option value="Relevans">Mest relevant (CV-match)</option>
                <option value="Nyast">Nyast först</option>
                <option value="Sista ansökan">Sista ansökan</option>
              </select>
            </div>
          </div>

          <div className="jp-jobs" style={{ marginTop: 18 }}>
            {visible.length === 0 ? (
              <div className="jp-empty">
                <div className="jp-empty__title">Inga träffar</div>
                Justera filter eller töm sökrutan för att se fler annonser.
              </div>
            ) : visible.map((j) => (
              <JobRow
                key={j.id}
                job={j}
                savedSet={savedSet}
                onOpen={setOpenJob}
                onToggleSave={onToggleSave}
              />
            ))}
          </div>
        </div>

        <JobModal
          job={openJob}
          onClose={() => setOpenJob(null)}
          savedSet={savedSet}
          onToggleSave={onToggleSave}
          alreadyApplied={openJob ? appliedSet.has(openJob.id) : false}
          onApply={(j) => { onApply(j); setOpenJob(null); }}
        />
      </>
    );
  }

  // ── Filter "drawer" (övriga filter — anställning, omfattning) ─
  function FilterDrawer({ anchor, onClose }) {
    const ref = useOutside(true, onClose);
    const r = anchor?.getBoundingClientRect();
    const top = r ? r.bottom + 8 + window.scrollY : 200;
    const left = r ? r.left + window.scrollX : 200;
    const [omfattning, setOmfattning] = useState([]);
    const [empType, setEmpType] = useState([]);
    const [workplace, setWorkplace] = useState([]);
    const [published, setPublished] = useState([]);

    const toggle = (arr, setArr, val) => {
      setArr(arr.includes(val) ? arr.filter((x) => x !== val) : [...arr, val]);
    };

    const Group = ({ label, options, value, onChange }) => (
      <div className="jp-popover__group">
        <div className="jp-popover__colhead">
          <span className="jp-popover__title">{label}</span>
          {value.length > 0 && (
            <button className="jp-popover__clear" onClick={() => onChange([])}>Rensa</button>
          )}
        </div>
        {options.map((v) => {
          const on = value.includes(v);
          return (
            <div
              key={v}
              className="jp-checkitem"
              role="checkbox"
              aria-checked={on}
              onClick={() => toggle(value, onChange, v)}
            >
              <span className="jp-checkitem__box">{on && <I.Check size={14} />}</span>
              {v}
            </div>
          );
        })}
      </div>
    );

    return (
      <div ref={ref} className="jp-popover" style={{ top, left, width: 580 }}>
        <div className="jp-popover__body">
          <div className="jp-popover__col">
            <Group
              label="Omfattning"
              options={["Heltid", "Deltid"]}
              value={omfattning}
              onChange={setOmfattning}
            />
            <Group
              label="Anställningsform"
              options={["Tillsvidare", "Tidsbegränsad", "Vikariat", "Sommarjobb", "Praktik"]}
              value={empType}
              onChange={setEmpType}
            />
          </div>
          <div className="jp-popover__col">
            <Group
              label="Arbetsplats"
              options={["Möjlighet till distansarbete", "Öppen för alla"]}
              value={workplace}
              onChange={setWorkplace}
            />
            <Group
              label="Publicerad"
              options={["Idag", "Senaste 7 dagarna", "Senaste 30 dagarna"]}
              value={published}
              onChange={setPublished}
            />
          </div>
        </div>
      </div>
    );
  }
  window.JpJobb = { JobbPage };
})();
