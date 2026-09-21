# ADR-0013: Archive the entire 4.0.0 codebase; the new ServiceHub is rebuilt beside it

**Status:** Accepted — 2026-09-21, by explicit owner instruction, after ADR-0012's first commits.

**Supersedes in part:** [ADR-0012](0012-single-servicehub-application.md) — its **D1** (only the
frontend is archived), **D4** (the shared package stays shared), **D4b** (the backend is
restructured by a strangler, never rewritten), **D5** (one API, one container) and **§5** (how two
surfaces are served from one process).
**Unchanged from ADR-0012:** **D2** (the freeze is enforced), **D3** (one application, one name),
**D6** (reuse by reading and copying behaviour, never by copying whole pages), and the
"Options considered" rejections of a mode flag, a separate repository and a fork-then-delete.
**Relates to:** [ADR-0003](0003-single-instance-sqlite.md), [ADR-0004](0004-self-hosted-security-model.md),
[ADR-0005](0005-ai-capability-boundary.md) — **not reopened**.

---

## Context

ADR-0012 archived the 4.0.0 *frontend* and kept the engine (`services/api`), the Python services
and the shared package in place, on the premise that the new ServiceHub would be built on top of
them. That premise was the reason it rejected "archive `services/api` too" and "rewrite the
background workers".

The owner has since decided the opposite: **the new ServiceHub is rewritten, not built on the old
tree.** The 4.0.0 code is to be preserved whole, as a reference the new code is written *beside*,
with pieces **copied** across "whenever necessary". Keeping any part of the old tree live at the
repository root would leave two generations of code side by side under the same paths, which is
exactly the ambiguity the archive exists to remove.

One technical constraint shapes the layout. The frozen web app reaches outside its own folder
through three relative paths — `../../services/api/.../wwwroot` (build output),
`../../packages/servicehub-ui-shared/src` (imports) and `../../.version`. If the API or the shared
package moves *separately* from the web app, the frozen app can no longer build without being
edited, and the freeze forbids editing it.

## Decision

### D1 — Everything is archived, as one folder that mirrors the old repository root

`archive/servicehub-4.0.0/` **is the old repository root, unchanged in structure**:

```
archive/servicehub-4.0.0/
├── apps/{web,demo,sandbox}
├── services/{api,ai,agent}
├── packages/servicehub-ui-shared
├── scripts/
├── Dockerfile  docker-compose.yml  .dockerignore  .env.example
├── run.sh  run.ps1  runtest.sh
└── package.json  package-lock.json  .version
```

Every relative path inside it therefore resolves exactly as before, so the tree **builds, tests
and runs with zero edits**. This was verified rather than assumed: every file under those paths at
`8844f31f` (1,401 files) is byte-identical, blob for blob, to its counterpart in the archive, with
no file-mode changes.

**Stays at the repository root:** `docs/`, `.github/`, `azuredevops/`, `self-hosting/`,
`README.md`, `CONTRIBUTING.md`, `CHANGELOG.md`, `LOCAL-DEPLOYMENT.md`, `LICENSE`, `SECURITY.md`,
`CODE_OF_CONDUCT.md`, `llms.txt`, `.gitignore` and a root `.version`. These are repository
governance, not product code. `.version` exists in both places: the archive's copy is frozen at
`4.0.0` and read by the archive's own builds; the root copy is the repository's release pointer.

### D2 — The archive still builds and ships

It is a **reference that also works**, not a dead snapshot: CI, the release-image workflow, the
Azure deploy workflow, CodeQL, Dependabot and the Azure DevOps pipeline all build and test
`archive/servicehub-4.0.0/`. Workflows do this with a workflow-level
`defaults.run.working-directory`, so every `run:` step keeps its original relative commands;
`with:` inputs (cache paths, artifact paths, Docker context) carry the full path explicitly.

### D3 — The freeze is absolute

**Nothing under `archive/` is ever edited.** ADR-0012 §5.2 named one two-line exception (moving the
archived app off the origin root at cutover). It is **withdrawn**: the archive is now
self-contained, includes its own API, and is never re-based, so nothing ever needs to be edited.
A CI check that fails on any change under `archive/` is still to be added; ADR-0012 D2's other two
mechanisms (the `archive/README.md` banner, a standing instruction to AI assistants) apply as
before.

### D4 — Reuse by copying, never by importing

Nothing outside `archive/` imports from it. New code copies a function, a component or a test when
it earns its place. This replaces ADR-0012 D4's "shared package stays shared": there is no longer a
shared package for two applications to consume, because the archive's copy of it is frozen.

### D5 — Repository-wide documentation refers to the archive by path

Documentation at the root that mentions code paths carries the full `archive/servicehub-4.0.0/…`
path in prose and links, and the `cd archive/servicehub-4.0.0` step in command blocks. **Accepted
ADRs 0001–0012 are not rewritten**: they cite paths as they were when written, and
`docs/adr/README.md` says so. `CHANGELOG.md` history is likewise left intact.

## What this reopens, and what it deliberately does not decide

Reopened and now superseded: ADR-0012's premise that the engine stays and is wrapped by a strangler
(D1, D4b). The transition plan's rules **R2** ("the engine is restructured by a strangler, never
rewritten") and its rejection list §2.1 / §2.2 ("Archive `services/api` too", "Rewrite the
background workers", "A separate `ServiceHub.Agents` project") no longer describe the direction.

**Not decided here — to be settled in a follow-up ADR before any new backend code is written:**

1. The shape of the new backend: language, layout, and which parts of the 4.0.0 engine are
   re-implemented versus copied.
2. How the new stack is built, versioned and served alongside the archive (ADR-0012 §5's
   `/new` fallback and cutover assumed one shared API and no longer apply).
3. What becomes of the planned units that assumed the old API — the escalation/pending-work
   endpoints (M0), the agent registry (L3.8–L3.10) and the Agent Activity trace (L4.1).
4. How defects found in 4.0.0 are handled while it is frozen (a hotfix branch from tag `v4.0.0`, or
   a recorded exception).

## Consequences

**Positive**

- There is one unambiguous place for 4.0.0 and one for everything new; no path means two things.
- The archive is provably identical to the released code and still runs, so it is a working
  reference rather than an unverifiable snapshot.
- The rewrite is free of the old tree's assumptions, which is the point of the exercise.

**Negative, and accepted**

- **No new code lives at the repository root yet**, and the repository root no longer contains a
  runnable product; the runnable product is `archive/servicehub-4.0.0/`. Every command in the
  README, CONTRIBUTING and LOCAL-DEPLOYMENT changed accordingly.
- **The engine is rewritten**, discarding the strangler's safety net (the existing tests as the
  contract). The archive's tests remain runnable and can serve as a specification to port from.
- **Two `.version` files** exist by design (D1).
- **Paths in ADRs 0001–0012 and in older CHANGELOG entries are pre-restructure** and need the
  `archive/servicehub-4.0.0/` prefix to be resolved.

## What would make this decision wrong

1. If the archive needs editing for any reason, it is not actually frozen.
2. If a `docker build` or CI run of the archive requires a file that was not moved, the "old repo
   root" claim is false — the blob-identity check in D1 would then be incomplete.
3. If, after the follow-up ADR, the new backend is mostly the old backend with different paths, the
   rewrite was not a rewrite and ADR-0012's strangler approach should have stood.
