# AWS guide content. Prose lives here; every screenshot's numbered key text comes from keys.json (written when the screenshot is taken),
# so the picture, the key and the Help article can never disagree. Step numbers match the Azure guide so each screen's (?) opens the same step.
CLOUD = "aws"
TITLE = "AWS SQS"
SHOT_DIRS = {"app": "aws", "portal": "aws-portal"}
INTRO = """This guide takes you from nothing to a working ServiceHub on **AWS SQS**, one screen at a time. Every picture is the real
application (or the real AWS console) — not a mock-up — with numbered markers; the numbered list under each picture says what that control
does **and what it will not do**. Account ids and the access key are blanked out in the console pictures.

**What you will do**

1. In the AWS console, make a user just for ServiceHub, limited to your queues, and create an access key for it.
2. Connect AWS in ServiceHub and check it can see your queues.
3. Ask ServiceHub to look at the dead letters, understand why they failed, and replay one safely.
4. See what ServiceHub can and cannot prove on AWS, and learn what Auto Replay, Settings and the Advanced pages show.

**What you need**

| You need | Why |
|---|---|
| One or more SQS queues, ideally with a **dead-letter queue** (a redrive policy) | The dead-letter queue is where failed messages wait for ServiceHub |
| Permission in AWS to create an IAM user and an access key | You will make a ServiceHub-only credential |
| ServiceHub running (see the README) | You will open it in a browser |

> **Cost, and the one thing that is different on AWS.** SQS has no way to look at a message without *receiving* it, and every receive counts as a
> delivery attempt. So ServiceHub **never looks on its own**: it counts messages for free, and reads dead letters only when you press
> **Look now** (up to 100 per queue, handed straight back). It does not browse your active messages at all. Normal SQS request charges apply;
> ServiceHub adds none of its own.

**The permissions policy** (replace `ACCOUNT_ID` and the region and queue names with yours). Nothing outside these two queues is reachable:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    { "Sid": "ListQueues", "Effect": "Allow", "Action": "sqs:ListQueues", "Resource": "*" },
    { "Sid": "WatchAndReplay", "Effect": "Allow",
      "Action": ["sqs:GetQueueUrl", "sqs:GetQueueAttributes", "sqs:ReceiveMessage",
                 "sqs:ChangeMessageVisibility", "sqs:SendMessage", "sqs:DeleteMessage"],
      "Resource": ["arn:aws:sqs:ap-south-1:ACCOUNT_ID:servicehub-dev-orders",
                   "arn:aws:sqs:ap-south-1:ACCOUNT_ID:servicehub-dev-orders-dlq"] }
  ]
}
```

Why each action: `ListQueues` finds your queues; `GetQueueUrl` and `GetQueueAttributes` read counts; `ReceiveMessage` is the look;
`ChangeMessageVisibility` hands a looked-at message straight back; `SendMessage` and `DeleteMessage` are used only when you replay.
"""
SECTIONS = [
 ("Part 1 — In the AWS console", "Do these first. Each step shows the real console.", [
  ("1.1 Look at your queues",
   "Sign in to the AWS console, search for **SQS** and open **Queues**. Note the **region** (top right): queues belong to a region, and you will choose the same one in ServiceHub. The **Messages available** column is what ServiceHub will count.",
   [("portal", "portal-01-queues")]),
  ("1.2 Find the dead-letter queue",
   "Open your main queue and choose the **Dead-letter queue** tab. It names the queue that receives failures and the **Maximum receives** — how many tries before SQS moves a message there. If a queue has no dead-letter queue, failed messages are retried until they expire and ServiceHub has nothing to show.",
   [("portal", "portal-02-orders-queue"), ("portal", "portal-03-dead-letter-queue")]),
  ("1.3 Create a ServiceHub-only user",
   "Open **IAM → Users → Create user**. Name it (we use `servicehub-app`), leave **console access** off — it is for programs only — and give it no groups. Do not use your own user or the root account: a separate user can be switched off later without disturbing anything else.",
   [("portal", "portal-04-iam-users")]),
  ("1.4 Give it only the SQS permissions it needs",
   "On the user, **Add permissions → Create inline policy**, choose JSON, and paste the policy from the start of this guide with your queue names. The user then has exactly one policy, limited to your queues: if its key ever leaked, it could touch nothing else in your account.",
   [("portal", "portal-05-user-permissions"), ("portal", "portal-06-policy-json")]),
  ("1.5 Create an access key",
   "Open the user's **Security credentials** tab and choose **Create access key**. Pick **Application running outside AWS**, then copy the **Access key ID** and the **Secret access key** — AWS shows the secret **once**. Treat both like a password: ServiceHub encrypts them on arrival and never shows them again. Making the key inactive or deleting it is how you revoke access later.",
   [("portal", "portal-07-access-keys")]),
 ]),
 ("Part 2 — Connect AWS in ServiceHub", "About two minutes.", [
  ("2.1 The welcome page",
   "With no cloud connected, ServiceHub opens on a welcome page. Choose **Connect AWS**.",
   [("app", "01-welcome")]),
  ("2.2 Add a cloud",
   "The window opens on the AWS tab. Open **Where do I get the AWS access key?** for the same console steps you just did.",
   [("app", "02-add-cloud"), ("app", "02-add-cloud-help")]),
  ("2.3 Fill it in and connect",
   "Give it a name, keep the environment as **Development** for your first connection, paste the Access key ID and the Secret access key, choose the region your queues are in, and press **Connect**. ServiceHub tests the key, then saves it encrypted (AES-256-GCM).",
   [("app", "03-add-cloud-filled"), ("app", "03b-add-cloud-bottom")]),
  ("2.4 The result",
   "You see exactly what ServiceHub can see: how many queues it found, how many messages are dead-lettered right now, and that this cloud **cannot yet prove a replayed message stayed fixed** — ServiceHub will say so honestly rather than pretend.",
   [("app", "04-connected")]),
 ]),
 ("Part 3 — Learn the screens", "Home, and the controls that are on every page.", [
  ("3.1 Home",
   "Home answers: *what needs me* and *how is each cloud doing*. For AWS it shows what the cloud can do (count messages, record dead letters when you look), the **Look now** button, and the counts. Scroll down for why messages failed, how replays ended, the queues needing attention, the latest dead letters and a live activity list.",
   [("app", "05-home"), ("app", "05c-home-middle"), ("app", "05d-home-end")]),
  ("3.2 The bar and the sidebar",
   "These are the same on every page.",
   [("app", "05b-navigation")]),
 ]),
 ("Part 4 — Work with dead letters", "The core loop: look, understand, replay, confirm.", [
  ("4.1 The Dead letters tab",
   "A **dead letter** is a message your consumer failed to process several times, so SQS moved it to the dead-letter queue. The list starts **empty on AWS** even when the queue holds thousands, because reading a dead letter counts as a delivery attempt. Press **Look at AWS's dead letters now**: ServiceHub receives up to 100 per queue, records them, and hands them straight back. It never deletes or moves anything. Press it again to record more; AWS hands back a sample, so a dead letter not seen on a later look stays in the list.",
   [("app", "06-dead-letters"), ("app", "06b-after-look")]),
  ("4.2 Filters, selection and the table",
   "Narrow the list, tick the messages you want, and use **Details** or **Replay** on a row. Choosing a reason chip filters to that reason (with *show all reasons* at the foot to clear it, and *Select all* for that reason in the selection bar); the list pages at the bottom. Refresh re-reads what ServiceHub has recorded; only Look now asks AWS.",
   [("app", "07-filters-and-table"), ("app", "07a-reason-filter"), ("app", "07a-reason-filter-end"), ("app", "07c-table-end"), ("app", "07b-selection")]),
  ("4.3 Open a message",
   "**Details** opens the message: the reason, a plain-words reading of why it failed (marked *Suggestion* because it is a reading, not something AWS reported), and the body. The tabs show the body, the attributes your sender attached, what SQS recorded, and the delivery count — which includes ServiceHub's own looks. At the bottom are **Replay this message** and **Purge instead…**, which only opens a form.",
   [("app", "08-message-details"), ("app", "08b-message-details-end"), ("app", "08-message-details-body"), ("app", "08-message-details-properties"), ("app", "08-message-details-headers"), ("app", "08-message-details-delivery")]),
  ("4.4 Replay one message — you see the proposal first",
   "**Replay** never sends straight away. It shows what will happen (the message goes back to the queue it came from and the dead-letter copy is removed once AWS accepts the new one), every safety check, and what happens afterwards. Only the blue button sends.",
   [("app", "09-replay-proposal"), ("app", "09-replay-proposal-middle"), ("app", "09-replay-proposal-checks")]),
  ("4.5 Replay several at once",
   "Tick messages (or use **Replay All Messages**) to get a preview: how many will be replayed, which are held back by a safety check, grouped by how they failed and where each goes. When the failures look like bad data, it suggests replaying one first. They are sent one at a time, each re-checked, and the run stops by itself after five sends in a row that are not accepted. Further down is **Purge instead…** — a separate, deliberate step to delete messages that are not worth replaying.",
   [("app", "13-bulk-replay"), ("app", "13b-bulk-replay-end")]),
  ("4.6 The result",
   "ServiceHub says plainly what happened and records it under Replayed. On AWS the result will read **Verification required**: the message was sent back, but AWS cannot prove the dead-letter queue stayed empty, so ServiceHub never says it held. If a consumer is running on the queue it may pick the message up straight away.",
   [("app", "10-replay-result")]),
 ]),
 ("Part 5 — Afterwards", "Did it hold?", [
  ("5.1 Replayed",
   "Every replay, who did it, and how it ended. On AWS a replay stays *Watching* for its window and is then recorded as *verification required* — never as success — until a person judges it.",
   [("app", "11-replayed"), ("app", "11b-replayed-end")]),
  ("5.2 Active messages",
   "What is waiting right now, **counted per queue**. ServiceHub does not open active messages on AWS, because there is no way to look at one without it counting as a delivery — watching could push a message into the dead-letter queue by itself. For the same reason **Follow live** is not offered here. **Send a message** is the one control here that changes a queue.",
   [("app", "12-active")]),
 ]),
 ("Part 6 — Auto Replay", "Let ServiceHub retry a kind of failure on its own — only once it has earned it.", [
  ("6.1 Rules",
   "A rule names a failure ServiceHub has already seen and how carefully to retry it. It never runs in Production, goes through the same safety checks as you, and **stops itself** if fewer than half of its replays stay fixed. On AWS a rule always waits for a person, because AWS cannot prove a fix held.",
   [("app", "14-auto-replay"), ("app", "14-auto-replay-create")]),
 ]),
 ("Part 7 — Settings, Help and Advanced", "Everything else.", [
  ("7.1 Settings",
   "Connections, Notifications (Slack, Teams, any webhook — sent only when the Agent stops and needs a person), Preferences, Access & security (roles and the emergency stop) and Backup.",
   [("app", "15-settings-connections"), ("app", "15-settings-notifications"), ("app", "15-settings-preferences"), ("app", "15-settings-access"), ("app", "15-settings-backup")]),
  ("7.2 Help, search and the bell",
   "Help is a panel over whatever you are doing. Search (⌘K) jumps anywhere. The bell is the only place the Agent asks you something.",
   [("app", "16-help"), ("app", "18-search"), ("app", "18-bell")]),
  ("7.3 The Advanced pages (read-only)",
   "Advanced never changes anything. **Overview** summarises recovery, authority and agents, and says why AWS cannot verify; the **Recovery Ledger** is the tamper-evident record of every action; **Failure Signatures** groups failures and shows what each has earned; **Agents** lists what is acting and what is only watching.",
   [("app", "17-advanced-overview"), ("app", "17-advanced-ledger"), ("app", "17-advanced-signatures"), ("app", "17c-advanced-signatures-end"), ("app", "17-advanced-agents"), ("app", "17b-advanced-agents-more")]),
 ]),
]
TROUBLESHOOTING = [
 ("Connect says the key is invalid or the signature does not match", "Check the Access key ID and Secret access key were copied whole, with no spaces, and that the key is **Active** (IAM → the user → Security credentials). A secret cannot be shown again: if you lost it, create a new key."),
 ("Connect works but finds 0 queues", "Check the **region** — queues in another region are not seen — and that the policy includes `sqs:ListQueues` with Resource `*`."),
 ("Dead letters stays empty after Look now", "The dead-letter queue may really be empty, or the policy lacks `sqs:ReceiveMessage` on the dead-letter queue's ARN. Make sure both queue ARNs are in the policy's Resource list."),
 ("Replay is unavailable or refused", "The policy lacks `sqs:SendMessage` (to put the copy back) or `sqs:DeleteMessage` (to remove the dead-letter copy), or the main queue's ARN is missing from Resource."),
 ("A looked-at message seems stuck for a while", "The policy lacks `sqs:ChangeMessageVisibility`, so ServiceHub cannot hand a message back at once; SQS releases it after its visibility timeout (30 seconds by default). Add the action."),
 ("The count is not exactly what the console shows", "SQS counts are approximate and can lag a minute. Look now records up to 100 per queue, so the Dead letters tab can show fewer than the queue holds."),
 ("Why does every replay say “Verification required”?", "ServiceHub can prove a fix held only where the cloud can show the dead-letter queue stayed empty. AWS cannot, so ServiceHub records the replay honestly instead of calling it fixed."),
]
CLEANUP = """To stop ServiceHub watching AWS: **Settings → Connections → Remove**. That forgets the connection and its stored credential on the ServiceHub server;
it deletes nothing in AWS. To revoke access in AWS, make the access key **Inactive** or delete it (IAM → the user → Security credentials), and delete the user
if you no longer need it. If you created the queues only to try ServiceHub, delete them in SQS so nothing keeps costing money."""
