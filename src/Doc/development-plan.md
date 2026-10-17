# Development Plan — Prospect.AI

## Context

`src/Doc/plan.txt` describes a freelance prospecting system (.NET / Angular, remote Europe from Belgium, day rate > €500): detect assignments, qualify them with AI, score them out of 100, prepare applications, track follow-ups in a CRM and measure KPIs — always with human validation before any commercial commitment.

The `src/` repository currently contains a blank Aspire 13.6 starter: `Prospect.AI.AppHost` (Redis + server + Vite), `Prospect.AI.Server` (.NET 10 Minimal API, demo `weatherforecast` endpoint, `Extensions.cs` = OpenTelemetry/health service defaults), `frontend` (React 19 + Vite, `/api` proxy).

**Agreed decisions**
- Orchestration: **n8n** (container in Aspire) for scheduling + connectors; **.NET API** for all business logic.
- CRM: **PostgreSQL = source of truth**, qualified opportunities **synced to HubSpot** (one-way, .NET → HubSpot).
- UI: **React/Vite (web)** + **React Native / Expo (mobile)** with **full feature parity**.
- AI: **Microsoft.Extensions.AI (`IChatClient`)**, swappable provider (Anthropic by default, configurable).
- Language: **everything in English** — code, identifiers, comments, UI text, API, database, documentation, commits, n8n workflows, AI prompts and generated messages.

**Guiding principle**: automate detection and preparation; no message is ever sent without human approval; no scraping that breaches platform terms (LinkedIn via email alerts or manual URL entry only).

---

## 1. Target architecture

```
                ┌───────────── Aspire AppHost ─────────────┐
 n8n (cron, RSS, IMAP, Telegram) ──HTTP+ApiKey──► Prospect.AI.Server (Minimal API)
                                                    │  ├─ Domain (scoring, rules, pipeline)
 Web React/Vite ─┐                                  │  ├─ AI (IChatClient, JSON output)
 Mobile Expo  ───┴──Bearer (Identity)──────────────►│  ├─ EF Core → PostgreSQL
                                                    │  ├─ Redis (cache)
                                                    │  └─ HubSpot sync (outbox)
                └──────────────────────────────────────────┘
```

### Solution structure (new projects)
| Project | Role |
|---|---|
| `Prospect.AI.Domain` | Entities, value objects (`DailyRate`, `WorkMode`, `Country`), `MissionScorer`, disqualifying rules, pipeline state machine. No infrastructure dependency. |
| `Prospect.AI.Infrastructure` | `ProspectDbContext` (EF Core + Npgsql), migrations, `IChatClient` + prompts, HubSpot client, outbox, normalization/deduplication. |
| `Prospect.AI.Server` (existing) | Minimal API endpoints grouped by feature, auth, OpenAPI. Remove `weatherforecast`. |
| `Prospect.AI.Tests` | xUnit: domain (scoring), infrastructure (Testcontainers Postgres), Aspire integration (`Aspire.Hosting.Testing`). |
| `frontend/` (existing) | React web app. |
| `mobile/` | Expo (React Native) + Expo Router. |
| `packages/shared/` | Typed API client generated from OpenAPI + TanStack Query hooks + types + validation (zod) — shared by web and mobile to guarantee parity. |
| `n8n/workflows/*.json` | Exported workflows, versioned in Git. |

JS monorepo via **npm workspaces** (root `src/package.json`: `frontend`, `mobile`, `packages/shared`).

### AppHost (`Prospect.AI.AppHost/AppHost.cs`)
- `AddPostgres("pg").WithDataVolume().AddDatabase("prospectdb")` (+ pgAdmin in dev).
- Keep `AddRedis("cache")`.
- `AddContainer("n8n", "n8nio/n8n")` with a volume, port 5678, `PROSPECT_API_URL` / `PROSPECT_API_KEY` variables (Aspire secret parameter).
- Secret parameters: `ai-apikey`, `hubspot-token`, `telegram-bot-token`, `n8n-api-key`.
- Server: `.WithReference(db).WithReference(cache)` + parameters; Vite unchanged; mobile started outside Aspire (`npx expo start`) pointing at the server's external URL.

---

## 2. Data model (PostgreSQL, EF Core)

Based on section 6 of the source document, extended for traceability:

- **Source** (name, type RSS/Email/API/Manual, active, stats).
- **RawPosting** (source, canonical URL, content hash, raw payload, collected at) — keeps the original.
- **Mission** (title, URL, company, technologies[], day rate min/max + status `Announced|ToConfirm`, work mode, allowed countries[], on-site requirement, duration, start date, language, published at, status).
- **MissionAnalysis** (AI extraction JSON, model + prompt version, total score, **per-criterion breakdown**, disqualifying flags, missing requirements, priority A/B/C).
- **Company** (name, industry, country, website, source, `HubSpotId`).
- **Contact** (professional name, job title, company, contact channel, contact-details source, last contacted at, next action, `HubSpotId`).
- **Opportunity** (mission, contact, pipeline stage, proposed rate, availability, interview date, next follow-up, outcome, **NextAction + NextActionDate required while active**, `HubSpotDealId`).
- **Interaction** (opportunity, type Message/Reply/Call/Interview, content, date, direction).
- **Draft** (opportunity, template A/B/C, CV variant, subject, body, status `Pending|Approved|Rejected|Sent`, approved by/at).
- **FollowUpTask** (opportunity, due date, status; automatically cancelled when a reply is recorded).
- **CvVariant** (positioning A Backend / B Full-stack / C Modernization, file, key skills).
- **Profile** (target day rate, availability, country of residence BE, languages, skills — basis for matching).
- **OutboxMessage** (HubSpot sync, notifications).

Pipeline (enum + transitions enforced in the domain):
`ToQualify → Qualified → ToApprove → Contacted → CallScheduled → ClientInterview → Negotiation → Won | Lost`.

---

## 3. Scoring (Domain, deterministic and testable)

The AI **extracts**; the **calculation** is pure C# in `MissionScorer`:

| Criterion | Points | Calculation |
|---|---|---|
| .NET/C#/Angular fit | 25 | Overlap between mission technologies and `Profile`, weighted (core vs. bonus). |
| Compatible day rate | 25 | ≥ 550 → 25; 500–549 → partial; missing → neutral score + "to confirm" flag (never invented). |
| Remote from Belgium | 20 | BE/EU allowed + remote; occasional on-site = partial. |
| Skills & experience | 15 | AI match score (0–1) with written justification. |
| Duration & start date | 10 | 6–12 months and start date compatible with availability. |
| Posting/contact reliability | 5 | Known source, identified contact, recent posting. |

Disqualifying rules take precedence (e.g. countries not allowed, on-site outside BE, stack out of scope) → priority C forced regardless of score. Thresholds: A ≥ 80, B 65–79, C < 65. Weights and thresholds in configuration (`ScoringOptions`) for the week-4 tuning.

---

## 4. AI layer (`Infrastructure/Ai`)

- `IChatClient` registered via Microsoft.Extensions.AI; provider selected through config (`Ai:Provider`, `Ai:Model:Extraction`, `Ai:Model:Drafting`) — lightweight model for extraction, stronger model for drafts.
- Services: `MissionExtractor` (→ `ExtractedMission` via `GetResponseAsync<T>` / JSON schema), `ProfileMatcher` (→ score + justification), `DraftWriter` (templates A/B/C from section 7, personalized from the CV and the posting, never claiming experience absent from the CV), `ResponseClassifier` (positive / negative / information request).
- Prompts versioned as resource files; version + model stored in `MissionAnalysis`.
- Safeguards: validation of returned JSON, Polly retries (already via `Microsoft.Extensions.Http.Resilience`), daily cost cap, Redis cache keyed by content hash so an identical posting is never re-analyzed.

---

## 5. API (`Prospect.AI.Server`, Minimal API groups)

- **Auth**: ASP.NET Core Identity + `MapIdentityApi` (bearer tokens, works for web and mobile); single seeded user. Machine endpoints `/api/automation/*` protected by an **API key** (n8n).
- `/api/automation`: `POST ingest` (batch of raw postings → normalize, deduplicate, store), `POST qualify` (`ToQualify` missions → AI + score), `GET daily-report`, `POST followups/due`, `POST weekly-analysis`, `POST inbox/email` (parsed email/LinkedIn alerts).
- `/api/missions`: filterable/paged list (priority, score, source, status), details + analysis, `POST from-url` (manual entry), requalify, dismiss.
- `/api/opportunities`: CRUD, stage transition (validated by the domain), next action.
- `/api/drafts`: generate, edit, **approve/reject** (approval creates the Interaction + FollowUpTasks at D+5/D+10 and moves to `Contacted`).
- `/api/contacts`, `/api/companies`, `/api/interactions`, `/api/cv-variants`, `/api/profile`, `/api/sources`.
- `/api/analytics`: section 9 KPIs (relevance rate, reply rate, interview rate, conversion rate, average proposed/signed rate, time from publication to application, share of remote-from-BE, time per interview), by period and by source.
- OpenAPI exported at build time → TS client generation in `packages/shared`.

### HubSpot synchronization
- `HubSpotClient` (typed HttpClient, Private App token): upsert Company, Contact, Deal + associations; HubSpot pipeline created with the same stages.
- Triggered by the **outbox** (BackgroundService) on every creation/transition of a qualified opportunity (score ≥ 65 or manual entry); idempotent via stored `HubSpotId`s.
- One-way .NET → HubSpot in V1; a HubSpot → .NET webhook can be considered later if editing in HubSpot is needed.

---

## 6. n8n workflows (section 8 of the source document)

| Workflow | Trigger | Steps |
|---|---|---|
| A — Detection | Cron 07:00 + 13:00 | RSS / HTTP nodes (freelancermap, Free-Work, permitted feeds) + IMAP (Malt/LinkedIn alerts) → `POST /automation/ingest`. |
| B — Qualification | End of A | `POST /automation/qualify` (the API performs extraction + scoring). |
| C — Daily report | Cron 08:00 | `GET /automation/daily-report` → Telegram + email: top 5 (title, link, score, day rate, location constraints, suggested next actions). |
| D — Follow-ups | Cron 09:00 | `POST /automation/followups/due` → Telegram notification "follow-up draft ready for review" (deep link to web/mobile). No automatic sending. |
| E — Weekly analysis | Monday 08:00 | `POST /automation/weekly-analysis` → Telegram/email summary. |

Workflows exported as JSON in `n8n/workflows/` and imported at startup (`n8n import:workflow` CLI in the container, or a script).

---

## 7. Web (React/Vite) & mobile (Expo) frontends — parity

`packages/shared` holds the API client, hooks (`useMissions`, `useApproveDraft`…), types, formatters (day rate, stages, priorities) and state logic → each web/mobile screen is just a presentation layer.

Screens (identical on both platforms):
1. **Sign in**.
2. **Dashboard**: today's top missions, due tasks, key KPIs.
3. **Missions**: filterable list (priority A/B/C, source, score), details with **per-criterion score explanation**, flags, missing requirements; add from URL (mobile: **share sheet** "Share to Prospect").
4. **Pipeline**: kanban (web) / list by stage with swipe actions (mobile); next action required.
5. **Drafts**: edit, choose template & CV, approve/reject.
6. **Contacts & companies**, interaction history, record a received reply.
7. **Analytics**: section 9 KPIs, source comparison, day rates.
8. **Settings**: profile, target day rate, CVs, sources, scoring weights.

Web: React Router, TanStack Query, a UI library (e.g. Mantine or shadcn/ui), Recharts. Mobile: Expo Router, TanStack Query, Expo push notifications (relayed via the API, alongside Telegram), secure token storage (`expo-secure-store`).

---

## 8. Phases (aligned with sections 10 and 12 of the source document)

| Phase | Duration | Deliverable | Done when |
|---|---|---|---|
| **0 — Foundations** | 2–3 d | Domain/Infrastructure/Tests projects, Postgres + n8n in AppHost, EF Core + first migration, Identity auth + API key, demo code removed, GitHub Actions CI (build + tests). | `aspire run` starts everything; health checks green. |
| **1 — Collection** | 3–4 d | Source/RawPosting/Mission entities, normalization, deduplication (canonical URL + hash), `/automation/ingest`, n8n workflow A with 2–3 permitted sources. | Real postings arrive daily with no duplicates. |
| **2 — AI qualification** | 4–5 d | `IChatClient`, `MissionExtractor`, `ProfileMatcher`, `MissionScorer` + disqualifying rules (exhaustive unit tests), workflow B. | Every mission has an explained score; missing rate = "to confirm". |
| **3 — Report & CRM** | 3–4 d | Workflow C (Telegram/email), Company/Contact/Opportunity, pipeline, outbox + HubSpot sync. | Top 5 received every morning; opportunities visible in HubSpot. |
| **4 — Web UI** | 5–7 d | npm monorepo, `packages/shared` + generated client, web screens 1–8. | Full missions → opportunity → pipeline flow from the web. |
| **5 — Assisted prospecting** | 4–5 d | `DraftWriter`, CV variants, approval, Interactions, FollowUpTasks, follow-ups stopped on reply, workflow D. | An approved draft creates contact + follow-up; a reply stops the follow-up. |
| **6 — Monitoring** | 3 d | `/analytics`, `ResponseClassifier`, workflow E, analytics screen. | Section 9 KPIs computed on real data. |
| **7 — Mobile** | 7–10 d | Expo app, all screens, share sheet, push, deep links from Telegram. | Parity checked screen by screen against the web. |
| **8 — Deployment** | 2–3 d | `aspire publish` → Docker Compose (VPS); Postgres backups; secrets; EAS Build for mobile; AWS optional later. | System reachable off the local machine, mobile app installed. |

The useful MVP (phases 0–3) is enough to feed prospecting while the UI is being built. Agent autonomy is raised only after results have been measured.

---

## 9. Compliance & security
- Sources: RSS, official APIs, email alerts, manual entry — no LinkedIn scraping, no bulk sending.
- GDPR: professional contact data only, contact-details source tracked, a contact can be deleted.
- Secrets via Aspire parameters / user-secrets, never committed; n8n API key separate from the user account.

---

## 10. Verification
- **Unit** (`Prospect.AI.Tests`): `MissionScorer` (each criterion, A/B/C thresholds, each disqualifying rule, missing rate), pipeline transitions, normalization/deduplication.
- **Integration**: Testcontainers Postgres for EF/ingest; fake `IChatClient` for deterministic AI output; HubSpot mocked (WireMock.Net); `Aspire.Hosting.Testing` to start the AppHost and call `/health` + `/api/automation/ingest`.
- **Manual end-to-end**: `aspire run` → import an n8n workflow → run A+B manually in the n8n UI → check scored missions in the web app → approve a draft → see the Deal in HubSpot (sandbox) and the FollowUpTask → record a reply → the follow-up is cancelled.
- **Frontends**: `npm run lint && npm run build` (web), `npx expo start` + on-device testing; web/mobile parity checklist per screen.
- **AI evaluation**: set of ~30 real postings annotated by hand; compare AI extraction and priority against the annotations before enabling the daily report.
