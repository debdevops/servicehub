import type { KnowledgeEntry } from '../src/types.ts'

/**
 * Hand-written answers. They rank above ServiceHub's own Help text and above doc sections, so write one here when a
 * question deserves a better answer than the docs give, or needs live data.
 *
 * Rules:
 * - The bot guides; it never acts. An answer says what is true, what to do, and where to click. Links only open screens.
 * - Words come from the product (5.0.0 layout): page and button names exactly as the app shows them, outcome words
 *   Verified / Watching / Came back / Unverified. Check a button name in `apps/servicehub/src/next` before writing it.
 * - Never promise what a cloud cannot do (AWS and Google Cloud cannot prove by themselves that a replay held).
 * - No thresholds typed here (counts, percentages): they live in the product and change there.
 * - App links name a page id from `next/nav/navigation.ts`; the build checks it exists.
 * - Every entry needs an eval case in `test/eval-cases.ts`.
 */
export const entries: readonly KnowledgeEntry[] = [
  {
    id: 'dlq-replay',
    title: 'Replay dead-lettered messages',
    questions: [
      'How do I replay a DLQ?',
      'How do I replay dead letters?',
      'Replay dead-lettered messages',
      'Resubmit messages from the dead-letter queue',
      'Retry failed messages in the DLQ',
      'Move messages back from the dead-letter queue',
      'Reprocess dead letter messages',
      'Redrive the DLQ',
    ],
    keywords: ['replay', 'dead letter', 'resubmit', 'redrive'],
    live: 'dlq-replay',
    answer: [
      '**One message**',
      '1. On **Home**, open the component (for example **Service Bus**), then the connection.',
      '2. Go to the **Dead-letter messages** tab and choose **Open** on the message.',
      '3. Choose **Replay**. A preview shows what will happen, and every safety check, *before* anything is sent. You confirm there.',
      '',
      '**Many at once:** on the same tab, tick the messages (or pick a reason and select all), then **Replay selected…**. You get a preview first; messages go one at a time and the run stops by itself after repeated failures.',
      '',
      '**Afterwards:** **Replay history** shows the outcome of each replay: **Watching**, then **Verified** (it did not come back to dead-letter in the window) or **Came back**. A cloud that cannot prove a fix held by itself (AWS, Google Cloud) shows **Unverified**, and a person decides.',
      '',
      'Fix the cause first: a message that failed every delivery usually fails again if nothing changed.',
    ].join('\n'),
    link: { label: 'Open Home', page: 'home' },
  },
  {
    id: 'dlq-status',
    title: 'What is in my dead-letter queues',
    questions: [
      'How many dead letters do I have?',
      'What is in my DLQ?',
      'Show my dead letters',
      'Why are my messages dead-lettering?',
      'How many messages are in the dead-letter queue?',
      'Which queue has the most dead letters?',
      'DLQ count',
    ],
    keywords: ['dead letter', 'count', 'reason', 'queue'],
    live: 'dlq-status',
    answer: [
      'Each connection has a **Dead-letter messages** tab with the reason, description and delivery count of every message.',
      '',
      '**Azure** is watched automatically, so its numbers update on their own. **AWS** and **Google Cloud** only show what ServiceHub was asked to look at: choose **Look now** to record what is in their dead-letter queues. (Reading a dead letter there counts as a delivery attempt, so ServiceHub never does it in the background.)',
    ].join('\n'),
    link: { label: 'Open Home', page: 'home' },
  },
  {
    id: 'replay-results',
    title: 'Did my replays work?',
    questions: [
      'Did my replay work?',
      'Did the fix hold?',
      'What happened to the messages I replayed?',
      'Replay status',
      'Show replay results',
    ],
    keywords: ['replayed', 'held', 'verified', 'came back', 'watching', 'unverified', 'outcome'],
    live: 'replay-results',
    answer: [
      '**Replay history** shows one outcome per replay:',
      '- **Watching**: still inside the watch window.',
      '- **Verified**: the window ended and it did not come back to dead-letter. It does not prove the consumer finished its work.',
      '- **Came back**: it dead-lettered again; the fix did not hold.',
      '- **Unverified**: sent back, but the cloud cannot prove it stayed fixed (AWS and Google Cloud by themselves). A person decides.',
    ].join('\n'),
    link: { label: 'Open Replay history', page: 'replays' },
  },
  {
    id: 'connections',
    title: 'Which clouds are connected',
    questions: [
      'Which clouds are connected?',
      'Is my namespace connected?',
      'Connection status',
      'Show my connections',
      'Why is my cloud not connected?',
    ],
    keywords: ['connection', 'namespace', 'connect', 'test connection'],
    live: 'connections',
    answer:
      'Each connection is one Azure namespace, AWS account and region, or Google Cloud project. **Connections** lists them all, grouped by cloud, with whether each answered when it was last tested. To add one, choose **Connect a cloud**.',
    link: { label: 'Open Connections', page: 'connections' },
  },
  {
    id: 'connect-cloud',
    title: 'Connect a cloud with the least access',
    questions: [
      'How do I connect a cloud?',
      'Connect AWS',
      'Connect Azure Service Bus',
      'Connect Google Cloud Pub/Sub',
      'What permissions does ServiceHub need?',
      'Least privilege access',
      'Add a cloud',
    ],
    keywords: ['connect', 'permissions', 'access', 'connection string', 'access key', 'service account'],
    answer: [
      'Choose **Connect a cloud** and pick the cloud. Give ServiceHub its **own** credential, so you can revoke it without disturbing anything else:',
      '- **Azure Service Bus:** the connection string of a ServiceHub-only shared access policy with **Manage** (Azure ticks Send and Listen for you). Azure lets only Manage list queues and count messages; Send is used only when you replay.',
      '- **AWS:** an access key for a ServiceHub-only IAM user with `sqs:ListQueues`, plus `GetQueueUrl`, `GetQueueAttributes`, `ReceiveMessage`, `ChangeMessageVisibility`, `SendMessage` and `DeleteMessage` on your queues.',
      '- **Google Cloud:** a service-account key with `roles/pubsub.viewer` on the project, `roles/pubsub.subscriber` on your subscriptions, and `roles/pubsub.publisher` on your topics (used only when you replay).',
      '',
      "Credentials are encrypted on the ServiceHub server. Each cloud's guide (`docs/clouds/`) has the console steps.",
    ].join('\n'),
    link: { label: 'Open Connections', page: 'connections' },
  },
  {
    id: 'reason-max-delivery',
    title: 'What does MaxDeliveryCountExceeded mean?',
    questions: [
      'What does MaxDeliveryCountExceeded mean?',
      'Max delivery count exceeded',
      'Message could not be consumed after 10 delivery attempts',
    ],
    keywords: ['maxdeliverycountexceeded', 'delivery count', 'delivery attempts'],
    answer: [
      "Azure Service Bus moved the message to the dead-letter queue because a consumer received it and failed (or did not complete it) on every attempt, up to the queue's **max delivery count** (10 unless you changed it).",
      '',
      "Look at the consumer's logs first. Replaying before the consumer is fixed usually sends the message straight back to the dead-letter queue.",
    ].join('\n'),
    link: { label: 'Open Home', page: 'home' },
  },
  {
    id: 'reason-ttl',
    title: 'What does TTLExpiredException mean?',
    questions: ['What does TTLExpiredException mean?', 'Message expired', 'Time to live expired dead letter'],
    keywords: ['ttlexpiredexception', 'ttl', 'expired', 'time to live'],
    answer:
      'The message waited longer than its time-to-live and nobody processed it, so Azure Service Bus dead-lettered it (dead-lettering on expiry is switched on for that queue or subscription). Nothing was wrong with the message itself, so once the consumer keeps up, a replay usually works.',
    link: { label: 'Open Home', page: 'home' },
  },
  {
    id: 'dlq-purge',
    title: 'Delete (purge) dead letters for good',
    questions: [
      'How do I purge a dead letter?',
      'Delete messages from the DLQ',
      'Remove dead letters for good',
      'Discard dead-lettered messages',
      'Why is Purge not offered?',
    ],
    keywords: ['purge', 'delete', 'discard', 'for good'],
    answer: [
      "**One message:** on the connection's **Dead-letter messages** tab, choose **Open** on the message, then **Purge**. Give a reason and confirm.",
      '',
      '**Many:** tick them, choose **Replay selected…**, then **Purge instead…**. Give a reason, check what would be purged, and confirm.',
      '',
      'Purging deletes the message from the dead-letter queue **for good**. It goes through the same checks as a replay, and your reason is recorded.',
      '',
      "**Not every cloud can purge.** It needs a cloud that can delete one message on its own. Azure Service Bus can't, so there ServiceHub says \"Purge isn't offered here\".",
    ].join('\n'),
    link: { label: 'Open Home', page: 'home' },
  },
  {
    id: 'run-local',
    title: 'Run ServiceHub on your machine (no Docker)',
    questions: [
      'How do I run ServiceHub locally?',
      'Run locally without Docker',
      'Install ServiceHub from source',
      'How do I start ServiceHub?',
      'run.sh',
    ],
    keywords: ['local', 'source', 'install', 'start', 'run.sh', 'without docker'],
    answer: [
      'On macOS, Linux or a WSL 2 terminal:',
      '```bash',
      'git clone https://github.com/debdevops/servicehub.git',
      'cd servicehub',
      './run.sh',
      '```',
      "`./run.sh` checks your machine, downloads the .NET SDK and Node.js if you don't have them (into `~/.servicehub/tools`, no `sudo`), builds and starts everything. When you see **✔ ServiceHub is ready**, open <http://localhost:3000>. **Ctrl-C** stops it.",
      '',
      '- See what it would install without changing anything: `./run.sh --check`',
      '- Never download anything: `./run.sh --no-install`',
    ].join('\n'),
    link: { label: 'Read the local setup guide', href: 'https://github.com/debdevops/servicehub/blob/main/docs/LOCAL-SETUP.md' },
  },
  {
    id: 'run-docker',
    title: 'Run ServiceHub with Docker',
    questions: ['How do I run ServiceHub with Docker?', 'Run with Docker', 'Docker run command', 'Install with Docker'],
    keywords: ['docker', 'container', 'image', 'ghcr'],
    answer: [
      '1. Make an encryption key once, and keep it (it protects the cloud credentials ServiceHub stores):',
      '   ```bash',
      '   export SERVICEHUB_ENCRYPTION_KEY="$(openssl rand -hex 32)"',
      '   ```',
      '2. Start it:',
      '   ```bash',
      '   docker run -d --name servicehub --restart unless-stopped \\',
      '     -p 127.0.0.1:8080:8080 -v servicehub-data:/data \\',
      '     -e SECURITY__ENCRYPTIONKEY="$SERVICEHUB_ENCRYPTION_KEY" \\',
      '     ghcr.io/debdevops/servicehub:latest',
      '   ```',
      '3. Check it: `curl -s http://localhost:8080/health` answers `Healthy` after 20 to 60 seconds.',
      '4. Open <http://localhost:8080>.',
      '',
      'Use the same key every time you start on the same data. If you lose it, saved cloud connections become unreadable.',
    ].join('\n'),
    link: { label: 'Read the Docker guide', href: 'https://github.com/debdevops/servicehub/blob/main/docs/DOCKER.md' },
  },
]
