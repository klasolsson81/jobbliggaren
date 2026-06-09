// app.jsx — router/mount för JobbPilot v3
(() => {
  const { useState, useMemo, useEffect } = React;
  const { Header } = window.JpShell;
  const { JobbPage } = window.JpJobb;
  const {
    AnsokningarPage, AnsokanDetailPage, SokningarPage,
    CvPage, CvDetailPage, KontoPage, LoginPage,
  } = window.JpPages;
  const { OversiktPage } = window.JpOversikt;
  const { LandingPage } = window.JpLanding;
  const { MOCK_APPLICATIONS, MOCK_JOBS } = window.JpData;

  function App() {
    // Lightweight router: route + optional payload
    const [route, setRoute] = useState("landing");
    const [payload, setPayload] = useState(null);
    const [toast, setToast] = useState(null);

    const initialSaved = new Set(MOCK_JOBS.filter((j) => j.saved).map((j) => j.id));
    const [savedSet, setSavedSet] = useState(initialSaved);

    const [applications, setApplications] = useState(MOCK_APPLICATIONS);
    const appliedSet = useMemo(
      () => new Set(applications.map((a) => a.jobId).filter(Boolean)),
      [applications]
    );

    const navigate = (next, data = null) => {
      setRoute(next);
      setPayload(data);
      window.scrollTo({ top: 0, behavior: "instant" });
    };

    const toggleSave = (jobId) => {
      setSavedSet((s) => {
        const next = new Set(s);
        if (next.has(jobId)) {
          next.delete(jobId);
          showToast("Annons borttagen från sparade");
        } else {
          next.add(jobId);
          showToast("Annons sparad");
        }
        return next;
      });
    };

    const applyToJob = (job) => {
      if (appliedSet.has(job.id)) return;
      const id = Math.random().toString(36).slice(2, 10);
      const today = new Date().toISOString().slice(0, 10);
      setApplications((apps) => [
        {
          id, jobId: job.id, title: job.title, company: job.company,
          status: "Submitted", updated: today, published: job.published,
        },
        ...apps,
      ]);
      showToast(`Ansökan registrerad — ${job.company}`);
    };

    function showToast(text) {
      setToast(text);
      setTimeout(() => setToast(null), 2400);
    }

    // Auth-läge: ren sida utan header
    if (route === "login" || route === "landing") {
      return (
        <>
          <LandingPage onNav={navigate} />
          {toast && <div className="jp-toast">{toast}</div>}
        </>
      );
    }

    // Header-route är "oversikt" / "jobb" / "ansokningar" / "cv" / "konto"
    // Subsidor mappas till sin top-level för aktiv-markering
    const headerRoute =
      route === "ansokningar-detalj" ? "ansokningar"
      : route === "cv-detalj" ? "cv"
      : route === "sokningar" ? "" // visas via user-menyn, ingen primary highlight
      : route;

    let page;
    if (route === "oversikt") {
      page = (
        <OversiktPage
          applications={applications}
          savedSet={savedSet}
          onNav={navigate}
        />
      );
    } else if (route === "jobb") {
      page = (
        <JobbPage
          onNav={navigate}
          onApply={applyToJob}
          savedSet={savedSet}
          onToggleSave={toggleSave}
          appliedSet={appliedSet}
          toast={showToast}
        />
      );
    } else if (route === "ansokningar") {
      page = <AnsokningarPage applications={applications} onNav={navigate} />;
    } else if (route === "ansokningar-detalj") {
      page = <AnsokanDetailPage app={payload} onNav={navigate} />;
    } else if (route === "sokningar") {
      page = <SokningarPage onNav={navigate} />;
    } else if (route === "cv") {
      page = <CvPage onNav={navigate} />;
    } else if (route === "cv-detalj") {
      page = <CvDetailPage cv={payload} onNav={navigate} />;
    } else if (route === "konto") {
      page = <KontoPage onNav={navigate} />;
    } else {
      page = (
        <div className="jp-container jp-page">
          <div className="jp-empty">
            <div className="jp-empty__title">Sidan hittades inte</div>
            Ruttning saknas för "{route}".
          </div>
        </div>
      );
    }

    return (
      <div className="jp-shell">
        <Header route={headerRoute} onNav={navigate} />
        <main className="jp-content">{page}</main>
        {toast && <div className="jp-toast">{toast}</div>}
      </div>
    );
  }

  const root = ReactDOM.createRoot(document.getElementById("root"));
  root.render(<App />);
})();
