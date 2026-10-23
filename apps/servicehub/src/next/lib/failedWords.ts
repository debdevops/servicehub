import type { DetectionKind } from '../../lib/api/coverage'

/**
 * The words the failed-items tab uses, by what the catalogue says a component detects: a `runs` component has failed runs, a `records`
 * component has failed records (dead-lettered events, undelivered events, stream failure records). Nothing here names a service.
 */
export interface FailedWords {
  /** The tab's heading, without the count. */
  readonly title: string
  /** The Tip key for the heading. */
  readonly tip: string
  /** One item, lower case: "run" or "record". */
  readonly one: string
  /** The detail panel's heading when nothing is selected. */
  readonly detailTitle: string
  readonly detailTip: string
  /** What the cloud says when the item is gone. */
  readonly gone: string
  /** The label of the two times in the detail. */
  readonly first: string
  readonly last: string
  readonly empty: (component: string, incomplete: boolean) => string
  readonly caption: (component: string) => string
  readonly foot: (component: string) => string
}

const runs: FailedWords = {
  title: 'Failed runs',
  tip: 'section:Failed runs',
  one: 'run',
  detailTitle: 'Run',
  detailTip: 'section:Run detail',
  gone: 'The cloud no longer has this run.',
  first: 'Started',
  last: 'Stopped',
  empty: (c, incomplete) => `No failed, timed-out or aborted ${c} runs were found${incomplete ? ' in what could be read' : ''}.`,
  caption: (c) => `Failed ${c} runs, newest first`,
  foot: (c) => `ServiceHub only reads these. To run one again, open it in the cloud’s console: this version does not move work on for ${c}.`,
}

const records: FailedWords = {
  title: 'Failed records',
  tip: 'section:Failed records',
  one: 'record',
  detailTitle: 'Record',
  detailTip: 'section:Record detail',
  gone: 'The cloud no longer has this record.',
  first: 'Earliest',
  last: 'Latest',
  empty: (c, incomplete) => `No failed or undelivered ${c} records were found${incomplete ? ' in what could be read' : ''}.`,
  caption: (c) => `Failed ${c} records, newest first`,
  foot: (c) => `ServiceHub only reads these. To deal with one, open it where the cloud keeps it: this version does not move work on for ${c}.`,
}

export function failedWords(detection: DetectionKind): FailedWords {
  return detection === 'records' ? records : runs
}

/** The cloud's own words for how an item ended. A state not listed is shown as the cloud wrote it, never relabelled. */
export const itemStatusWords: Readonly<Record<string, string>> = {
  FAILED: 'Failed', TIMED_OUT: 'Timed out', ABORTED: 'Aborted', CANCELLED: 'Cancelled',
  Failed: 'Failed', TimedOut: 'Timed out', Cancelled: 'Cancelled',
  DEAD_LETTERED: 'Dead-lettered', UNDELIVERED: 'Undelivered',
}

const bad = 'bg-red-50 text-red-700'
const warn = 'bg-amber-50 text-amber-800'
const calm = 'bg-slate-100 text-slate-700'
export const itemStatusTone: Readonly<Record<string, string>> = {
  FAILED: bad, TIMED_OUT: warn, ABORTED: calm, CANCELLED: calm,
  Failed: bad, TimedOut: warn, Cancelled: calm,
  DEAD_LETTERED: warn, UNDELIVERED: warn,
}
