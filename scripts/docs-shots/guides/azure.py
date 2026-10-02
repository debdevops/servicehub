# Azure guide content. Prose lives here; every screenshot's numbered key text comes from keys.json (written when the screenshot is taken),
# so the picture, the key and the Help article can never disagree.
CLOUD = "azure"
TITLE = "Azure Service Bus"
SHOT_DIRS = {"app": "azure", "portal": "azure-portal"}
INTRO = """This guide takes you from nothing to a working ServiceHub on **Azure Service Bus**, one screen at a time. Every picture is the real
application (or the real Azure portal) — not a mock-up — with numbered markers; the numbered list under each picture says what that control
does **and what it will not do**.

**What you will do**

1. In the Azure portal, make a policy just for ServiceHub, and copy its connection string.
2. Connect Azure in ServiceHub and check it can see your queues.
3. Find a dead-lettered message, understand why it failed, and replay it safely.
4. See that the replay held, and learn what Auto Replay, Settings and the Advanced pages show.

**What you need**

| You need | Why |
|---|---|
| An Azure Service Bus namespace (Standard or Premium for topics; queues work on every tier) | ServiceHub reads what is in it |
| Permission in Azure to create a *Shared access policy* on that namespace | You will make a ServiceHub-only credential |
| **Local Authentication: Enabled** on the namespace | ServiceHub connects with a connection string (shared access key). If your organisation disables it, use a namespace where it is on |
| ServiceHub running (see the README) | You will open it in a browser |

> **Cost.** Looking at Azure from ServiceHub is free of delivery side-effects: on Azure a *peek* is not a delivery, so reading a queue never
> consumes a message or adds a delivery attempt. Your normal Service Bus charges apply; ServiceHub adds none of its own.
"""
SECTIONS = [
 ("Part 1 — In the Azure portal", "Do these first. Each step shows the real portal.", [
  ("1.1 Open your Service Bus namespace",
   "Sign in to the Azure portal, search for **Service Bus** and open your namespace. Note the **Host name** (it ends in `.servicebus.windows.net`) and that **Local Authentication** is *Enabled*. In the left menu, **Settings → Shared access policies** is where the next step happens.",
   [("portal", "portal-01-namespace-overview")]),
  ("1.2 Shared access policies — and why not to reuse the default",
   "Every namespace has `RootManageSharedAccessKey`, a default key shared by everything that touches the namespace. Give ServiceHub its **own** policy instead, so you can revoke it later without disturbing anything else.",
   [("portal", "portal-02-shared-access-policies")]),
  ("1.3 Create a ServiceHub-only policy",
   "Choose **Add**, name it (we use `servicehub-app`), tick **Manage** — Azure then ticks **Send** and **Listen** for you — and **Create**. Why Manage? Azure only lets a *Manage* policy **list** your queues and **count** their messages; with Listen and Send alone ServiceHub cannot even see which queues exist (Azure answers *401: Manage, EntityRead claims required*). ServiceHub uses *Send* only when you press Replay.",
   [("portal", "portal-03-add-policy"), ("portal", "portal-03b-add-policy-filled"), ("portal", "portal-04-policy-created")]),
  ("1.4 Copy the primary connection string",
   "Open the new policy. The keys are hidden; click the copy icon beside **Primary connection string**. It begins `Endpoint=sb://…`. Treat it like a password: ServiceHub encrypts it on arrival and never shows it again, but anyone who has it can use it. The **Delete** button on this panel is how you revoke access later.",
   [("portal", "portal-05-connection-string")]),
  ("1.5 Check what is in your queues",
   "Under **Entities → Queues** you can see each queue's *Active* and *Dead-letter* counts. Open a queue to see its **Max delivery count** (after that many failed tries Azure dead-letters the message) — ServiceHub's numbers should match these.",
   [("portal", "portal-06-queues"), ("portal", "portal-07-queue-overview")]),
 ]),
 ("Part 2 — Connect Azure in ServiceHub", "About two minutes.", [
  ("2.1 The welcome page",
   "With no cloud connected, ServiceHub opens on a welcome page. Choose **Connect Azure**.",
   [("app", "01-welcome")]),
  ("2.2 Add a cloud",
   "The window opens on the Azure tab. Open **Where do I get the Service Bus connection string?** for the same portal steps you just did.",
   [("app", "02-add-cloud"), ("app", "02-add-cloud-help")]),
  ("2.3 Fill it in and connect",
   "Give it a name, keep the environment as **Development** for your first connection, paste the connection string, and press **Connect**. ServiceHub tests the string, then saves it encrypted (AES-256-GCM).",
   [("app", "03-add-cloud-filled")]),
  ("2.4 The result",
   "You see exactly what ServiceHub can see: how many queues, topics and subscriptions, how many messages are dead-lettered right now, and whether this cloud can prove a replayed message stayed fixed (Azure can).",
   [("app", "04-connected")]),
 ]),
 ("Part 3 — Learn the screens", "Home, and the controls that are on every page.", [
  ("3.1 Home",
   "Home answers three questions: *what needs me*, *how is each cloud doing*, and *what is the Agent up to*. The Agent only acts on what it has earned (see Auto Replay below); until then it watches.",
   [("app", "05-home")]),
  ("3.2 The bar and the sidebar",
   "These are the same on every page.",
   [("app", "05b-navigation")]),
 ]),
 ("Part 4 — Work with dead letters", "The core loop: see, understand, replay, confirm.", [
  ("4.1 The Dead letters tab",
   "A **dead letter** is a message your consumer failed to process several times, so Azure set it aside in the queue's dead-letter queue. ServiceHub groups them by the reason your sender recorded.",
   [("app", "06-dead-letters")]),
  ("4.2 Filters, selection and the table",
   "Narrow the list, tick the messages you want, and use **Details** or **Replay** on a row.",
   [("app", "07-filters-and-table"), ("app", "07b-selection")]),
  ("4.3 Open a message",
   "**Details** opens the message: the reason, a plain-words reading of why it failed (marked *Suggestion* because it is a reading, not something Azure reported), and the body with the bad field marked. The tabs show the body, properties, headers and delivery history.",
   [("app", "08-message-details"), ("app", "08-message-details-body"), ("app", "08-message-details-properties"), ("app", "08-message-details-headers"), ("app", "08-message-details-delivery")]),
  ("4.4 Replay one message — you see the proposal first",
   "**Replay** never sends straight away. It shows what will happen, every safety check, and what happens afterwards. Only the blue button sends.",
   [("app", "09-replay-proposal"), ("app", "09-replay-proposal-checks")]),
  ("4.5 Replay several at once",
   "Tick messages (or use **Replay All Messages**) to get a preview: how many will be replayed, which are held back by a safety check, grouped by how they failed and where each goes. They are sent one at a time and each is re-checked.",
   [("app", "13-bulk-replay")]),
  ("4.6 The result",
   "ServiceHub says plainly what happened, records it in the ledger, and starts a 24-hour watch for it coming back.",
   [("app", "10-replay-result")]),
 ]),
 ("Part 5 — Afterwards", "Did it hold?", [
  ("5.1 Replayed",
   "Every replay, who did it, and how it ended. On Azure ServiceHub can **prove** whether a replayed message stayed out of the dead-letter queue, so the result moves from *Watching* to *Stayed fixed* or *Came back*.",
   [("app", "11-replayed")]),
  ("5.2 Active messages",
   "What is waiting right now. Looking does not touch anything on Azure — a peek is not a delivery. **Send a message** is the one control here that changes a queue.",
   [("app", "12-active")]),
 ]),
 ("Part 6 — Auto Replay", "Let ServiceHub retry a kind of failure on its own — only once it has earned it.", [
  ("6.1 Rules",
   "A rule names a failure ServiceHub has already seen and how carefully to retry it. It never runs in Production namespaces, goes through the same safety checks as you, and **stops itself** if fewer than half of its replays stay fixed. A rule only acts once that failure has 10 verified fixes at 95% or better.",
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
   "Advanced never changes anything. **Overview** summarises recovery, authority and agents; the **Recovery Ledger** is the tamper-evident record of every action; **Failure Signatures** groups failures and shows what each has earned; **Agents** lists what is acting and what is only watching.",
   [("app", "17-advanced-overview"), ("app", "17-advanced-ledger"), ("app", "17-advanced-signatures"), ("app", "17-advanced-agents")]),
 ]),
]
TROUBLESHOOTING = [
 ("Connect says it could not reach the namespace", "Check the connection string was copied whole (it starts `Endpoint=sb://` and includes `SharedAccessKeyName` and `SharedAccessKey`). In the portal, open **Settings → Networking** and make sure public access is allowed from where ServiceHub runs. Confirm **Local Authentication** is *Enabled* on the Overview."),
 ("Connect fails with 401 or says it could not list queues", "The policy is missing **Manage**. Azure only lets Manage list queues and count messages. Create a policy with Manage (which includes Send and Listen) and connect again."),
 ("Connect works but Replay is unavailable", "Your policy lacks **Send**. Replay needs it. A policy with **Manage** includes Send and Listen, so create one as in step 1.3 and connect again."),
 ("The counts differ from the portal", "Portal metrics can lag by a minute. Press **Refresh** in ServiceHub and **Refresh** in the portal; they should agree."),
 ("A message I replayed is not under Replayed", "Check the time window on the Replayed tab (the default is the last 24 hours) and that the *Result* filter is *Any result*."),
]
CLEANUP = """To stop ServiceHub watching Azure: **Settings → Connections → Remove**. That forgets the connection and its stored credential on the ServiceHub server;
it deletes nothing in Azure. To revoke access in Azure, delete the policy under **Shared access policies**. If you created the namespace only to try
ServiceHub, delete its resource group in the portal (or run your Terraform destroy) so nothing keeps costing money."""
