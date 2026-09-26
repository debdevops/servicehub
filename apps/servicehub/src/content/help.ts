/**
 * Help (unit 6.4): task-shaped answers, a few sentences each, with a link into the product. Plain words only — no
 * "signature", "pillar", "disposition", "autonomy level", "grant", "attestation" or "ledger" as a term to learn.
 */
export interface HelpAnswer {
  readonly id: string
  readonly group: 'Everyday' | 'Setting up'
  readonly question: string
  readonly answer: string
  readonly link?: { readonly label: string; readonly href: string }
}

export const helpAnswers: readonly HelpAnswer[] = [
  {
    id: 'replay-one', group: 'Everyday', question: 'Replay a dead-lettered message',
    answer: 'Open Dead letters, choose Details on the message, then Replay. You see exactly what will happen and every safety check before anything is sent. Afterwards ServiceHub watches whether the message comes back.',
    link: { label: 'Open Dead letters', href: '/?tab=dlq' },
  },
  {
    id: 'replay-many', group: 'Everyday', question: 'Replay many at once',
    answer: 'On Dead letters, tick the messages (or pick a reason and "Select all"), then Replay selected. You get a preview first, messages go one at a time, each is checked on its own, and the run stops by itself after five failures in a row.',
    link: { label: 'Open Dead letters', href: '/?tab=dlq' },
  },
  {
    id: 'verification-required', group: 'Everyday', question: 'Why does AWS or Google Cloud say "Verification required"?',
    answer: 'The replay worked — the message was sent back. What those clouds cannot do is prove the dead-letter queue stayed empty afterwards, so ServiceHub will not say "Verified" there. Azure can prove it, so Azure replays can end "Verified". A small observer that would let AWS and Google prove it is not part of this version yet.',
    link: { label: 'See what was replayed', href: '/?tab=replayed' },
  },
  {
    id: 'aws-dead-letters', group: 'Everyday', question: 'Why are my AWS or Google dead letters not listed?',
    answer: 'On those clouds, reading a dead letter counts as one delivery attempt, so ServiceHub never looks on its own. Open Dead letters and choose "Look now" — it reads up to 100 per queue and keeps them, so you can open and replay them.',
    link: { label: 'Open Dead letters', href: '/?tab=dlq' },
  },
  {
    id: 'auto-replay', group: 'Everyday', question: 'Let ServiceHub retry something on its own',
    answer: 'Open Auto Replay and create a rule for a kind of failure. A rule only acts once that failure has 10 fixes proven to have held at 95% or better, on a cloud that can prove it, and never in Production. Until then it asks you, in the bell. It switches itself off if fewer than half of its replays stay fixed.',
    link: { label: 'Open Auto Replay', href: '?panel=rules' },
  },
  {
    id: 'asked', group: 'Everyday', question: 'The Agent asked me something — what do I do?',
    answer: 'Open the bell and choose Review. You see why it asked, the messages, and the safety checks. Approve replays them as you; Decline needs a reason and stops it asking about those messages again. Not now changes nothing.',
  },
  {
    id: 'pause', group: 'Everyday', question: 'Pause the Agent',
    answer: 'Use Pause on the Agent bar on Home. The Agent will not act until someone resumes it, but it keeps watching and recording, and nothing it already did is undone. For an emergency, an Admin can switch on emergency stop in Settings → Access & security.',
    link: { label: 'Go to Home', href: '/' },
  },
  {
    id: 'connect', group: 'Setting up', question: 'Connect a cloud with the least access',
    answer: 'Choose Add a cloud. Azure needs a connection string with Listen (and Send, to replay). AWS needs a key that can read, receive and send on the queues you care about. Google needs a service account with Pub/Sub subscriber and publisher roles. Credentials are encrypted at rest on this server.',
    link: { label: 'Add a cloud', href: '?modal=add-cloud' },
  },
  {
    id: 'alerts', group: 'Setting up', question: 'Send alerts to Slack or Teams',
    answer: 'Open Settings → Notifications and add a Slack, Teams or generic webhook, then Send a test. You get a message only when the Agent stops and needs a person — never for routine activity. Private and internal addresses are refused.',
    link: { label: 'Open Settings', href: '?modal=settings' },
  },
  {
    id: 'roles', group: 'Setting up', question: 'Limit who can replay or approve',
    answer: 'In Settings → Access & security, give each API key or signed-in user a role: Viewer sees, Operator replays, Approver answers the Agent and switches rules on, Admin connects clouds and manages roles. Until you give the first role, everyone can do everything.',
    link: { label: 'Open Settings', href: '?modal=settings' },
  },
]

/** Only shortcuts that work are listed (6.6). A single key never fires inside a text field. */
export const shortcuts: readonly { readonly keys: string; readonly does: string }[] = [
  { keys: '⌘K', does: 'Search clouds, queues and places' },
  { keys: 'R', does: 'Replay the open message (shows the proposal first)' },
  { keys: '/', does: 'Filter the dead-letter table' },
  { keys: '?', does: 'Open this help' },
  { keys: 'A', does: 'Switch Simple / Advanced' },
  { keys: 'Esc', does: 'Close a panel or window' },
]
