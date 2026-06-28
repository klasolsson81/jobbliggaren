// English message catalog — mirrors the sv namespace tree key-for-key. English
// copy follows a plain, direct civic register (1177 / GOV.UK): "you", no
// blame, no marketing language, no em-dash.
import admin from "./admin.json";
import aktivitetsrapport from "./aktivitetsrapport.json";
import applications from "./applications.json";
import common from "./common.json";
import contentFaq from "./content-faq.json";
import contentLegal from "./content-legal.json";
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
  aktivitetsrapport,
  applications,
  common,
  "content-faq": contentFaq,
  "content-legal": contentLegal,
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
