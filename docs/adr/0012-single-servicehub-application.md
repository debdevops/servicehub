# ADR-0012: Archive the 4.0.0 frontend; ServiceHub is one application

**Status:** Accepted — 2026-09-21, by explicit owner sign-off, recorded the same way ADR-0006
through ADR-0011 each were.

**Relates to:** [ADR-0003](0003-single-instance-sqlite.md) (single-instance),
[ADR-0004](0004-self-hosted-security-model.md) (self-hosted only),
[ADR-0005](0005-ai-capability-boundary.md) (AI capability boundary).
**Does not modify:** any accepted ADR.

---

## Context

ServiceHub 4.0.0's frontend is 53,783 lines across 33 pages in `apps/web`, with 26 navigation
destinations in six groups. The product behind it is complete and correct. The *presentation* is
not: in the owner's own words, *"today the product is much more complex, menus are available, user
will definitely [be] confused — sometimes I could not understand [it]."*

The decision has been taken to simplify it to eight screens and **keep one name: ServiceHub**. The
33-page surface remains reachable through a **ServiceHub 4.0** control while it is still needed, and
is retired afterwards. (The old surface is called "ServiceHub 4.0" — never "Legacy", never
"Classic". **Advanced** is a different thing: a permanent section of the new product.)

Three constraints govern how this is built, all stated by the owner:

1. **Archive the existing frontend into its own folder. Nothing in it is ever edited.** It is
   reference material.
2. **Maintain exactly one application going forward** — ServiceHub. Not a light edition and an
   advanced edition; one product.
3. **Keep the serving change as simple as possible.**

Two constraints come from the tree itself:

4. **The data layer is already separate from the UI.** All 29 API modules and 35 React Query hooks
   live in `packages/servicehub-ui-shared`; `apps/web` holds presentation only. A new app inherits
   the entire tested data layer for the cost of an import.
5. **The engine is not being replaced.** `services/api` — 33 controllers, 20 background workers, 31
   entities, the autonomy engine, the evidence ledger — is the background that must stay rock
   solid. It is not touched by any of this.

## Decision

### D1 — The 4.0.0 frontend is archived, frozen, and never edited

`apps/web` moves to **`archive/servicehub-web-4.0.0/`**, unchanged, as a complete working copy.

```
servicehub/
├── apps/
│   ├── servicehub/                  ← THE product. The only application maintained.
│   ├── demo/                        ← unchanged (experimental)
│   └── sandbox/                     ← unchanged (experimental)
├── archive/
│   ├── README.md                    ← FROZEN banner + how to read it as reference
│   └── servicehub-web-4.0.0/        ← the former apps/web, byte-identical
├── packages/
│   └── servicehub-ui-shared/        ← the shared data layer (unchanged)
└── services/
    ├── api/                         ← the engine — NOT archived, NOT changed
    └── agent/  ai/                  ← unchanged
```

**`archive/` sits at the same depth as `apps/`**, deliberately: every relative path inside the moved
app (`../../.version`, `../../packages/servicehub-ui-shared/src`,
`../../services/api/src/ServiceHub.Api/wwwroot`) keeps resolving without edit. The move is a `git
mv` plus the mechanical repointing of references outside the folder (see "What moving `apps/web`
actually touches").

**What "archived" means, precisely** — because the word could be read more broadly than intended:

| | |
|---|---|
| **Is it deleted?** | No. It is a complete, working, buildable copy. |
| **Does it still ship?** | Yes, until retirement — it *is* the ServiceHub 4.0 surface users click through to. An archive that still ships is unusual but honest: it is frozen, not dead. |
| **Is the backend archived?** | **No.** Only the frontend. Archiving `services/api` would leave the new app with no API. |
| **Is it edited?** | **Never.** Not for a lint rule, not for a dependency bump, not for a "quick fix". If it genuinely must change, that is a new decision, recorded. |
| **When does it stop being built?** | At retirement (Wave L6). After that it is pure reference, or deleted. |

### D2 — The freeze is enforced, not just intended

Three mechanisms, because a rule that depends only on memory will eventually be broken:

1. **`archive/README.md`** opens with a FROZEN banner stating the rule and pointing here.
2. **A standing instruction** for AI assistants: never edit under `archive/`; read it as reference
   only.
3. **A CI check** that fails when a commit modifies anything under `archive/`, overridable only by
   an explicit marker in the commit. `.github/workflows/servicehub.yml` already has a
   `whitespace-check` job to model it on. The check is verified by deliberately breaking it.

### D3 — There is one application: `apps/servicehub`

The new app is the product. Not an edition, not a mode, not a variant.

**The working codename of this transition appears in no folder, route, bundle, CI job, page title or
wordmark that ships.** The folder is `apps/servicehub`, and no later rename is planned.

### D4 — The shared package stays shared, and is where reuse goes

`packages/servicehub-ui-shared` is consumed by `apps/servicehub` and by the archived app. While both
build:

- **Nothing already in the package has its signature or behaviour changed.** New needs are met by
  new exports. The archived app's test suite is the regression contract, and CI already runs it
  alongside the package's own tests.
- After retirement, that constraint lifts entirely and the package can be simplified freely —
  because there is only one consumer left.

**Rejected:** forking the package. It would duplicate 14,536 lines of API-client code that must stay
in lockstep with 33 backend controllers. Two copies of a wire contract drift silently, and the
divergence surfaces as a production bug rather than a build error.

### D4b — The backend is not archived, and is not rewritten either

Archiving applies to the **frontend only**. `services/api` keeps running and keeps being developed —
but under a strict rule, because it is the most proven code in the repository:

> **The background machinery is restructured by a strangler, never a rewrite.** Any change adds
> structure **over** the existing 20 workers with zero logic change. Any later migration moves **one
> agent per change**, body moved not rewritten, with the **existing tests unchanged as the
> contract**. A test that needs editing means behaviour changed — that is the stop signal.

This keeps *"whatever we are doing today, the same we can do"* as a gate rather than a hope, and it
means UI work and backend work can proceed in parallel without either destabilising the other.

### D5 — One API, one container, one database — unchanged

No new service, port, process or datastore. [ADR-0003](0003-single-instance-sqlite.md) and
[ADR-0004](0004-self-hosted-security-model.md) are not reopened. Both frontends are static assets
served by the same ASP.NET Core process.

### D6 — The new app is built for simplicity, not assembled from the archive

The archived pages are **read as reference** — for endpoint sequences, error states and the edge
cases they already handle — and are **not copied**. A copied 1,000-line page brings the panel model,
the URL model and the vocabulary with it, which is exactly what this transition exists to leave
behind.

Where a sub-component is genuinely presentation-neutral and worth keeping (a JSON body viewer, a
copy button, a provider icon), it is **promoted** into
`packages/servicehub-ui-shared/src/components/` as a new file. Promotion is additive; it never
rewrites the archive's import.

## Options considered

| | Option | Verdict |
|---|---|---|
| **A** | **Archive `apps/web`; build one `apps/servicehub`** | **Chosen.** One maintained app, matching the stated end state. The old surface stays available and frozen through the transition, then goes. |
| **B** | Two co-maintained sibling apps | **Rejected.** It keeps both surfaces alive indefinitely — the opposite of *"we maintain only one, i.e. ServiceHub."* |
| **C** | A `mode` flag inside `apps/web` | **Rejected.** Every screen becomes conditional on mode; all 53,783 lines being simplified must be touched and then maintained in both modes forever. |
| **D** | A separate repository | **Rejected.** Forces the shared package to be published, splits the single-container ship, duplicates CI, makes an API change a two-repo problem. |
| **E** | Fork `apps/web` → new app, then delete pages | **Rejected.** Starts the surface that replaces 53,783 lines *with* those 53,783 lines. Fast for a week, slower every week after — deletion never finishes and the old assumptions survive in the corners. |

## How the two surfaces are served

Kept as simple as the owner asked for: one added fallback rule, one flip, one deletion.

### 5.1 During Waves L2–L4 — the new app at `/new`

| | |
|---|---|
| New app output | `services/api/src/ServiceHub.Api/wwwroot/new/` |
| New app Vite `base` | `/new/` |
| New app router | `createBrowserRouter(routes, { basename: '/new' })` |
| Archived app | **untouched** — still builds to `wwwroot/`, still owns `/`, still no basename |
| API change | one added fallback: `/new/**` matching no file → `wwwroot/new/index.html` |

The archive is not edited at all in this phase. The new ServiceHub is fully usable and testable, and
can be driven live while it is being built.

### 5.2 Wave L5 — the cutover

| | After cutover |
|---|---|
| ServiceHub | `wwwroot/`, `base: '/'`, no basename — **owns the origin root** |
| ServiceHub 4.0 (archived) | `wwwroot/servicehub-4-0/`, `base: '/servicehub-4-0/'`, `basename: '/servicehub-4-0'` |
| Archive diff | `vite.config.ts` (`base`, `outDir`) + `router.tsx` (`basename`) — **two lines** |
| API | `/servicehub-4-0/**` → the archive's index; everything else → ServiceHub's index |

> **This is the one and only time the archive is edited**, and it is a two-line mechanical change
> required to move it off the root. It is made under this ADR, at a gate, and recorded in the commit
> — it is not a precedent.

### 5.3 The published demo URLs — settled now, not discovered at cutover

`/demo/azure`, `/demo/aws` and `/demo/gcp` appear in `sitemap.xml`
(`WebApplicationExtensions.cs`), in the README and in published posts. They are served by the
archived app's router today, and a React Router app has exactly one `basename` — so after cutover
the archive cannot own both `/servicehub-4-0/*` and `/demo/*`.

> **ServiceHub owns `/demo/*` from the cutover onward**, with its own demo mode built on the
> existing shared `packages/servicehub-ui-shared/src/lib/demo/` mock providers. The archive's demo
> moves to `/servicehub-4-0/demo/*`.

This makes **ServiceHub demo mode a prerequisite of cutover**, not a follow-up. It is also the
better outcome: published links land a first-time visitor in the simple product.

### 5.4 Wave L6 — retirement

The `/servicehub-4-0/**` fallback is removed, the archive drops out of the Dockerfile and CI, and
`wwwroot/servicehub-4-0/` stops being produced. `archive/servicehub-web-4.0.0/` remains in the
repository as reference, or is deleted — a separate call at that time.

## What moving `apps/web` actually touches

The move itself is `git mv`. References outside the folder are then repointed, all mechanical:

| File | Change |
|---|---|
| `package.json` (root) | add `"archive/*"` to `workspaces` |
| `.github/workflows/servicehub.yml` | `-w apps/web` → `-w archive/servicehub-web-4.0.0` (every occurrence); the job says it builds the archived surface; the frozen-archive check is added |
| `.github/workflows/deploy.yml`, `codeql.yml`, `.github/codeql/codeql-config.yml`, `azuredevops/azure-pipelines.yml` | the same path repoint |
| `Dockerfile`, `.dockerignore` | `COPY apps/web/` → `COPY archive/servicehub-web-4.0.0/`; the `.env.production` exception follows |
| `runtest.sh`, `run.sh` | `WEB_DIR` and the dev-server workspace path repointed |
| `.gitignore` | the app's `test-results/` and `playwright-report/` entries follow the move |
| `package-lock.json` | regenerated by `npm install`, not hand-edited |

**Nothing inside the moved folder changes** — `archive/` sits at the same depth `apps/` did, so
every relative path in it still resolves. That is the reason for putting it at the top level rather
than nesting it.

Two standing gotchas that apply to this move:

- **`runtest.sh` does not type-check.** Run `npx tsc -b` separately or CI will be red while local is
  green.
- **The archived app's build writes into `services/api/src/ServiceHub.Api/wwwroot`, which is
  tracked**, so a build dirties tracked files. The new app must not inherit that by copy-paste — its
  output path is decided here (§5.1), once.

## Consequences

**Positive**

- **One product, one name, one maintained application** — the stated end state, reached by a
  planned route rather than by attrition.
- The archive cannot be damaged by new work: it is not open, and CI fails if it is touched.
- The new app inherits the entire tested data layer — the first useful screen is days, not weeks.
- One container, one image, one deployment, one database throughout. The self-hosted promise is
  untouched.
- Retirement is already designed (L6), so "when does the old UI go away" has an answer now.

**Negative, and accepted**

- **Two frontends build during the transition.** The image is larger and both must compile.
  Mitigated by the archive being frozen: frozen code changes rarely, so it breaks rarely.
- **The shared package is a coupling point while both exist.** Mitigated by D4 and by CI already
  running both suites. The constraint lifts entirely at retirement.
- **Some duplication is deliberate.** A DLQ table exists in both, written differently. That is the
  intended cost: the archive's version is frozen and the new one is free to be simple.
- **The cutover is a real, if small, risk.** Contained to one gated wave, a two-line archive diff,
  and a demo-URL story settled in advance (§5.3).

## What would make this decision wrong

Recorded so it can be checked later rather than argued from memory:

1. If ServiceHub's page set grows past **twelve screens**, the simplification has failed and the old
   product is being rebuilt under a new name.
2. If more than **three** shared-package exports need their existing signatures changed, D4's
   additive-only rule is not holding, and the (rejected) fork question should be reopened
   deliberately rather than drifted into.
3. If the archive needs to be edited for any reason other than §5.2's two lines, then it is not
   actually frozen and either the freeze or the plan is wrong.
4. If the cutover cannot be executed inside one wave, the serving model was more complex than §5
   assumes — in which case the new app simply stays at `/new` until it can be. That fallback costs
   nothing and is always available.
