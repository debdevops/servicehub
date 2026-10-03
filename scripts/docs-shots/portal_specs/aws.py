"""AWS console callouts. Coordinates are in the 1568x681 capture frame; SRC is the folder the raw (unredacted) captures were saved in.
Redactions hide the account id (top-right account box, every ARN and URL) and the access key id."""
SRC = "/private/tmp/claude-501/-Users-debasisghosh-Github-servicehub/a765dcd3-e818-407c-9a00-e712d3864593/scratchpad/rawaws/screenshot-"
ACCOUNT = (1424, 0, 1568, 37)  # account name + id + user name, top right
SPECS = {
 "portal-01-queues": ("1790940581972-0", [ACCOUNT], [
   ((171, 274, 310, 322), "Queue names — your orders queue and its dead-letter queue (the one ending -dlq). ServiceHub finds every queue in the region; you do not list them."),
   ((690, 242, 765, 322), "Messages available — waiting to be received. The dead-letter queue's number is what ServiceHub counts as dead letters (here 324)."),
   ((1298, 14, 1408, 31), "Region — queues belong to a region. You choose the same region in ServiceHub when you connect (here Asia Pacific (Mumbai), ap-south-1)."),
 ]),
 "portal-02-orders-queue": ("1790940589853-1", [ACCOUNT, (1162, 277, 1251, 293), (826, 326, 909, 342)], [
   ((597, 311, 975, 357), "URL — the address of this queue. ServiceHub builds it for you from the queue's name and your region."),
   ((1020, 311, 1100, 341), "Dead-letter queue — Enabled: a message that fails too often is moved to a second queue instead of being lost."),
   ((791, 466, 904, 484), "Dead-letter queue tab — shows which queue receives the failures (next screenshot)."),
 ]),
 "portal-03-dead-letter-queue": ("1790940593970-2", [ACCOUNT, (1162, 277, 1251, 293), (826, 326, 909, 342), (297, 603, 386, 619)], [
   ((156, 586, 525, 620), "Queue — the dead-letter queue that receives the failures. ServiceHub reads this one to show Dead letters."),
   ((808, 586, 910, 620), "Maximum receives — 3: after three failed tries SQS moves the message here. Reading a dead letter from ServiceHub counts as one more receive, which is why ServiceHub only looks when you ask."),
 ]),
 "portal-04-iam-users": ("1790940611313-4", [ACCOUNT, (250, 228, 1550, 258), (1216, 267, 1346, 284)], [
   ((1451, 101, 1548, 131), "Create user — start here to make a user just for ServiceHub, not your own."),
   ((297, 266, 378, 283), "The ServiceHub user — separate from your own, so you can switch it off later without disturbing anything else."),
 ]),
 "portal-05-user-permissions": ("1790940621054-5", [ACCOUNT, (350, 186, 434, 202), (1120, 188, 1297, 202)], [
   ((693, 168, 790, 202), "Console access — Disabled. This user is for programs only: nobody can sign in to the AWS console as it."),
   ((1120, 168, 1300, 218), "Access key 1 — what ServiceHub signs in with. It is made on the Security credentials tab; AWS shows its secret only once."),
   ((315, 508, 426, 524), "servicehub-sqs — one inline policy, limited to the two queues (next screenshot). The user has no other permissions."),
 ]),
 "portal-06-policy-json": ("1790940631899-7", [ACCOUNT, (566, 545, 664, 579)], [
   ((410, 399, 620, 508), "Actions — what ServiceHub may do: read queue counts, receive (to look), change visibility (to hand a looked-at message back), send and delete (to replay). Above these, sqs:ListQueues lets it find your queues."),
   ((410, 546, 846, 580), "Resource — only these two queues. If the key ever leaked, it could touch nothing else in your account."),
 ]),
 "portal-07-access-keys": ("1790940651183-9", [ACCOUNT, (283, 342, 461, 358)], [
   ((1403, 265, 1531, 290), "Create access key — choose “Application running outside AWS”, then copy the Access key ID and the Secret access key. AWS shows the secret once."),
   ((907, 378, 962, 396), "Status — Active. Make it Inactive or delete it at any time to cut ServiceHub off."),
   ((1422, 337, 1514, 360), "Actions — make the key inactive or delete it: the way to revoke ServiceHub’s access later."),
 ]),
}
