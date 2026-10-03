"""Azure portal callouts. Coordinates are in the 1568x681 capture frame; SRC is the folder the raw (unredacted) captures were saved in."""
SRC = "/var/folders/6r/fwml6g_1175_vcn45v15b_f80000gn/T/claude-chrome-screenshots-Ah91HP/screenshot-"
EMAIL = (1380, 0, 1568, 33)  # account name + directory, top right
SPECS = {
 "portal-01-namespace-overview": ("1790933893443-4", [EMAIL, (410, 246, 625, 262)], [
   ((410, 291, 615, 308), "Host name — the address of your namespace. Your connection string begins Endpoint=sb:// followed by this name."),
   ((1064, 223, 1120, 239), "Pricing tier — Standard or Premium. Basic has no topics; queues work on every tier."),
   ((16, 299, 205, 347), "Settings → Shared access policies — where you create and copy the connection string ServiceHub uses."),
   ((16, 535, 108, 553), "Entities — your Queues and Topics live here."),
 ]),
 "portal-02-shared-access-policies": ("1790933901668-5", [EMAIL], [
   ((236, 112, 284, 134), "Add — creates a new policy. Make one just for ServiceHub, so you can revoke it without touching anything else."),
   ((258, 208, 404, 226), "RootManageSharedAccessKey — the namespace's default key, shared by everything. Do not hand this one to ServiceHub."),
   ((258, 235, 312, 252), "Any policy you already have for another tool — better not to share it. Give ServiceHub its own."),
 ]),
 "portal-03-add-policy": ("1790934479408-22", [EMAIL], [
   ((1338, 118, 1552, 138), "Policy name — for example servicehub-app, so you can recognise (and later revoke) it."),
   ((1338, 148, 1392, 166), "Manage — required. Azure lets only Manage list your queues and count their messages; without it ServiceHub cannot see them."),
   ((1338, 179, 1392, 197), "Send — used only when you press Replay, to put a copy of a dead letter back."),
   ((1338, 210, 1392, 228), "Listen — used to read queues and dead-letter queues."),
 ]),
 "portal-03b-add-policy-filled": ("1790934506002-23", [EMAIL], [
   ((1338, 118, 1552, 138), "The name you chose."),
   ((1338, 148, 1392, 228), "Manage, Send and Listen. Azure ticks Send and Listen for you when you tick Manage — Manage includes them."),
   ((1338, 645, 1406, 665), "Create — saves the policy and generates its keys."),
 ]),
 "portal-04-policy-created": ("1790934506003-24", [EMAIL], [
   ((258, 262, 334, 278), "Your new policy appears in the list."),
   ((894, 262, 996, 278), "Claims — Manage, Send, Listen."),
 ]),
 "portal-05-connection-string": ("1790934506003-25", [EMAIL, (1114, 479, 1546, 501)], [
   ((1113, 154, 1215, 247), "Claims — what this policy may do."),
   ((1113, 380, 1545, 402), "Primary connection string — click the eye to reveal, then the copy icon. Paste this into ServiceHub. Treat it like a password."),
   ((1520, 383, 1541, 401), "Copy to clipboard — copies the whole connection string."),
   ((1420, 100, 1478, 118), "Delete — the way to revoke ServiceHub's access later."),
 ]),
 "portal-06-queues": ("1790934063067-19", [EMAIL], [
   ((240, 240, 300, 257), "Queue name — ServiceHub finds every queue in the namespace; you do not list them."),
   ((842, 240, 856, 256), "Active messages — waiting to be processed."),
   ((1029, 240, 1043, 256), "Dead-letter messages — failed too often and were set aside. These are what ServiceHub shows on its Dead letters tab."),
 ]),
 "portal-07-queue-overview": ("1790934063067-20", [EMAIL], [
   ((245, 351, 340, 385), "Max delivery count — how many tries before Azure dead-letters a message. Here 3."),
   ((245, 436, 673, 472), "Message counts — Active and Dead-letter must match what ServiceHub shows."),
   ((232, 289, 490, 306), "Dead lettering on expiry is off — messages here are dead-lettered by delivery count or by your app, which is all ServiceHub needs."),
 ]),
}
