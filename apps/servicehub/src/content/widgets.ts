import type { ColumnHelp } from './columns'

/**
 * Why each widget on a page exists — what it shows, where the numbers come from, and what to do with it. Every card
 * carries one behind its (i), the same way every table column does, so nothing on screen has to be guessed.
 */
export const widgetHelp = {
  trend: { title: 'Dead letters over time', text: 'New dead letters against ones that left the queue, per day. A widening gap means trouble is arriving faster than it is cleared. Use it to spot when something started.' },
  whyFailed: { title: 'Why messages failed', text: 'The reasons the cloud recorded for the dead letters in view, biggest first. Fixing the top reason clears the most messages. Pick a reason to open just those.' },
  replayOutcomes: { title: 'How replays ended', text: 'What happened to messages put back in the last 7 days: stayed fixed, came back, still being watched, or waiting for a person. It is how you learn whether replaying is working.' },
  queueDepth: { title: 'Queue depth', text: 'Messages waiting (blue) against dead-lettered (red) for each queue or subscription that has any. A queue that is all red has stopped working.' },
  glance: { title: 'At a glance', text: 'The size of what ServiceHub can see in this cloud — namespaces, queues, topics — and how many have dead letters. It confirms the connection is reading the right place.' },
  latest: { title: 'Latest dead letters', text: 'The newest dead letters in view, with the recorded reason. A quick look before opening the full list; pick Details to read one and replay it.' },
  fleetNs: { title: 'Namespaces', text: 'Every namespace ServiceHub can see, grouped by cloud then environment, worst first. Use it to find which one to open.' },
  fleetFail: { title: 'Top failures', text: 'The most common recorded reasons across dead-letter queues, per cloud. If one reason dominates, fixing it clears the most messages.' },
  auth: { title: 'Authority', text: 'What ServiceHub is allowed to do on its own, by kind of failure, and why. It only grows when replays it made stayed fixed.' },
  agents: { title: 'Agents', text: 'The background workers that watch queues and act within the authority above. Each says what it last did.' },
  cap: { title: 'Capability', text: 'What each cloud can prove about a replay. Where a cloud cannot prove a message stayed gone, a person confirms.' },
  changed: { title: 'What changed', text: 'Each time ServiceHub earned or lost the right to act on its own, with the evidence that moved it.' },
} as const satisfies Record<string, ColumnHelp>
