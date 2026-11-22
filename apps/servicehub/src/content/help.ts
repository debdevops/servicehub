/**
 * Where each screen's (?) and each page's book icon opens the step-by-step guide (`?panel=help&topic=guide-azure&step=3.1`).
 * The answers to "How do I…" live in `next/content/help.ts`.
 */

/**
 * Each screen's (?) opens the guide on its step (`?panel=help&topic=guide-azure&step=2.2`). Keyed by overlay id; a screen
 * with no entry shows no (?). A test checks every step number exists in the guide.
 */
export const screenHelpStep: Readonly<Record<string, string>> = {
  'add-cloud': '2.2',
  connections: '7.1',
  settings: '7.1',
  replay: '4.4',
  'bulk-replay': '4.5',
  'auto-replay': '6.1',
}

/** Each page's book icon opens the guide on its step. A test checks every step exists in the guide. */
export const pageHelpStep = {
  home: '3.1',
  'dead-letters': '4.1',
  replayed: '5.1',
  active: '5.2',
  'auto-replay': '6.1',
  advanced: '7.3',
} as const
