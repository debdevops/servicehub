# The 5.0.0 part of every cloud guide (Part 2 onward). Part 1 (the cloud's own console) stays in each cloud's file.
# A screenshot is used only if the file exists, so a cloud that has no approval waiting simply has no Review picture.
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))


def _have(d, name):
    return os.path.exists(f"{ROOT}/docs/screenshots/{d}/{name}.png")


def parts(cfg):
    d = cfg["dir"]
    L = cfg["label"]

    def shots(*names):
        return [("app", n) for n in names if _have(d, n)]

    watched = cfg["watched"]
    look = []
    if not watched:
        look = [(
            "4.2 Look now (" + L + " is not watched in the background)",
            cfg["look_text"],
            shots("05a-look-now"),
        )]

    out = [
        (f"Part 2 — Connect {L} in ServiceHub", "About two minutes. You only do this once.", [
            ("2.1 The Connections page",
             "Open **Connections** in the left menu. This is the list of every cloud ServiceHub can read, grouped by cloud. Each row says whether the last test passed, which components are switched on, and has **Test**, **Add a component** and a remove (bin) button. Press **Connect a cloud** to add one.",
             shots("02-connections")),
            ("2.2 Fill in the connect window",
             f"Choose the **{cfg['tab']}** tab. {cfg['connect_text']} Nothing is saved until you press **Connect**; ServiceHub then tests the credential, and stores it encrypted (AES-256-GCM). It is never shown again.",
             shots("03-connect-dialog")),
            ("2.3 Tick the components you want",
             f"After connecting, **Add a component** lets you tick other {L} services on the same connection (for example {cfg['components']}). Each one's access is checked first, read-only, before it is added. Unticking removes it from the menu and keeps every record it produced. A small amber dot beside a component in the menu means it has been built but not yet proven against a real cloud.",
             shots("23-add-component")),
        ]),
        ("Part 3 — Learn the screens", "Two minutes of orientation so nothing is a surprise.", [
            ("3.1 All clouds: Home",
             "The first page. The four tiles say what **needs your attention**, how many replays are **pending approval**, what is dead-lettered, and how many clouds are connected. Below them each cloud has its own card. ServiceHub deliberately shows **no total across clouds**: one cloud counts exactly, one approximately, one not at all, so adding them would give a number that means nothing.",
             shots("01-home-all")),
            (f"3.2 {L}: Home and the menu",
             f"Pick **{L}** in the cloud switcher (top left). The cloud is part of the address, so a link shows someone exactly what you see. The menu has three groups: **PaaS Components** (the {L} services ServiceHub reads), **Operational Tools** (the pages you use every day, the same on every cloud) and **Configuration** with **What ServiceHub covers** at the bottom. The bar on top shows whether the Agent is running, and holds **Emergency stop**, **Search** and **Help**.",
             shots("04-home-cloud")),
        ]),
        (f"Part 4 — Find and replay a dead-lettered message on {L}", "The core loop: find it, understand it, replay it with a preview, check it held.", [
            ("4.1 Open the connection",
             f"In the menu choose **{cfg['store']}**, then your connection. The **Overview** says how many messages are dead-lettered now ({cfg['count_word']}), and lists every {cfg['entity_word']}. The tabs are: **Dead-letter messages**, **Recovery** (what was replayed and whether it held), **Access** (what ServiceHub may do here), **Configuration** and **Audit**.",
             shots("05-detail-overview")),
        ] + look + [
            ("4.3 The Dead-letter messages tab",
             "Every dead-lettered message on this connection. Group chips above the table count them by reason. Filter by queue or state, or search by message id or reason (never inside bodies). Tick one or more messages and press **Replay selected…**; a preview opens first. Press **Open** on a row to read one.",
             shots("06-dead-letters")),
            ("4.4 Open a message",
             "A side window opens beside the list. **Why it failed** gives the reason, the description, how many times it was tried, and how many others failed the same way. **Body**, **Properties** and **History** are the message exactly as stored. ServiceHub never changes a body. **Replay…** is at the bottom of the window.",
             shots("07-message")),
            ("4.5 The replay preview",
             "Nothing is sent yet. The preview shows the message, where it would go back to, and the safety checks that ran. **Replay 1 message** is the only button that changes anything. Cancel closes it.",
             shots("08-replay-preview")),
            ("4.6 The result, and whether it held",
             cfg["result_text"],
             shots("24-replay-result")),
        ]),
        ("Part 5 — When ServiceHub asks you", "The Agent never replays what it has not earned. When it is unsure it stops and asks.", [
            ("5.1 Needs your attention",
             "Everything on this cloud waiting for a person: replays the Agent is holding, and items ServiceHub handed off. The four tiles count them and show the oldest. Filter by component, family or age; **Review** opens the decision.",
             shots("09-attention")),
            ("5.2 Pending approvals",
             "The held replays on their own. Each row says which failure it was and why the Agent stopped (usually: this kind of failure has not yet earned automatic replay, so a person decides). Approving needs the **Approver** role.",
             shots("10-approvals")),
            ("5.3 Review: approve or decline",
             "Review explains why the Agent stopped, lists the messages that would be replayed, and offers **Approve** or **Decline…**. Declining needs a reason. Both answers are written to the evidence ledger.",
             shots("17-review")),
        ]),
        ("Part 6 — Afterwards", "A replay is not finished when it is sent. ServiceHub watches what happens next.", [
            ("6.1 Replay history",
             "Every replay, who or what started it, how many messages, and what ServiceHub saw afterwards: **Verified** (did not come back), **Watching** (still inside the window), **Came back** (failed again, so it is not fixed) or **Unverified** (sent, but this cloud cannot prove it held).",
             shots("12-replays")),
            ("6.2 Active messages",
             "The live queues, not the dead-letter ones. Read-only: it exists so you can see whether things are moving. Browse opens a queue's messages without changing them.",
             shots("11-active")),
            ("6.3 Failure signatures",
             "Failures grouped by how they fail, so a thousand messages become a handful of causes. For each: how many are stuck, whether replaying has helped before, and how much the Agent may do about it (its level of trust).",
             shots("14-signatures")),
        ]),
        ("Part 7 — Letting the Agent help", "Optional, and earned. Nothing is automatic until you create a rule and the failure has a record of verified fixes.", [
            ("7.1 Auto Replay rules",
             "A rule says: for this failure pattern, replay on your behalf, at this pace. A rule that has not earned trust only produces approvals. If results turn bad it switches itself off and asks a person.",
             shots("13-rules")),
            ("7.2 Agents",
             "The background workers: what each one does, whether it is healthy, when it last ran, and whether it may act or only observe. Pause any of them from here.",
             shots("16-agents")),
            ("7.3 Evidence ledger",
             "A tamper-evident record of every decision ServiceHub took, with who or what took it. Each entry is chained to the one before; export it and check it offline with `scripts/verify-recovery-chain.py`.",
             shots("15-ledger")),
        ]),
        ("Part 8 — Configuration, Help and safety", "Where the rest lives.", [
            ("8.1 Configuration",
             "How ServiceHub looks (**Preferences**), who may do what (**Access**), **Notifications**, **Backup** and restore, and the list of **Connections** again, with each cloud's own settings.",
             shots("21-configuration")),
            ("8.2 Help and Search",
             "**Help** (or press `?`) has answers by task, the step-by-step guides, and the keyboard shortcuts. **Search** (Cmd-K or Ctrl-K) finds a connection, a queue, a page or an action; it never searches inside messages.",
             shots("18-help", "19-search")),
            ("8.3 Emergency stop",
             "Switches off everything ServiceHub does on its own. It asks for a reason and for you to type STOP. A replay a person starts still works, and anything already sent stays sent.",
             shots("20-emergency-stop")),
            ("8.4 What ServiceHub covers",
             "Every component on every cloud, and how far each has been proven: **Supported** (seen working against a real cloud) or **Being validated** (built, not yet proven).",
             shots("22-coverage")),
        ]),
    ]
    # Number the steps in order inside each part, whichever optional steps are present.
    import re
    fixed = []
    for part, blurb, steps in out:
        n = int(re.match(r"Part (\d+)", part).group(1))
        fixed.append((part, blurb, [(re.sub(r"^\d+\.\d+ ", f"{n}.{i} ", t), x, sh) for i, (t, x, sh) in enumerate(steps, 1)]))
    return fixed
