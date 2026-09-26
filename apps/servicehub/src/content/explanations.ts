/**
 * The "What you're looking at" words, one source per view (D45).
 *
 * The explainer card on each working view and the Help panel (unit 6.4) both read from here, so an
 * explanation is written once and cannot be worded two ways. Three terms at most — the knowledge a
 * newcomer needs before the screen makes sense, not a manual.
 */
export interface Explanation {
  readonly title: string
  readonly terms: readonly { readonly term: string; readonly meaning: string }[]
  /** The link that opens the longer answer in Help. */
  readonly learnMore: string
}

export const explanations = {
  fleet: {
    title: "What you're looking at",
    terms: [
      {
        term: 'Fleet',
        meaning:
          'every cloud account you have connected — each is a namespace (an Azure namespace, an AWS account and region, a Google project).',
      },
      {
        term: 'Can confirm a fix held',
        meaning:
          'whether that cloud lets ServiceHub prove a replayed message stayed out of the dead-letter queue. Azure can; AWS and Google need the small DLQ observer.',
      },
      {
        term: 'New · resolved',
        meaning:
          'messages that landed in a dead-letter queue today, and ones that left it — replayed or cleared. Each cloud counted on its own.',
      },
    ],
    learnMore: 'Clouds and what they can prove',
  },
  'dead-letters': {
    title: "What you're looking at",
    terms: [
      {
        term: 'Dead letter',
        meaning:
          'a message your consumer failed to process several times, so the cloud moved it to a side queue (the DLQ) to keep the main queue moving. Nothing is lost.',
      },
      {
        term: 'Why it failed',
        meaning:
          'ServiceHub reads the error the cloud recorded and sorts it into a category — Validation, Timeout, Lock lost… A category is a best guess, badged when it comes from the AI.',
      },
      {
        term: 'Replay',
        meaning:
          'send the message back to the queue it came from. You always see what will happen first, and ServiceHub then watches whether it stays fixed.',
      },
    ],
    learnMore: 'Dead letters in 2 minutes',
  },
  active: {
    title: "What you're looking at",
    terms: [
      {
        term: 'Active message',
        meaning:
          'one waiting in a queue or subscription to be processed. It has not failed — so there is nothing to replay here.',
      },
      {
        term: 'Delivery count',
        meaning:
          'how many times a consumer has picked it up. A climbing count is an early sign it may end up dead-lettered.',
      },
      {
        term: 'Why AWS looks different',
        meaning:
          'on SQS and Pub/Sub, opening a message counts as a delivery — so ServiceHub shows counts there, never the message.',
      },
    ],
    learnMore: 'Active vs dead letters',
  },
  signatures: {
    title: "What you're looking at",
    terms: [
      {
        term: 'Signature',
        meaning:
          'a fingerprint for one way of failing — same queue, same kind of error — so 47 messages become one thing to reason about.',
      },
      {
        term: 'Replay helps?',
        meaning:
          'from the replays already done: how many of them stayed fixed. Timeouts usually do; missing data never does.',
      },
      {
        term: 'Growing',
        meaning: 'the recent days hold at least twice what the earlier days did, and at least three messages.',
      },
    ],
    learnMore: 'Signatures and trust',
  },
  ledger: {
    title: "What you're looking at",
    terms: [
      {
        term: 'Ledger entry',
        meaning:
          'one recovery action — a replay by a person, a rule or the Agent — with everything that happened to it, in order. Entries are never edited or deleted.',
      },
      {
        term: 'Outcome',
        meaning:
          'Recovered means it did not come back. Unverified means it was replayed but the cloud can’t prove it stayed out; the two are never counted together.',
      },
      {
        term: 'Chain',
        meaning:
          'each entry carries a fingerprint of the one before it, so any change to history is detectable — even offline, with a script.',
      },
    ],
    learnMore: 'How the ledger proves itself',
  },
  agents: {
    title: "What you're looking at",
    terms: [
      {
        term: 'Agent',
        meaning:
          'a named piece of background work — one watches for dead letters, one checks whether replays held, one replays what your rules allow.',
      },
      {
        term: 'Acting vs watching',
        meaning:
          'watching agents only look and record. Only an acting agent can change anything — and only after the same safety checks you get.',
      },
      {
        term: 'Pause',
        meaning:
          'stops an agent running its cycles, so an acting agent will not act. Its loop keeps going and nothing it did is undone. Resume it from Home.',
      },
    ],
    learnMore: 'Agents in plain words',
  },
} as const satisfies Record<string, Explanation>

export type ExplanationId = keyof typeof explanations
