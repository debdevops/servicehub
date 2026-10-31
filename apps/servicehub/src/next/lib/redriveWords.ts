/**
 * The words of moving a failed run on, by component. The two clouds' actions are not the same thing, and the dialog must not describe one
 * as the other: an AWS Step Functions redrive restarts the SAME run from the step that failed (steps that already ran do not run again),
 * while an Azure Logic Apps resubmit starts a NEW run from the trigger and every step runs again. Keyed by the catalogue's component key,
 * never by cloud; a component with no entry of its own gets the redrive words.
 */
export interface RedriveWords {
  /** What the cloud calls the action, lower case: `redrive` or `resubmit`. */
  readonly verb: string
  /** The dialog's heading. */
  readonly title: string
  /** The line under the heading. */
  readonly subtitle: string
  /** The link in each row: `Redrive…` or `Resubmit…`. */
  readonly link: string
  /** One sentence for the failed-runs footnote, said only where the row offers the action. */
  readonly footNote: string
  /** One sentence for the page banner, said only where the row offers the action. */
  readonly bannerNote: string
  /** What will happen, one bullet each. */
  readonly will: (cloud: string) => readonly string[]
  /** What it cannot promise, one bullet each. */
  readonly cannot: (cloud: string) => readonly string[]
  /** The sentence for a second request on a run the cloud already accepted one for. */
  readonly again: string
  /** The confirming button. */
  readonly button: (cloud: string) => string
}

const redrive: RedriveWords = {
  verb: 'redrive',
  title: 'Redrive a run',
  subtitle: 'Ask the cloud to restart one failed run from the step that failed.',
  link: 'Redrive…',
  footNote: 'Redrive… asks the cloud to restart one run from the step that failed; ServiceHub never reads the run’s input.',
  bannerNote: 'A person can ask the cloud to redrive one; ServiceHub never runs anything again by itself.',
  will: (cloud) => [
    `${cloud} is asked to restart this one run from the step that failed, with the same input it already has.`,
    `${cloud} does the work. ServiceHub never reads the run’s input and starts nothing itself.`,
    `The request is recorded with your name before it is sent, and what ${cloud} answers is recorded after.`,
  ],
  cannot: (cloud) => [
    'That the run will finish fixed. The record will say Unverified, whatever happens next.',
    'That steps which already ran will not run again. Steps that change something outside the cloud may do it twice.',
    `That ${cloud} accepts it: a run that is still going, too old, or on a changed state machine is refused.`,
  ],
  again: 'restart the run again',
  button: (cloud) => `Ask ${cloud} to redrive`,
}

const resubmit: RedriveWords = {
  verb: 'resubmit',
  title: 'Resubmit a run',
  subtitle: 'Ask the cloud to start a new run from the same trigger. Every step runs again.',
  link: 'Resubmit…',
  footNote: 'Resubmit… asks the cloud to start a new run from the same trigger: every step runs again, and the original run stays failed. ServiceHub never reads the trigger’s input.',
  bannerNote: 'A person can ask the cloud to resubmit one, which starts a new run from the trigger; ServiceHub never runs anything again by itself.',
  will: (cloud) => [
    `${cloud} starts a new run from the trigger that started this one, with the trigger’s original input.`,
    'Every step runs again, including steps that already succeeded and anything they changed outside the cloud.',
    `${cloud} does the work. ServiceHub never reads the trigger’s input or any step’s input or output, and starts nothing itself.`,
    `The request is recorded with your name before it is sent, and what ${cloud} answers is recorded after.`,
  ],
  cannot: (cloud) => [
    'That the new run will finish fixed. The record will say Unverified, whatever happens next.',
    'That this run changes. It stays failed; the new run appears beside it.',
    'That nothing happens twice. Steps that send, write or charge outside the cloud will do it again.',
    `That ${cloud} accepts it: the run’s trigger history may have expired.`,
    'That it runs on a disabled Logic App. ServiceHub refuses, because Azure would run a resubmit on a disabled Logic App anyway.',
  ],
  again: 'start yet another full run',
  button: (cloud) => `Ask ${cloud} to resubmit`,
}

const byComponent: Readonly<Record<string, RedriveWords>> = {
  'logic-apps': resubmit,
}

export function redriveWords(componentKey: string | null | undefined): RedriveWords {
  return (componentKey ? byComponent[componentKey] : undefined) ?? redrive
}

/**
 * The sentence a failed-records footnote says where a component cannot retry in place, or can only retry somewhere else. A failure that
 * is held as data (an event, a blob) is retried by reading it and putting it back, so the words must say where, or why nowhere: for Event Grid
 * a retry would reach every subscription, and Event Grid has no way to redeliver to one. Keyed by component key, never by cloud.
 */
const handOffNotes: Readonly<Record<string, string>> = {
  'event-grid': 'ServiceHub cannot retry these: Event Grid has no way to redeliver an event, and republishing one to the topic would send it to every subscription, including ones that already handled it. Open the storage account in Azure to see the blob.',
  eventarc: 'To retry an event, use Retry in Pub/Sub: its dead-letter topic is a Pub/Sub topic, and Pub/Sub puts the event back on this trigger’s own transport topic only. On Google Cloud, press Look now there to bring the events in.',
}

export function handOffNote(componentKey: string | null | undefined): string | undefined {
  return componentKey ? handOffNotes[componentKey] : undefined
}
