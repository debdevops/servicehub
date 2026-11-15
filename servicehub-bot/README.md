# ServiceHub Bot

A help bot for ServiceHub. Ask it a question ("how do I replay dlq?") and it answers from a searchable knowledge base. For some questions it adds your own live numbers, read from your ServiceHub.

**It guides; it never acts.** The bot only reads. It tells you what is true, what to do, and where to click, and its links only open screens. Every replay, purge or approval stays yours, with ServiceHub's own preview and safety checks. A test (`test/guide-only.test.ts`) fails if anything in `src/` could send a request that changes something.

No AI model and no cloud service: search is SQLite full-text search (FTS5) through Node's built-in `node:sqlite`. There are no runtime dependencies.

## Try it

Needs Node.js 24 or later.

```bash
cd servicehub-bot
npm install                    # dev tools only (TypeScript, for typecheck and the widget build)
npm run build                  # → data/knowledge.db (the widget needs no build: see below)
npm run serve -- --try         # then open http://127.0.0.1:5160/bot/ and press the round button
npm run ask -- "how do I replay dlq"
npm run ask -- --live "how do I replay dlq"                # adds live numbers from http://localhost:5153 (GET only)
npm run ask -- --debug "why aws dead letters not listed"   # also prints the ranked matches and scores
npm run ask -- --json "rotate encryption key"              # the exact payload the widget will get
npm test                       # eval set + unit tests; builds its own fresh knowledge base
npm run typecheck
```

## Where answers come from

`npm run build:kb` reads the rest of this repository (never writes to it) and builds one SQLite file:

| Source | What | Ranked |
|---|---|---|
| `knowledge/entries.ts` | Hand-written answers, some marked for live numbers | First |
| `apps/servicehub/src/next/content/help.ts` | The app's own Help answers, word for word, plus extra phrasings from `knowledge/help-aliases.ts` | Second |
| `docs/**/*.md` | Every doc's introduction and every `##`/`###` section, with a link to that heading on GitHub | Last |

Links into the app are written as page ids (`replays`, `connections`) and checked against `apps/servicehub/src/next/nav/navigation.ts` at build time, so a renamed page fails the build instead of shipping a dead link. A cloud-scoped page gets the `all` scope (`/all/replays`).

## How a question is answered

1. The question is cut to its meaningful words, each with its synonyms (`knowledge/synonyms.ts`: dlq = dead letter, resubmit = replay, …).
2. FTS5 finds candidates. Each is scored by how close the question is to one of the entry's own questions or headings, then by relevance (bm25), and by how many of the question's words it contains.
3. If the top entry contains at least 60% of the question's words, it is the answer. Below that the bot offers "Did you mean…", and with no match it says it doesn't know yet.
4. If the question asks the bot to *do* something ("replay them for me"), the answer starts by saying it can't, then gives the steps.

## Adding or fixing an answer

1. When a question gets the wrong answer, run it with `--debug` to see why.
2. The usual fix is a new phrasing in the entry's `questions`, a Help alias, or a synonym. Write a new entry only when no existing answer is right.
3. Use the app's words exactly (check the button in `apps/servicehub/src/next`), and type no thresholds.
4. Add the question to `test/eval-cases.ts`, then run `npm test`.

## Live numbers

Four answers add numbers read from ServiceHub *as the person asking*. Only their own auth headers are passed on, so the bot sees exactly what they could see.

| Answer | Reads (all GET, all used by the app's own screens) | Shows |
|---|---|---|
| Replay / what is in my DLQ | `/fleet/components`, `/fleet/coverage`, `/dead-letters?namespaceId=…&status=active&pageSize=1` per connection | Per connection: count (exact, "about", or recorded at the last **Look now**), top reasons, busiest queues, **Pending approvals**, read-only. For replay, also a fix-first tip for reasons whose meaning is certain, and a link to that connection's **Dead-letter messages** tab filtered on its top reason |
| Did my replays work? | `/recovery/summary?window=7d` | Last 7 days per cloud in the outcome words (Watching, Verified, Came back, Unverified, Not sent, Outcome unknown), and the stayed-fixed rate where there is one |
| Which clouds are connected? | `/namespaces`, `/fleet/components`, `/fleet/coverage` | Every connection with its components, last connection test and any failed read |

`src/servicehub.ts` is the only code that talks to ServiceHub: GET only, those five paths only (anything else is refused before a request is made), a 5-second timeout, and plain-sentence errors. If a read fails, the how-to answer still appears with a line saying why the numbers are missing. Totals are never added across clouds, a count the cloud didn't give is never shown as a number, and no message body or preview is ever printed.

## The service and the widget

`npm run serve` starts the bot as its own process (default `127.0.0.1:5160`, this machine only). Everything is under `/bot/`, so ServiceHub's proxy can forward one prefix and the browser sees one origin:

| Endpoint | What |
|---|---|
| `POST /bot/ask` | `{ "question": "…", "live": true }` → the answer (`src/types.ts` → `Answer`). JSON only: another website cannot make a person's browser send it without a CORS preflight, which the bot never answers |
| `GET /bot/health` | `{ ok, knowledgeBase: { built_at, servicehub_commit, counts } }` |
| `GET /bot/widget.js` | The widget, served straight from `widget/servicehub-bot.ts` with its types stripped by Node at start-up (no build step, no dependency) |
| `GET /bot/` | A try-out page, only with `--try` |

Environment: `BOT_PORT` (5160), `BOT_HOST` (127.0.0.1), `SERVICEHUB_API_URL` (http://localhost:5153), `KB_PATH` (data/knowledge.db). Live reads are cached for 15 seconds per person (a hash of their auth headers, never shared), a client gets 30 questions a minute, a body is at most 4 KB, and logs hold method, path, status and time, never the question.

The widget is one dependency-free file. Add it to a page with:

```html
<servicehub-bot endpoint="/bot/ask" live="true" app-base=""></servicehub-bot>
<script type="module" src="/bot/widget.js"></script>
```

- `live="false"` in ServiceHub's demo, where there is no API: answers then come from the knowledge base only.
- `app-base="/demo"` in the demo, so links stay inside it.
- App links ending in `/all/…` open on the cloud the person is looking at.
- A page that routes itself can catch the `servicehub-bot:navigate` event (cancelable, `detail.href`) to move without a reload.

Safety in the widget: answers can contain names that came from a cloud (queues, reasons), so all text is escaped before any of the few allowed tags are added. Links come only from the answer's `links` list, and only app paths and ServiceHub's GitHub docs pass `safeHref`. Styles are constructable stylesheets inside Shadow DOM, so they don't clash with the page and aren't blocked by its Content-Security-Policy. Key presses inside the bot stay inside it, so typing `?` or `/` there does not fire the page's own shortcuts.

## In ServiceHub

Owner's decision (c), 2026-11-15: **the bot comes with `./run.sh` and development only.** The Docker image and hosted installs don't include it.

- **`./run.sh`** builds the knowledge base and starts the bot beside the API on `127.0.0.1:5160`, or the next free port. It tells Vite to proxy `/bot` there and to show the button (`VITE_SERVICEHUB_BOT=true`). Ctrl-C stops it with everything else.
- **The bot is optional.** It is skipped, with one line saying why, when `SERVICEHUB_BOT=0`, when Node.js is older than 24, or when the knowledge base fails to build. If it stops later, `run.sh` warns once and ServiceHub keeps running. `./run.sh --check` says whether it will start.
- **In the app**, `apps/servicehub/src/next/components/BotLauncher.tsx` adds the button only when that flag is set **and** `/bot/health` answers with the bot's own JSON. Without the flag it makes no request at all, so Docker, the public demo page and the test suites are unchanged.
- **In the demo** (`/demo/…`) the button answers from the knowledge base only (`live="false"`), and its links stay inside `/demo`. In the real app it adds live numbers, read as the person using the page.
- **Links** move inside the app without a page load, through the `servicehub-bot:navigate` event.
- **Without `run.sh`:** start the bot (`npm run serve` here), then start Vite with `VITE_SERVICEHUB_BOT=true` (and `VITE_BOT_PROXY_TARGET` if the bot is not on 5160).

## Status

| Phase | What | State |
|---|---|---|
| 1 | Knowledge base, search, CLI, eval set | Built and tested (`npm test`) |
| 2 | Live numbers through ServiceHub's API, GET only | Built and tested (`npm test`, fixtures for every case). Seen against a running ServiceHub with one Azure Service Bus connection; AWS and Google Cloud only through fixtures |
| 3 | The `/bot/` service (its own process) and the floating-button widget | Built and tested. Seen in Chrome on the try-out page against a running ServiceHub: live answer, deep link, suggestion chips, the "I can't do that" reply, Escape and focus, phone width, dark mode, axe clean in light and dark |
| 4 | In ServiceHub with `./run.sh` and development (decision (c)) | Built and tested. Seen in Chrome inside ServiceHub in demo mode (Vite dev, the API proxy pointed at a dead port): button, demo answer without live numbers, links moving inside `/demo` without a page load, Escape, `?` typed in the bot not opening ServiceHub's Help, axe clean on the whole page, no console errors. **Not yet seen:** the full `./run.sh` start and stop, and live numbers inside the real app (the owner's instance was running, so neither was started beside it) |

This folder is its own npm package. It is not in the root npm workspaces (`apps/*`), and the Dockerfile copies only named paths, so it is not in ServiceHub's install or image.
