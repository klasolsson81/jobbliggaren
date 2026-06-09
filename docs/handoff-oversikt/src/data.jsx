// data.jsx — mock-data för JobbPilot v3
(() => {
  const MOCK_JOBS = [
    {
      id: "A-2851",
      title: "Backend-utvecklare (.NET / Azure)",
      company: "Folksam IT",
      location: "Stockholm",
      occupation: "Mjukvaru- och systemutvecklare",
      published: "2026-05-18",
      deadline: "2026-06-05",
      match: 92,
      saved: true,
      isNew: true,
      jobs: 1,
      description:
        "Folksam IT söker en erfaren backend-utvecklare som blir del av teamet som bygger nästa generations försäkringsplattform.\n\nDu arbetar med .NET, EF Core och Azure i en händelsedriven arkitektur. Teamet sitter i Stockholm men hybrid är möjligt två dagar i veckan.\n\nVi söker dig som har:\n• Minst fem års erfarenhet av .NET\n• God förståelse för domändriven design\n• Vana av CI/CD och produktionsdrift",
      url: "https://example.com/a-2851",
      requirements: ["C# / .NET 8", "Azure", "EF Core", "Event-driven", "Domain Driven Design"],
    },
    {
      id: "A-2849",
      title: "Senior systemutvecklare",
      company: "Skatteverket",
      location: "Solna",
      occupation: "Systemutvecklare",
      published: "2026-05-18",
      deadline: "2026-06-08",
      match: 81,
      saved: false,
      isNew: true,
      jobs: 3,
      description:
        "Skatteverket söker senior systemutvecklare till avdelningen för Inkomst och beskattning. Tjänsten är tillsvidare med placering i Solna.\n\nDu blir en del av ett tvärfunktionellt team som vidareutvecklar våra kritiska tjänster för medborgare och företag.",
      requirements: ["Java", "Kotlin", "Linux", "Kubernetes"],
    },
    {
      id: "A-2845",
      title: "Lokalvårdare (5 jobb)",
      company: "Nordic Städning & Service",
      location: "Obestämd ort",
      occupation: "Städare",
      published: "2026-05-18",
      deadline: "2026-06-15",
      match: 22,
      saved: false,
      isNew: true,
      jobs: 5,
      description:
        "Nordic Städning & Service söker engagerade lokalvårdare till vår växande verksamhet.\n\nKvälls- och dagtid finns tillgängliga. Erfarenhet är meriterande men vi lär upp rätt person.",
      requirements: ["Erfarenhet meriterande", "Truckkort meriterande"],
    },
    {
      id: "A-2841",
      title: "Yrkeslärare Försäljnings- och serviceprogrammet",
      company: "Jönköpings kommun",
      location: "Jönköping",
      occupation: "Yrkeslärare",
      published: "2026-05-13",
      deadline: "2026-05-27",
      match: 44,
      saved: false,
      isNew: false,
      jobs: 1,
      description:
        "Jönköpings kommun är en stor arbetsgivare med hundratals olika yrkesroller.\n\nVi söker en yrkeslärare med erfarenhet inom försäljning och service till gymnasieskola i centrala Jönköping.",
      requirements: ["Lärarlegitimation", "Erfarenhet av försäljning"],
    },
    {
      id: "A-2839",
      title: "Truckförare till kund — Göteborg",
      company: "Jobandtalent Sweden AB",
      location: "Göteborg",
      occupation: "Lager- och terminalarbetare",
      published: "2026-05-13",
      deadline: "2026-05-29",
      match: 64,
      saved: true,
      isNew: false,
      jobs: 4,
      description:
        "Arbetsbeskrivning: Lasta och lossa lastbilar med motviktstruck. Hantera skrymmande gods på ett säkert och effektivt sätt.\n\nKrav: Truckkort A+B, minst två års erfarenhet.",
      requirements: ["Truckkort A+B", "Erfarenhet 2+ år"],
    },
    {
      id: "A-2836",
      title: "Sjuksköterska — natt, akutmottagning",
      company: "Region Skåne",
      location: "Malmö",
      occupation: "Sjuksköterskor",
      published: "2026-05-12",
      deadline: "2026-05-30",
      match: 18,
      saved: false,
      isNew: false,
      jobs: 2,
      description:
        "Akutmottagningen i Lund söker erfaren sjuksköterska för nattjänstgöring.\n\nTjänsten omfattar 75% och utgår från modern dygnetruntverksamhet.",
      requirements: ["Sjuksköterskelegitimation", "Akutsjukvårdserfarenhet"],
    },
    {
      id: "A-2832",
      title: "Processoperatör — skiftgång",
      company: "Höganäs AB",
      location: "Höganäs",
      occupation: "Processoperatörer",
      published: "2026-05-11",
      deadline: "2026-05-25",
      match: 39,
      saved: false,
      isNew: false,
      jobs: 6,
      description:
        "Vi söker processoperatör till vår produktion av järnpulver. Skiftgång med möjlighet till utveckling i ett stabilt, etablerat bolag.",
      requirements: ["Skiftarbete", "Tekniskt intresse"],
    },
  ];

  const MOCK_APPLICATIONS = [
    {
      id: "5361d216",
      jobId: "A-2828",
      title: "Verksamhetsutvecklare",
      company: "Bonnier News",
      status: "OfferReceived",
      updated: "2026-05-18",
      published: "2026-04-22",
      nextDate: "2026-05-21",
    },
    {
      id: "f3a78c01",
      jobId: "A-2851",
      title: "Backend-utvecklare (.NET / Azure)",
      company: "Folksam IT",
      status: "InterviewScheduled",
      updated: "2026-05-17",
      published: "2026-05-18",
      nextDate: "2026-05-21 14:00",
    },
    {
      id: "ab129f44",
      jobId: "A-2849",
      title: "Senior systemutvecklare",
      company: "Skatteverket",
      status: "Submitted",
      updated: "2026-05-16",
    },
    {
      id: "9c4ef212",
      jobId: "A-2810",
      title: "Lead Engineer",
      company: "Trafikverket",
      status: "Acknowledged",
      updated: "2026-05-12",
    },
    {
      id: "69cc8a45",
      jobId: "A-2808",
      title: "Backend Engineer",
      company: "Tink",
      status: "Rejected",
      updated: "2026-05-09",
    },
    {
      id: "44ab2913",
      jobId: "A-2806",
      title: "Fullstack-utvecklare",
      company: "Polestar",
      status: "Draft",
      updated: "2026-05-15",
    },
  ];

  const STATUS_META = {
    Draft:              { sv: "Utkast",         color: "neutral" },
    Submitted:          { sv: "Skickad",        color: "info"    },
    Acknowledged:       { sv: "Bekräftad",      color: "info"    },
    InterviewScheduled: { sv: "Intervju bokad", color: "brand"   },
    Interviewing:       { sv: "I intervju",     color: "brand"   },
    OfferReceived:      { sv: "Erbjudande",     color: "success" },
    Accepted:           { sv: "Accepterad",     color: "success" },
    Rejected:           { sv: "Nekad",          color: "danger"  },
    Withdrawn:          { sv: "Återtagen",      color: "neutral" },
    Ghosted:            { sv: "Inget svar",     color: "warning" },
  };
  const STATUS_ORDER = [
    "Draft", "Submitted", "Acknowledged", "InterviewScheduled",
    "Interviewing", "OfferReceived", "Accepted", "Rejected",
    "Withdrawn", "Ghosted",
  ];

  const MOCK_CVS = [
    {
      id: "CV-04",
      name: "Backend & molnplattform",
      role: "Backend-utvecklare",
      updated: "2026-05-13",
      language: "sv",
      primary: true,
      sections: 7,
      skills: ["C#", ".NET", "Azure", "EF Core", "DDD"],
    },
    {
      id: "CV-03",
      name: "Fullstack inriktning",
      role: "Fullstack-utvecklare",
      updated: "2026-04-22",
      language: "sv",
      primary: false,
      sections: 6,
      skills: ["React", "TypeScript", "Node", ".NET"],
    },
    {
      id: "CV-02",
      name: "Internationell — English",
      role: "Backend Engineer",
      updated: "2026-03-10",
      language: "en",
      primary: false,
      sections: 7,
      skills: ["C#", "Azure", "AWS", "DDD"],
    },
  ];

  const RECENT_SEARCHES = [
    { id: "rs1", label: "Alla annonser", count: 60, isNew: true },
    { id: "rs2", label: "Systemutvecklare", count: 1, isNew: true },
    { id: "rs3", label: "Backend-utvecklare Stockholm", count: 12 },
  ];

  const SAVED_SEARCHES = [
    {
      id: "ss1",
      name: "Backend-utvecklare Stockholm",
      query: "backend",
      occupation: "Mjukvaruutvecklare",
      location: "Stockholms län",
      sortBy: "Relevans",
      count: 12,
      created: "2026-05-12",
    },
    {
      id: "ss2",
      name: "Säkerhet & DevOps Göteborg",
      query: "säkerhet",
      occupation: "IT-säkerhet",
      location: "Västra Götalands län",
      sortBy: "Nyast först",
      count: 4,
      created: "2026-05-08",
    },
    {
      id: "ss3",
      name: "Remote / Distansjobb",
      query: "remote",
      occupation: "—",
      location: "Hela Sverige",
      sortBy: "Nyast först",
      count: 23,
      created: "2026-04-30",
    },
  ];

  // Taxonomi för popovers (förenklad — speglar Platsbankens uppdelning)
  const REGIONS = [
    { id: "01", name: "Stockholms län", komm: ["Stockholm", "Solna", "Sundbyberg", "Nacka", "Huddinge", "Botkyrka"] },
    { id: "03", name: "Uppsala län", komm: ["Uppsala", "Enköping", "Knivsta"] },
    { id: "04", name: "Södermanlands län", komm: ["Nyköping", "Eskilstuna", "Strängnäs"] },
    { id: "05", name: "Östergötlands län", komm: ["Linköping", "Norrköping", "Motala"] },
    { id: "06", name: "Jönköpings län", komm: ["Jönköping", "Värnamo", "Vetlanda"] },
    { id: "07", name: "Kronobergs län", komm: ["Växjö", "Älmhult", "Ljungby"] },
    { id: "08", name: "Kalmar län", komm: ["Kalmar", "Västervik", "Oskarshamn"] },
    { id: "09", name: "Gotlands län", komm: ["Gotland"] },
    { id: "10", name: "Blekinge län", komm: ["Karlskrona", "Karlshamn", "Ronneby"] },
    { id: "12", name: "Skåne län", komm: ["Malmö", "Lund", "Helsingborg", "Kristianstad", "Trelleborg"] },
    { id: "13", name: "Hallands län", komm: ["Halmstad", "Varberg", "Falkenberg", "Kungsbacka"] },
    { id: "14", name: "Västra Götalands län", komm: ["Göteborg", "Borås", "Trollhättan", "Skövde", "Mölndal"] },
    { id: "17", name: "Värmlands län", komm: ["Karlstad", "Kristinehamn", "Arvika"] },
    { id: "18", name: "Örebro län", komm: ["Örebro", "Kumla", "Karlskoga"] },
    { id: "19", name: "Västmanlands län", komm: ["Västerås", "Köping", "Sala"] },
    { id: "20", name: "Dalarnas län", komm: ["Falun", "Borlänge", "Mora"] },
    { id: "21", name: "Gävleborgs län", komm: ["Gävle", "Sandviken", "Bollnäs"] },
    { id: "22", name: "Västernorrlands län", komm: ["Sundsvall", "Härnösand", "Örnsköldsvik"] },
    { id: "23", name: "Jämtlands län", komm: ["Östersund", "Åre", "Strömsund", "Bräcke", "Krokom"] },
    { id: "24", name: "Västerbottens län", komm: ["Umeå", "Skellefteå", "Lycksele"] },
    { id: "25", name: "Norrbottens län", komm: ["Luleå", "Piteå", "Kiruna", "Boden"] },
  ];

  const OCCUPATION_FIELDS = [
    {
      id: "of-01",
      name: "Administration, ekonomi, juridik",
      occs: ["Ekonomiassistent", "Redovisningsekonom", "HR-specialist", "Jurist"],
    },
    {
      id: "of-02",
      name: "Bygg och anläggning",
      occs: ["Snickare", "Murare", "Anläggningsarbetare", "VVS-installatör"],
    },
    {
      id: "of-03",
      name: "Chefer och verksamhetsledare",
      occs: ["IT-chef", "HR-chef", "VD", "Försäljningschef"],
    },
    {
      id: "of-04",
      name: "Data/IT",
      occs: [
        "Drifttekniker, IT",
        "IT-säkerhetsspecialister",
        "Mjukvaru- och systemutvecklare m.fl.",
        "Nätverks- och systemtekniker m.fl.",
        "Supporttekniker, IT",
        "Systemadministratörer",
        "Systemanalytiker och IT-arkitekter m.fl.",
        "Systemförvaltare m.fl.",
        "Systemtestare och testledare",
        "Utvecklare inom spel och digitala media",
      ],
    },
    {
      id: "of-05",
      name: "Försäljning, inköp, marknadsföring",
      occs: ["Säljare", "Inköpare", "Marknadschef", "Butikssäljare"],
    },
    {
      id: "of-06",
      name: "Hälso- och sjukvård",
      occs: ["Sjuksköterska", "Läkare", "Undersköterska", "Fysioterapeut"],
    },
    {
      id: "of-07",
      name: "Industriell tillverkning",
      occs: ["Processoperatör", "Maskinoperatör", "Montör", "Truckförare"],
    },
    {
      id: "of-08",
      name: "Pedagogik",
      occs: ["Lärare i grundskolan", "Förskollärare", "Yrkeslärare", "Specialpedagog"],
    },
    {
      id: "of-09",
      name: "Hotell, restaurang, storhushåll",
      occs: ["Kock", "Servitör", "Restaurangbiträde", "Bartender"],
    },
  ];

  const NOTIFICATIONS = [
    { id: "n1", text: "Skatteverket öppnade din ansökan", time: "5 min sedan" },
    { id: "n2", text: "1 ny matchning på sparad sökning Backend Sthlm", time: "2 tim sedan" },
    { id: "n3", text: "Erbjudande från Bonnier News väntar svar", time: "i går" },
  ];

  window.JpData = {
    MOCK_JOBS, MOCK_APPLICATIONS, STATUS_META, STATUS_ORDER, MOCK_CVS,
    RECENT_SEARCHES, SAVED_SEARCHES, REGIONS, OCCUPATION_FIELDS, NOTIFICATIONS,
  };
})();
