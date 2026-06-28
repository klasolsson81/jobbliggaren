// Swedish message catalog — composed from one file per top-level namespace so
// parallel extraction work never writes the same file. `request.ts` and the
// typed-messages augmentation (`src/types/messages.d.ts`) both consume this
// barrel. Swedish values are the source of truth and are kept verbatim from the
// original in-component literals.
import admin from "./admin.json";
import applications from "./applications.json";
import common from "./common.json";
import contentFaq from "./content-faq.json";
import contentTips from "./content-tips.json";
import errors from "./errors.json";
import guest from "./guest.json";
import jobads from "./jobads.json";
import landing from "./landing.json";
import metadata from "./metadata.json";
import oversikt from "./oversikt.json";
import pages from "./pages.json";
import resumes from "./resumes.json";
import settings from "./settings.json";
import validation from "./validation.json";

const messages = {
  admin,
  applications,
  common,
  "content-faq": contentFaq,
  "content-tips": contentTips,
  errors,
  guest,
  jobads,
  landing,
  metadata,
  oversikt,
  pages,
  resumes,
  settings,
  validation,
};

export default messages;
