/** The guided tour's own small state: whether this browser has seen it, and how the banner asks for it again. */
const SEEN_KEY = 'servicehub.demo.tour'
export const TOUR_EVENT = 'servicehub:demo-tour'

export function tourSeen(): boolean {
  try {
    return window.localStorage.getItem(SEEN_KEY) === 'done'
  } catch {
    return true // a browser that cannot remember must not be shown the tour on every page
  }
}

export function rememberTourSeen(): void {
  try {
    window.localStorage.setItem(SEEN_KEY, 'done')
  } catch {
    /* it will simply be offered again */
  }
}

/** Starts the tour from anywhere (the banner's "Show me around"). */
export const startTour = () => window.dispatchEvent(new Event(TOUR_EVENT))

export interface TourStop {
  readonly title: string
  /** What to say. `canConfirm` is the chosen cloud's own capability — never its name. */
  readonly text: (canConfirm: boolean) => string
  /** Where the stop happens. Null = stay where the last stop left the screen. */
  readonly path: ((provider: string) => string) | null
  /** Something to press first so the stop's subject is on screen (opening a message, opening the replay window). */
  readonly open?: { readonly ready: () => boolean; readonly press: string }
  readonly target: () => Element | null
  /** What "Next" presses for a visitor who has not done it themselves. */
  readonly nextPresses?: () => HTMLElement | null
}

const one = (selector: string) => document.querySelector(selector)
const replayButton = () => [...document.querySelectorAll<HTMLButtonElement>('[role="dialog"] button')].find((b) => /^Replay 1 message/.test(b.textContent?.trim() ?? '')) ?? null

/** The replay window once the replay has been accepted: its "Sent back" result. */
const sentBack = () => [...document.querySelectorAll<HTMLElement>('[role="dialog"] h2, [role="dialog"] h3, [role="dialog"] p, [role="dialog"] strong, [role="dialog"] b')].find((e) => e.textContent?.trim() === 'Sent back')?.parentElement ?? null

export const stops: readonly TourStop[] = [
  {
    title: 'Messages that failed, in one place',
    text: () => 'When a message cannot be processed, the cloud sets it aside in a “dead-letter queue”. ServiceHub shows them for every cloud you connect. Three made-up companies are connected here.',
    path: () => '/',
    target: () => one('main article[aria-label]')?.parentElement ?? null,
  },
  {
    title: 'Open one',
    text: () => 'This is the list of stuck messages for one cloud. Each row says where it failed and why. Press Next to open the first one.',
    path: (provider) => `/?tab=dlq&provider=${provider}`,
    target: () => one('a[aria-label^="Details of message"]'),
  },
  {
    title: 'Why it failed',
    text: () => 'ServiceHub reads the failure and says it in plain words, beside the message itself. Nothing here is a guess presented as a fact — a suggestion is always marked as one.',
    path: null,
    open: { ready: () => !!one('section[aria-label="Why it failed"]'), press: 'a[aria-label^="Details of message"]' },
    target: () => one('section[aria-label="Why it failed"]'),
  },
  {
    title: 'Put it back',
    text: () => 'Replay sends the message back to where it came from. First ServiceHub shows what it checked: it is still stuck, it is not production, it has not been replayed too often. Press Replay — or Next, and the tour presses it.',
    path: null,
    open: { ready: () => !!replayButton() || !!sentBack(), press: 'a[aria-label^="Replay message"]' },
    target: replayButton,
    nextPresses: replayButton,
  },
  {
    title: 'Did it stay fixed?',
    text: (canConfirm) =>
      canConfirm
        ? 'ServiceHub now watches the message. If it stays out of the dead-letter queue it says “Verified”; if it comes back, it says so. In real life that takes hours — in this demo, about half a minute.'
        : 'ServiceHub recorded the replay. This cloud cannot prove a replayed message stayed fixed, so the result will read “verification required” — ServiceHub says so instead of guessing.',
    path: null,
    target: sentBack,
  },
  {
    title: 'And on its own',
    text: (canConfirm) =>
      canConfirm
        ? 'A failure that replay has fixed many times, verified, can be replayed by the Agent without asking. It earns that from counted results, and loses it the same way. You can pause the Agent here at any time.'
        : 'Where a cloud cannot prove a fix, the Agent never replays on its own: it stops and asks a person. You can pause it here at any time.',
    path: () => '/',
    target: () => one('#agent-bar'),
  },
  {
    title: 'Help and information, on every page',
    text: () => 'The small book beside a page title opens Help on that page, in the same guide the real app has. The “What am I looking at?” card explains the numbers on the page; you can hide it and bring it back.',
    path: () => '/',
    target: () => one('a[aria-label="Help for this page"]'),
  },
  {
    title: 'Auto Replay rules',
    text: () => 'Here you decide what ServiceHub may retry on its own — by failure, queue and limit. Rules are off until you switch them on, and every rule says what it would do.',
    path: (provider) => `/?panel=rules&provider=${provider}`,
    target: () => one('main section'),
  },
  {
    title: 'Advanced: the whole picture',
    text: () => 'Advanced is read-only. The overview shows what needs you, the recovery state of every message, what each Agent may do, and what changed.',
    path: () => '/advanced',
    target: () => one('main h1'),
  },
  {
    title: 'Recovery Ledger',
    text: () => 'Every replay is written down: what was sent back, by whom, and whether it stayed fixed.',
    path: () => '/advanced/ledger',
    target: () => one('main h1'),
  },
  {
    title: 'Failure Signatures',
    text: () => 'Messages that fail for the same reason are grouped into one signature, so you decide once instead of per message.',
    path: () => '/advanced/signatures',
    target: () => one('main h1'),
  },
  {
    title: 'Agents',
    text: () => 'Who is watching, what each Agent may do, and what it never does — with its recent activity.',
    path: () => '/advanced/agents',
    target: () => one('main h1'),
  },
  {
    title: 'Connections',
    text: () => 'In the real app this is where you connect a cloud with its connection string, and see whether each one is reachable. Here, three made-up clouds are connected. Now try anything — nothing is sent.',
    path: () => '/?panel=connections',
    target: () => one('main section'),
  },
]
