/**
 * Help for the 5.0.0 shell (build-plan unit F6; design `d09-help`): task-shaped answers, a few sentences each, with a link to the page that
 * does the thing. Every answer says only what the built pages do. **No threshold is typed here** (10 verified fixes, 95%…): those live
 * in the trust rules and must be read from them, never written twice (the F11 rule), so an answer points at Failure signatures, which shows
 * how close each kind of failure is. `to` is a page entry id; the panel fills in the cloud you are looking at.
 */
export interface NextHelpAnswer {
  readonly id: string
  readonly question: string
  readonly answer: string
  readonly link?: { readonly label: string; readonly to: string }
}

export const nextHelpAnswers: readonly NextHelpAnswer[] = [
  { id: 'why-failed', question: 'See why a message failed', answer: 'Open the component, then Dead-letter messages, then the message. It shows the recorded reason, its body and properties, and what has happened to it.', link: { label: 'Open Home', to: 'home' } },
  { id: 'replay-one', question: 'Replay one message', answer: 'Open the message and choose Replay. A preview shows what will happen, and every safety check, before anything is sent.', link: { label: 'Open Replay history', to: 'replays' } },
  { id: 'replay-many', question: 'Replay many at once', answer: 'On a component’s Dead-letter messages tab, tick the messages (or pick a reason and select all), then Replay selected. You get a preview first; messages go one at a time and the run stops by itself after repeated failures.' },
  { id: 'did-it-work', question: 'Know whether a replay worked', answer: 'Replay history shows the outcome word for each replay. Verified means it did not come back to dead-letter in the window. It does not mean the consumer finished its work.', link: { label: 'Open Replay history', to: 'replays' } },
  { id: 'asked-less', question: 'Stop being asked about the same failure', answer: 'Approve it while it keeps working. Once enough fixes for that kind of failure have been verified at a high enough rate, on a cloud that can prove it, the Agent handles it alone. Failure signatures shows how close each kind is.', link: { label: 'Open Failure signatures', to: 'signatures' } },
  { id: 'agent-earns', question: 'How does the Agent earn the right to act alone?', answer: 'Per kind of failure, from verified results, never from a number of approvals. A cloud that cannot prove a fix held can never earn it, so a person decides there every time.', link: { label: 'Open Failure signatures', to: 'signatures' } },
  { id: 'stop-all', question: 'Stop everything', answer: 'Emergency stop, in the header. It stops every replay ServiceHub would do on its own, on every cloud. A person can still replay one message. It needs a reason and a typed word.' },
  { id: 'handed-off', question: 'Something was handed to a person', answer: 'ServiceHub stopped and will not retry it by itself. Open it from Needs your attention: it says why, then you can replay it again, mark it settled with a reason, or leave it.', link: { label: 'Open Needs your attention', to: 'attention' } },
  { id: 'no-replay', question: 'Why is there no Replay button?', answer: 'Either the connection is read-only, or the component keeps no failed copy to put back. What ServiceHub covers says which components it can replay.', link: { label: 'What ServiceHub covers', to: 'coverage' } },
  { id: 'auto-rules', question: 'Let ServiceHub replay a failure on its own', answer: 'Make a rule from a failure it has already seen. A rule only proposes what the safety checks allow, and it stops itself if its replays do not stay fixed.', link: { label: 'Open Auto Replay rules', to: 'rules' } },
]
