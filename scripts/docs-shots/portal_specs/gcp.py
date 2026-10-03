"""Google Cloud console callouts. Coordinates are in the 1568-wide capture frame; SRC is the folder the raw (unredacted) captures were saved in.
Redactions hide the signed-in account (avatar), the free-trial credit banner, the owner's email, the service-account key ids and unique ids."""
SRC = "/private/tmp/claude-501/-Users-debasisghosh-Github-servicehub/a765dcd3-e818-407c-9a00-e712d3864593/scratchpad/rawgcp/screenshot-"
AVATAR = (1524, 40, 1560, 78)        # profile picture, top right (frames with the trial banner)
AVATAR_TOP = (1524, 2, 1560, 38)     # the same, in frames without the banner
BANNER = (0, 0, 1568, 40)            # "Upgrade your account … credit …" — billing detail
SPECS = {
 "portal-01-topics": ("1790950070690-12", [AVATAR_TOP], [
   ((163, 5, 270, 36), "Project picker — shows the project you are in. ServiceHub needs its Project ID, which is the part after projects/ in a topic's full name below (not the project's display name)."),
   ((274, 396, 420, 452), "Topic IDs — where messages are published. ServiceHub finds every topic in the project; you do not list them."),
   ((580, 396, 925, 452), "Topic name — the full path. The part after projects/ is your Project ID, the first thing you type into ServiceHub."),
 ]),
 "portal-02-subscriptions": ("1790950152471-18", [BANNER, AVATAR], [
   ((350, 420, 450, 508), "Subscription IDs — a queue in ServiceHub is a subscription together with its topic. The one ending -dlq-subscription holds your dead letters."),
   ((599, 420, 714, 476), "Topic — the topic each subscription reads from."),
   ((799, 420, 884, 478), "Ack deadline — how long a reader has to acknowledge a message before Pub/Sub delivers it again. ServiceHub hands a looked-at message straight back, so it never waits this out."),
 ]),
 "portal-03-subscription-dead-letter": ("1790950169810-19", [BANNER, AVATAR], [
   ((489, 296, 570, 320), "Dead lettering — the tab for the same policy, where you can change it."),
   ((261, 509, 820, 545), "Dead letter topic — where Pub/Sub forwards a message after it fails too often. ServiceHub reads this topic’s own subscription as your dead letters."),
   ((261, 550, 500, 568), "Maximum delivery attempts — after this many failed tries Pub/Sub forwards the message. Reading a dead letter from ServiceHub counts as one more attempt, which is why ServiceHub only looks when you ask."),
 ]),
 "portal-04-service-accounts": ("1790950184473-20", [BANNER, AVATAR, (884, 338, 1140, 355), (884, 378, 1140, 395), (1278, 338, 1422, 355), (1278, 378, 1422, 395)], [
   ((404, 116, 550, 136), "Create service account — start here to make an account just for ServiceHub, not your own."),
   ((294, 338, 470, 372), "The ServiceHub account — separate from your own and from the accounts your applications use, so you can switch it off later without disturbing anything else."),
   ((582, 338, 640, 355), "Status — Enabled. Disabling or deleting the account cuts ServiceHub off."),
 ]),
 "portal-05-iam-roles": ("1790950219329-22", [BANNER, AVATAR, (242, 432, 1535, 452)], [
   ((251, 342, 343, 361), "Grant access — where you give an account a role on the whole project."),
   ((342, 457, 1130, 475), "The ServiceHub account with Pub/Sub Viewer on the project — it lets ServiceHub list your topics and subscriptions. Pub/Sub Subscriber and Pub/Sub Publisher are granted on the individual subscriptions and topics (next screenshot)."),
 ]),
 "portal-06-topic-permissions": ("1790950137563-17", [BANNER, AVATAR], [
   ((1239, 272, 1351, 298), "Add principal — grants a role on just this topic. Do the same on each subscription for Pub/Sub Subscriber."),
   ((1240, 494, 1415, 584), "Pub/Sub Publisher — granted to the ServiceHub account on this topic only. It is used to put a replayed message back."),
   ((1240, 590, 1360, 608), "Pub/Sub Viewer — inherited from the project, so the account can see this topic."),
 ]),
 "portal-07-keys": ("1790950200768-21", [BANNER, AVATAR, (312, 85, 454, 99), (380, 486, 634, 503)], [
   ((242, 225, 1535, 272), "Google’s reminder that keys are a risk if they leak. Treat the file like a password, and delete the key when you no longer need it."),
   ((242, 416, 322, 442), "Add key → Create new key → JSON — downloads the key file ServiceHub needs. Google shows the private key only once."),
   ((254, 486, 862, 504), "The key — Active. The bin deletes it, which is how you revoke ServiceHub’s access later."),
 ]),
}
