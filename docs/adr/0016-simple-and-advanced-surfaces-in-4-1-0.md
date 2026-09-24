# ADR-0016: Simple and Advanced — two surfaces of one product, in 4.1.0

**Status:** Accepted — 2026-09-23, by the owner, choosing between written options. **Amended 2026-09-24 by D45** (see the end).

**Supersedes in part:** [ADR-0014](0014-servicehub-4-1-0-architecture.md) **D6** — Advanced is no
longer wholly out of 4.1.0. **D4 is unchanged and is what shapes this decision**: the archive is not
served, so an Advanced screen exists natively or is not listed.
**Answers:** transition decision **D40** (the toggle label), open since 2026-09-21.
**Recorded as:** `docs-private/servicehub-transition/09-DECISIONS-LOG.md` **D44**.
**Relates to, and does not reopen:** ADR-0002 (the ledger), ADR-0005 (AI boundary), ADR-0015 (schema).

---

## Context

4.0.0 worked, but it asked for a specialist vocabulary before it gave anyone value: 33 pages behind
a 26-item menu. Its users are SREs, DevOps engineers and developers who arrive with one question —
*what is stuck, and can I put it back?* — and should be able to act on the answer without reading the
story behind it.

ADR-0014 answered that with a nine-screen 4.1.0 and moved the Advanced surface (A0–A12) to 4.2.0.
The owner now wants 4.1.0 to open on the simple screens by default and to offer **Advanced** as the
place to go for more information, once a person is comfortable. The full Advanced design
(`15-ADVANCED-DESIGN.md`, D39) is ten-plus screens; eight of them were ~6,900 lines of 4.0.0 pages,
and several need tables ADR-0015 does not list. Bringing all of it into 4.1.0 would roughly double
the remaining plan.

## Decision

### D1 — Two surfaces, one product: **Simple** and **Advanced**

The product is ServiceHub. **Simple** gives the answer and the action. **Advanced** gives the
investigation. They are two views of one application — same shell, same tokens, same API, same
database — never two products, and never an edition.

The switch reads **`Simple | Advanced`**. "Light" never ships, as D0 required; this settles D40.

### D2 — The URL is the surface. There is no mode flag

Every Advanced route lives under **`/advanced`**. `surfaceOf(pathname)` is the only thing that
decides which surface a page belongs to. There is no global mode in state, in `localStorage` or in
the database; the switch is a link. A person's last surface may be remembered **per browser as a
convenience only**, and a first visit always lands on Simple.

**Every screen belongs to exactly one surface.** No page renders differently depending on a mode,
and no component outside the layout asks which surface it is on. This is what keeps this decision out
of the rejected *"simple mode flag"* (`GUARDRAILS.md` §2.2), whose cost was every screen maintained
twice. Shared *components* — the message drawer, the Outcome Card, the table — are fine; shared
*pages* are not.

### D3 — Advanced in 4.1.0 is read-only, and adds no authority

Advanced shows more about data the Simple loop already produces. **It has no action of its own.**
Replay, approval, pause and rule changes happen in the shared drawer and flows the Simple surface
already owns, reached from Advanced by link. So Advanced cannot become a second execution path, and
nothing about the eligibility gate, the ledger or R8 changes.

**One exception, on principle: pause.** An agent's pause may sit on the Advanced Agents screen,
because it can only *take authority away* — R8 forbids authority growing, never shrinking, and a
safety stop belongs wherever a person is looking at the thing to stop.

### D4 — Advanced in 4.1.0 is five screens, each built on data 4.1.0 already has

| Screen | Route | Reads | Arrives with |
|---|---|---|---|
| **Recovery Ledger** | `/advanced/ledger` | `RecoveryLedgerEntries` · `RecoveryOperations` | Wave 2 |
| **Failure Signatures** | `/advanced/signatures` | `NamespaceSignatures` | Wave 3 |
| **Agents** | `/advanced/agents` | the agent registry | Wave 4 — **moved from Simple** |
| **Approval Queue** | `/advanced/approvals` | the pending-work query (no new table) | Wave 5 |
| **Advanced Overview** | `/advanced` | the recovery summary · grants · registry · capability · pending work | Wave 6, panel by panel |

**Every table named above is already on ADR-0015 D3's list.** This decision adds none.

Each panel of the Advanced Overview renders only once its endpoint exists (R5) — so the page can be
built last without waiting on every wave, and it never shows a placeholder number.

**Out, to 4.2.0 and later:** Incidents, DLQ Intelligence, Agent Activity (nine-link traces),
Autonomy (the authority control centre, which *acts*), Fleet Operations, Cross-Cloud Trace,
Diagnostics.

### D5 — One summary, two presentations

Simple's *"94% stayed fixed"* and Advanced's disposition breakdown are computed **once**, by one
endpoint. If they could disagree, a person would stop trusting both.

### D6 — Simple gets simpler

| Change | Why |
|---|---|
| **A "Needs you" strip leads Home** | The one thing a person opens the app to learn. It was the best idea in the Advanced design (C4) and was on the wrong surface. It reads the same pending-work query as the bell. |
| **The Replay screen is folded away** | Replay happens in place, from the drawer (R9). A separate screen was a second path to the same action. Replay history becomes a **Replayed** tab on the DLQ screen. |
| **Agents moves to Advanced** | *What machinery runs?* is an investigation question. Simple keeps the Agent bar and its pause. |
| **No marketing strip in the product** | The mockup's footer feature row is copy for a website, not for an operator. |

Simple is now **seven screens** — Fleet Overview, Home, DLQ, Active Messages, Bulk Replay, Auto
Replay, Connect — plus Settings and Help.

### D7 — Two ceilings

Simple's ceiling stays at **twelve**. Advanced's ceiling **in 4.1.0 is five**: the sixth Advanced
screen is exactly how "a thin Advanced" becomes 4.0.0's thirty-three pages again, so it is a recorded
decision, never an extra entry. Both are asserted by `navigation.test.ts`.

## Options considered and rejected

| Rejected | Why |
|---|---|
| **Keep Advanced wholly in 4.2.0** | The owner wants a path to depth from the first release. Five read-only screens over existing tables cost about nine units and add no schema. |
| **The full Advanced design in 4.1.0** | Ten-plus screens, tables beyond ADR-0015, and roughly double the remaining plan — with no `4.0 ↗` fallback to make the unbuilt ones cheap (ADR-0014 D4). |
| **A mode flag (state, setting or context) that pages branch on** | Already rejected as the "simple mode flag" (`GUARDRAILS.md` §2.2): every page maintained twice, forever. |
| **`Light \| Advanced`** | D0 — "Light" is a codename and ships nowhere. |
| **Actions on Advanced screens** | Creates a second execution route beside the gated one, and grows authority (R8). Advanced links to the flow that acts. |

## Consequences

**Positive** — a person can start simple and find depth without leaving the product; the Simple
surface shrinks from nine screens to seven; Advanced cannot overstate or act; no new table.

**Negative, and accepted** — about nine more units before release; the Advanced Overview is the last
screen built, so for most of the build the Advanced surface is sparse. The mockups predate this
decision and must be corrected before the screens they drive are built (unit `0.11`).

## What would make this decision wrong

1. A page that renders two ways by surface — the rejected mode flag has returned.
2. A sixth Advanced screen in 4.1.0 without a recorded decision.
3. An Advanced screen with a button that executes, approves or changes authority.
4. Simple's stayed-fixed figure and Advanced's breakdown disagreeing.
5. The cold-start test (Gate 6) needing Advanced to finish the loop — Simple has failed, not the user.

## Amendment — D45, 2026-09-24: fewer pages

Accepted by the owner after reviewing the full design set. **D4, D6 and D7 are amended; D1–D3 and
D5 stand unchanged.**

- **D6 (Simple):** Simple is **two pages** — Fleet Overview and Home. Dead letters, Active and
  Replayed are **tabs** of Home's table; Add a cloud, Replay, Bulk Replay, Approve and Settings are
  **modals**; Auto Replay and Help are **panels**. With nothing connected, Home is the welcome; there
  is no Connect page. Each is a URL state (`?tab=` · `?modal=` · `?panel=`), linkable and
  refresh-safe.
- **D4 (Advanced):** **four** read-only pages — Overview, Recovery Ledger, Failure Signatures,
  Agents. The Approval Queue is the Ledger's **Waiting** tab: an escalation already is a ledger entry.
- **D7 (ceilings):** counted as **places** — pages + panels + modals; tabs are views of a table and
  are not counted. **Simple 12, Advanced 4.** `navigation.test.ts` asserts both, and that Advanced
  holds pages only.

The navigation array gives every entry a **kind** (`page` · `tab` · `panel` · `modal`); only pages
are routes. That is what lets the design keep changing: a new destination of any kind is one entry.
