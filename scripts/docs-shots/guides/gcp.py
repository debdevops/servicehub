# Google Cloud guide content. Prose lives here; every screenshot's numbered key text comes from keys.json (written when the screenshot is taken),
# so the picture, the key and the Help article can never disagree. Step numbers match the Azure and AWS guides so each screen's (?) opens the same step.
CLOUD = "gcp"
TITLE = "Google Pub/Sub"
SHOT_DIRS = {"app": "gcp", "portal": "gcp-portal"}
INTRO = """This guide takes you from nothing to a working ServiceHub on **Google Cloud Pub/Sub**, one screen at a time. Every picture is the real
application (or the real Google Cloud console) — not a mock-up — with numbered markers; the numbered list under each picture says what that control
does **and what it will not do**. Account names and key ids are blanked out in the console pictures.

**What you will do**

1. In the Google Cloud console, make a service account just for ServiceHub, give it only the Pub/Sub roles it needs, and download a key.
2. Connect Google Cloud in ServiceHub and check it can see your topics and subscriptions.
3. Ask ServiceHub to look at the dead letters, understand them, and replay one safely.
4. See what ServiceHub can and cannot prove on Google Cloud, and learn what Auto Replay, Settings and the Advanced pages show.

**What you need**

| You need | Why |
|---|---|
| A project with Pub/Sub **topics and subscriptions**, ideally with a **dead-letter policy** on the subscription | Failed messages are forwarded to a dead-letter topic; ServiceHub reads that topic's subscription |
| Permission to create a service account and a key in the project | You will make a ServiceHub-only credential. Some organisations forbid service-account keys by policy: see the troubleshooting list |
| ServiceHub running (see the README) | You will open it in a browser |

> **Cost, and what is different on Google Cloud.** Pub/Sub has no way to look at a message without *pulling* it, and every pull counts as a
> delivery attempt. So ServiceHub **never looks on its own**: it reads dead letters only when you press **Look now** (up to 100 per queue, handed
> straight back, and it can take up to a minute per queue). It does not browse your active messages at all. Pub/Sub also reports **no message
> counts**, **no failure reason** and **no delivery count**, so ServiceHub says "can't count here" or "reason not recorded" instead of guessing.
> Normal Pub/Sub charges apply; ServiceHub adds none of its own.

**The roles to give the service account** (Pub/Sub only; nothing else in your project is reachable):

| Role | Where | Why |
|---|---|---|
| `roles/pubsub.viewer` | The project | List your topics and subscriptions |
| `roles/pubsub.subscriber` | Your subscriptions (including the dead-letter one) | Pull to look, hand a message back, remove the dead-letter copy on replay |
| `roles/pubsub.publisher` | Your topics (including the dead-letter topic) | Put a replayed message back — used only when you replay |

Granting the three roles on the whole project also works and is simpler; granting `subscriber` and `publisher` on just the resources above is tighter.
"""
SECTIONS = [
 ("Part 1 — In the Google Cloud console", "Do these first. Each step shows the real console.", [
  ("1.1 Look at your topics and subscriptions",
   "Sign in to the Google Cloud console, pick your project (note its **Project ID**, not its name) and open **Pub/Sub**. **Topics** lists where messages are published; **Subscriptions** lists who reads them. A queue in ServiceHub is a subscription together with its topic.",
   [("portal", "portal-01-topics"), ("portal", "portal-02-subscriptions")]),
  ("1.2 Find the dead-letter policy",
   "Open your main subscription and look at **Dead lettering**. It names the dead-letter topic and the **Maximum delivery attempts** — how many tries before Pub/Sub forwards a message there. The dead-letter topic needs its own subscription, or forwarded messages are not kept: that subscription is what ServiceHub reads as your dead letters.",
   [("portal", "portal-03-subscription-dead-letter")]),
  ("1.3 Create a ServiceHub-only service account",
   "Open **IAM & Admin → Service accounts → Create service account**. Name it (we use `servicehub-app`) and skip the optional role steps for now. Do not use your own account or the default compute account: a separate account can be switched off later without disturbing anything else.",
   [("portal", "portal-04-service-accounts")]),
  ("1.4 Give it only the Pub/Sub roles it needs",
   "Grant the three roles from the table at the start of this guide: **Pub/Sub Viewer** on the project (IAM page), and **Pub/Sub Subscriber** and **Pub/Sub Publisher** on your subscriptions and topics (each resource's **Permissions** panel) — or all three on the project if you prefer one step. If the key ever leaked, it could touch nothing outside Pub/Sub.",
   [("portal", "portal-05-iam-roles"), ("portal", "portal-06-topic-permissions")]),
  ("1.5 Create a key",
   "Open the service account, then **Keys → Add key → Create new key → JSON**. A file downloads — **Google shows its private key only once**. Treat the file like a password: ServiceHub encrypts it on arrival and never shows it again, but anyone who has it can use it. Disabling or deleting the key is how you revoke access later.",
   [("portal", "portal-07-keys")]),
 ]),
 ("Part 2 — Connect Google Cloud in ServiceHub", "About two minutes.", [
  ("2.1 The welcome page",
   "With no cloud connected, ServiceHub opens on a welcome page. Choose **Connect Google**.",
   [("app", "01-welcome")]),
  ("2.2 Add a cloud",
   "The window opens on the Google Pub/Sub tab. Open **Where do I get the service-account key?** for the same console steps you just did.",
   [("app", "02-add-cloud"), ("app", "02-add-cloud-help")]),
  ("2.3 Fill it in and connect",
   "Give it a name, keep the environment as **Development** for your first connection, type the **Project ID**, choose the key file you downloaded, and press **Connect**. ServiceHub reads the file in your browser, tests it, then saves it encrypted (AES-256-GCM).",
   [("app", "03-add-cloud-filled")]),
  ("2.4 The result",
   "You see what ServiceHub can see: how many topics and subscriptions it found, and that this cloud **cannot yet prove a replayed message stayed fixed** — ServiceHub will say so honestly rather than pretend.",
   [("app", "04-connected")]),
 ]),
 ("Part 3 — Learn the screens", "Home, and the controls that are on every page.", [
  ("3.1 Home",
   "Home answers: *what needs me* and *how is each cloud doing*. For Google Cloud it shows what the cloud can do (the chips: no message counts, recorded when you look, no browsing, can't confirm fixes yet), the **Look now** button, and what has been recorded. Scroll down for why messages failed, how replays ended, your subscriptions with a Look now beside each, the latest dead letters and a live activity list.",
   [("app", "05-home"), ("app", "05c-home-middle"), ("app", "05d-home-end")]),
  ("3.2 The bar and the sidebar",
   "These are the same on every page.",
   [("app", "05b-navigation")]),
 ]),
 ("Part 4 — Work with dead letters", "The core loop: look, understand, replay, confirm.", [
  ("4.1 The Dead letters tab",
   "A **dead letter** is a message your consumer failed to process several times, so Pub/Sub forwarded it to the dead-letter topic. The list starts **empty on Google Cloud** even when messages are waiting, because pulling a message counts as a delivery attempt. Press **Look at Google Cloud's dead letters now**: ServiceHub pulls up to 100 per queue, records them, and hands them straight back. It never deletes or moves anything. It can take up to a minute per queue. Press it again to record more; Google Cloud hands back a sample, so a dead letter not seen on a later look stays in the list.",
   [("app", "06-dead-letters"), ("app", "06b-after-look")]),
  ("4.2 Filters, selection and the table",
   "Narrow the list, tick the messages you want, and use **Details** or **Replay** on a row. Pub/Sub records no failure reason, so there is a single reason, *No reason recorded*; choosing it filters the list (with *show all reasons* at the foot to clear it), and the list pages at the bottom. Refresh re-reads what ServiceHub has recorded; only Look now asks Google Cloud.",
   [("app", "07-filters-and-table"), ("app", "07a-reason-filter"), ("app", "07a-reason-filter-end"), ("app", "07c-table-end"), ("app", "07b-selection")]),
  ("4.3 Open a message",
   "**Details** opens the message: *Reason not recorded* (Pub/Sub keeps none), a plain-words reading drawn from the message's own attributes (marked *Suggestion* because it is a reading, not something Google Cloud reported), and the body. The tabs show the body, the attributes your publisher attached, what Pub/Sub recorded, and delivery — which Google Cloud does not report, so it says so. At the bottom are **Replay this message** and **Purge instead…**, which only opens a form.",
   [("app", "08-message-details"), ("app", "08b-message-details-end"), ("app", "08-message-details-body"), ("app", "08-message-details-properties"), ("app", "08-message-details-headers"), ("app", "08-message-details-delivery")]),
  ("4.4 Replay one message — you see the proposal first",
   "**Replay** never sends straight away. It shows what will happen (the message is published back to the topic it came from and the dead-letter copy is removed once Google Cloud accepts the new one), every safety check, and what happens afterwards. Only the blue button sends. On Google Cloud finding the one message can take up to a minute.",
   [("app", "09-replay-proposal"), ("app", "09-replay-proposal-middle"), ("app", "09-replay-proposal-checks")]),
  ("4.5 Replay several at once",
   "Tick messages (or use **Replay All Messages**) to get a preview: how many will be replayed, which are held back by a safety check, grouped by how they failed and where each goes. They are sent one at a time, each re-checked, and the run stops by itself after five sends in a row that are not accepted. Further down is **Purge instead…** — a separate, deliberate step to delete messages that are not worth replaying.",
   [("app", "13-bulk-replay"), ("app", "13b-bulk-replay-end")]),
  ("4.6 The result",
   "ServiceHub says plainly what happened and records it under Replayed. On Google Cloud the result will read **Verification required**: the message was sent back, but Google Cloud cannot prove the dead-letter queue stayed empty, so ServiceHub never says it held. If a consumer is running on the subscription it may pick the message up straight away.",
   [("app", "10-replay-result")]),
 ]),
 ("Part 5 — Afterwards", "Did it hold?", [
  ("5.1 Replayed",
   "Every replay, who did it, and how it ended. On Google Cloud a replay stays *Watching* for its window and is then recorded as *verification required* — never as success — until a person judges it.",
   [("app", "11-replayed"), ("app", "11b-replayed-end")]),
  ("5.2 Active messages",
   "Your topics and subscriptions. Pub/Sub reports **no message counts**, so each says *can't count here* rather than guess. ServiceHub does not open active messages on Google Cloud, because there is no way to look at one without it counting as a delivery — watching could push a message into the dead-letter topic by itself. For the same reason **Follow live** is not offered here. **Send a message** is the one control here that changes a topic.",
   [("app", "12-active")]),
 ]),
 ("Part 6 — Auto Replay", "Let ServiceHub retry a kind of failure on its own — only once it has earned it.", [
  ("6.1 Rules",
   "A rule names a failure ServiceHub has already seen and how carefully to retry it. It never runs in Production, goes through the same safety checks as you, and **stops itself** if fewer than half of its replays stay fixed. On Google Cloud a rule always waits for a person, because Google Cloud cannot prove a fix held.",
   [("app", "14-auto-replay"), ("app", "14-auto-replay-create")]),
 ]),
 ("Part 7 — Settings, Help and Advanced", "Everything else.", [
  ("7.1 Settings",
   "Connections, Notifications (Slack, Teams, any webhook — sent only when the Agent stops and needs a person), Preferences, Access & security (roles and the emergency stop) and Backup.",
   [("app", "15-settings-connections"), ("app", "15-settings-notifications"), ("app", "15-settings-preferences"), ("app", "15-settings-access"), ("app", "15-settings-backup")]),
  ("7.2 Help, search and the bell",
   "Help is a panel over whatever you are doing. Search (⌘K) jumps anywhere. The bell is the only place the Agent asks you something.",
   [("app", "16-help"), ("app", "16b-help-end"), ("app", "18-search"), ("app", "18-bell")]),
  ("7.3 The Advanced pages (read-only)",
   "Advanced never changes anything. **Overview** summarises recovery, authority and agents, and says why Google Cloud cannot verify; the **Recovery Ledger** is the tamper-evident record of every action; **Failure Signatures** groups failures (on Google Cloud, with no reason recorded, they group by subscription); **Agents** lists what is acting and what is only watching.",
   [("app", "17-advanced-overview"), ("app", "17-advanced-ledger"), ("app", "17-advanced-signatures"), ("app", "17-advanced-agents"), ("app", "17b-advanced-agents-more")]),
 ]),
]
TROUBLESHOOTING = [
 ("Connect says the key is invalid, or could not authenticate", "Check you chose the whole JSON file the console downloaded for the ServiceHub service account (it contains `\"type\": \"service_account\"` and a `private_key`), and that the key is still **Enabled** (IAM & Admin → Service accounts → the account → Keys). A key cannot be downloaded again: if you lost the file, create a new key."),
 ("Connect works but finds no topics or subscriptions", "Check the **Project ID** — it is the ID, not the project name or number — and that the service account has **Pub/Sub Viewer** on that project."),
 ("Creating the key is blocked", "Your organisation may enforce the policy *Disable service account key creation* (`iam.disableServiceAccountKeyCreation`). Ask an administrator for an exception for this project, or use a project where it is allowed."),
 ("Dead letters stays empty after Look now", "The dead-letter subscription may really be empty, or there may be no subscription on the dead-letter topic (forwarded messages are dropped without one), or the service account lacks **Pub/Sub Subscriber** on it."),
 ("Replay is unavailable or refused", "The service account lacks **Pub/Sub Publisher** on the topic (to put the copy back) or **Pub/Sub Subscriber** on the subscription (to remove the dead-letter copy)."),
 ("Look now or Replay takes a long time", "Pub/Sub has no way to fetch one message by id, so ServiceHub pulls until it finds it; with a long dead-letter queue this can take up to a minute. Keep the window open — nothing is lost if you leave."),
 ("The counts say “can't count here”", "Pub/Sub reports no message counts to ServiceHub. That is the cloud, not a fault: ServiceHub shows what it has recorded instead."),
 ("Why does every replay say “Verification required”?", "ServiceHub can prove a fix held only where the cloud can show the dead-letter queue stayed empty. Google Cloud cannot, so ServiceHub records the replay honestly instead of calling it fixed."),
]
CLEANUP = """To stop ServiceHub watching Google Cloud: **Settings → Connections → Remove**. That forgets the connection and its stored credential on the ServiceHub server;
it deletes nothing in Google Cloud. To revoke access in Google Cloud, disable or delete the key (IAM & Admin → Service accounts → the account → Keys), and delete the
service account and its role grants if you no longer need them. If you created the topics only to try ServiceHub, delete them in Pub/Sub so nothing keeps costing money."""
