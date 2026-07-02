// Swedish message catalog — composed from one file per top-level namespace so
// parallel extraction work never writes the same file. `request.ts` and the
// typed-messages augmentation (`src/types/messages.d.ts`) both consume this
// barrel. Swedish values are the source of truth and are kept verbatim from the
// original in-component literals.
import admin from "./admin.json";
import aktivitetsrapport from "./aktivitetsrapport.json";
import applications from "./applications.json";
import common from "./common.json";
import contentCvGranskning from "./content-cv-granskning.json";
import contentFaq from "./content-faq.json";
import contentLegal from "./content-legal.json";
import contentMatchning from "./content-matchning.json";
import contentTips from "./content-tips.json";
import errors from "./errors.json";
import guest from "./guest.json";
import jobads from "./jobads.json";
import landing from "./landing.json";
import matchsetup from "./matchsetup.json";
import metadata from "./metadata.json";
import oversikt from "./oversikt.json";
import pages from "./pages.json";
import resumes from "./resumes.json";
import settings from "./settings.json";
import statistik from "./statistik.json";
import validation from "./validation.json";

const messages = {
  admin,
  aktivitetsrapport,
  applications,
  common,
  "content-cv-granskning": contentCvGranskning,
  "content-faq": contentFaq,
  "content-legal": contentLegal,
  "content-matchning": contentMatchning,
  "content-tips": contentTips,
  errors,
  guest,
  jobads,
  landing,
  matchsetup,
  metadata,
  oversikt,
  pages,
  resumes,
  settings,
  statistik,
  validation,
};

export default messages;
