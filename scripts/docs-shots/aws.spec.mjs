// AWS walkthrough: real screenshots of the shipped build against real SQS queues, numbered callouts, and a keys.json that the
// markdown + Help article are generated from. Run via run-aws.sh (fresh DB, drained orders queue, AWS key read from a file).
import fs from 'node:fs'
import { open, shot, finishRun, BASE, ROOT } from './lib.mjs'
const OUT = process.env.DOCS_OUT || `${ROOT}/docs/screenshots/aws`
const { browser, page } = await open()
const settle = (ms = 1500) => page.waitForTimeout(ms)
const go = async (path, ms) => { await page.goto(BASE + path); await settle(ms || 3000) }
const link = (n) => page.getByRole('link', { name: n, exact: true })
const btn = (n) => page.getByRole('button', { name: n, exact: true })
const dlg = () => page.getByRole('dialog')
const dismiss = async () => { await btn('Got it').click({ timeout: 1500 }).catch(() => {}); await settle(400) }
const ONLY = process.env.ONLY ? process.env.ONLY.split(',') : null
const scene = async (name, fn) => { if (ONLY && !ONLY.some((o) => name.startsWith(o))) return; try { await fn(); console.log('ok  ', name) } catch (e) { console.log('FAIL', name, String(e).split('\n')[0]) } }
const S = (name) => `${OUT}/${name}.png`
const scrollDialog = (y) => dlg().evaluate((d, y) => { const s = [...d.querySelectorAll('*')].find((e) => e.scrollHeight > e.clientHeight + 50 && getComputedStyle(e).overflowY !== 'visible'); (s || d).scrollTop = y }, y)

const [AK, SK] = fs.readFileSync(process.env.AWS_KEY_FILE, 'utf8').trim().split(/\s+/)
const EXAMPLE_KEY_ID = 'AKIAIOSFODNN7EXAMPLE' // AWS's documented example: the screenshots never show a real key id
const awsTab = () => page.getByRole('button', { name: /^AWS SQS/ })
const pickRegion = async () => { await dlg().getByText('Select a region').click(); await settle(400); await page.getByRole('option', { name: /ap-south-1/ }).click(); await settle(300) }
const scrollMain = (y) => page.evaluate((y) => { const els = [document.scrollingElement, ...document.querySelectorAll('main, main *')]; const sc = els.find((e) => e && e.scrollHeight > e.clientHeight + 50 && (e === document.scrollingElement || /(auto|scroll)/.test(getComputedStyle(e).overflowY))); (sc || document.scrollingElement).scrollTop = y }, y)
const region = (name) => page.getByRole('region', { name })
const onlyValidation = async () => { await page.getByRole('button', { name: /^\d+ ValidationError/ }).click(); await settle(800) } // a fixed kind of failure, so every replay screen shows the same story
const tabs = () => dlg().getByRole('button', { name: /^(Azure Service Bus|AWS SQS|Google Pub)/ })

// ---------- Part 2: connect ----------
await scene('01', async () => {
  await go('/')
  await shot(page, S('01-welcome'), [
    { n: 1, loc: page.getByRole('link', { name: 'Connect AWS' }), t: 'Connect AWS — opens the Add a cloud window on the AWS tab. It asks for an access key and a region; nothing is read until you press Connect there.' },
    { n: 2, loc: link('Add a cloud'), t: 'Add a cloud — the same window, from anywhere in the app. Use it later to connect Azure or Google Cloud as well.' },
    { n: 3, loc: page.getByRole('link', { name: 'Advanced' }), t: 'Simple | Advanced — Simple is where you act. Advanced is read-only pages (ledger, signatures, agents); it never changes anything.' },
    { n: 4, loc: link('Help'), t: 'Help — opens the Help panel with task-shaped answers and the keyboard shortcuts.' },
    { n: 5, loc: [page.getByRole('link', { name: 'Connect Azure' }), page.getByRole('link', { name: 'Connect Google' })], t: 'Connect Azure / Connect Google — the same Add a cloud window, on the Azure or Google Cloud tab. You do not need them for AWS.' },
    { n: 6, loc: page.getByRole('link', { name: 'Try it with sample data' }), t: 'Try it with sample data — opens a demo with made-up data and no cloud connected, so you can look around first. Nothing in it is real and nothing is sent anywhere.' },
  ])
})
await scene('02', async () => {
  await go('/?modal=add-cloud', 1500); await awsTab().click(); await settle(500)
  await shot(page, S('02-add-cloud'), [
    { n: 1, loc: tabs(), all: true, t: 'Cloud tabs — AWS SQS / SNS is selected. The Azure and Google tabs ask for different credentials.' },
    { n: 2, loc: page.getByText('Where do I get the AWS access key?'), t: 'Where do I get the AWS access key? — expands the console steps, with the permissions to give.' },
    { n: 3, loc: page.getByPlaceholder('orders-dev'), t: 'Name — what you will see in ServiceHub. It is a label only; it does not change anything in AWS.' },
    { n: 4, loc: dlg().locator('input[type=radio]'), all: true, t: 'Environment — Development, UAT or Production. Start with Development; Auto Replay never acts in Production.' },
    { n: 5, loc: page.getByPlaceholder('AKIAIOSFODNN7EXAMPLE'), t: 'Access key ID — the ID of the access key you made for ServiceHub (it starts AKIA).' },
    { n: 6, loc: page.getByLabel('Secret access key'), t: 'Secret access key — shown by AWS only once, when you create the key. It is encrypted on this server and never shown again.' },
    { n: 7, loc: btn('Show value'), t: 'Show value — reveals what you typed so you can check it. It cannot reveal a stored secret.' },
    { n: 8, loc: dlg().getByText('Select a region'), t: 'Region — the region your queues are in (the console shows it top right). Queues in other regions are not seen.' },
    { n: 9, loc: dlg().getByText(/To watch:/), t: 'The permissions ServiceHub uses — to watch, and (in the second sentence) also to replay. Give it nothing more.' },
    { n: 10, loc: dlg().getByRole('link', { name: /^Help for/ }), t: '(?) — opens Help on this very screen. Every window has one.' },
    { n: 11, loc: dlg().getByRole('button', { name: 'Close' }), t: '✕ — closes the window without saving (Esc does the same).' },
  ])
  await page.getByText('Where do I get the AWS access key?').click(); await settle(600)
  await shot(page, S('02-add-cloud-help'), [
    { n: 1, loc: page.getByText(/In the AWS console open/).first(), t: 'IAM → Users — in the AWS console, make a user just for ServiceHub (not your own).' },
    { n: 2, loc: page.getByText(/Attach a policy/).first(), t: 'Attach a policy with only the SQS permissions below, limited to the queues you want watched.' },
    { n: 3, loc: page.getByText(/Security credentials/).first(), t: 'Security credentials → Create access key — copy the Access key ID and the Secret access key; AWS shows the secret once.' },
    { n: 4, loc: page.getByText(/Use the region your queues/).first(), t: 'Use the region your queues are in.' },
    { n: 5, loc: [tabs(), dlg().locator('input'), btn('Show value'), dlg().locator('summary'), dlg().getByText('Select a region')], all: true, t: 'The form underneath — the cloud tabs, Name, Environment, Access key ID, Secret access key, Show value and Region, exactly as in the previous screenshot.' },
  ])
})
await scene('03', async () => {
  await go('/?modal=add-cloud', 1500); await awsTab().click(); await settle(500)
  await page.getByPlaceholder('orders-dev').fill('AWS Dev')
  await page.getByPlaceholder('AKIAIOSFODNN7EXAMPLE').fill(EXAMPLE_KEY_ID)
  await page.getByLabel('Secret access key').fill('x'.repeat(40))
  await pickRegion()
  await shot(page, S('03-add-cloud-filled'), [
    { n: 1, loc: page.getByPlaceholder('orders-dev'), t: 'A name you will recognise.' },
    { n: 2, loc: page.getByPlaceholder('AKIAIOSFODNN7EXAMPLE'), t: 'The Access key ID (AWS’s documented example is shown here, not a real key).' },
    { n: 3, loc: page.locator('input[type=password]'), t: 'The Secret access key, hidden as you paste it.' },
    { n: 4, loc: dlg().getByRole('button', { name: /ap-south-1/ }).or(dlg().getByText(/ap-south-1/)), t: 'The region — here ap-south-1, Asia Pacific (Mumbai).' },
    { n: 6, loc: [btn('Cancel'), btn('Connect')], t: 'Cancel and Connect — shown in full on the next screenshot.' },
    { n: 5, loc: [tabs(), dlg().locator('input[type=radio]'), btn('Show value'), dlg().locator('summary')], all: true, t: 'The rest of the form above — cloud tabs, Environment and Show value, as described on the previous screenshot.' },
  ])
  await scrollDialog(99999); await settle(500)
  await shot(page, S('03b-add-cloud-bottom'), [
    { n: 1, loc: dlg().getByText(/To watch:/), t: 'The permissions ServiceHub uses — to watch, and (second sentence) also to replay. Give it nothing more.' },
    { n: 2, loc: btn('Cancel'), t: 'Cancel — closes the window; nothing is saved.' },
    { n: 3, loc: btn('Connect'), t: 'Connect — tests the key, then saves it. It only reads queue counts; it never receives, sends or deletes anything by itself.' },
    { n: 4, loc: [dlg().getByText('ap-south-1'), dlg().getByRole('button', { name: /ap-south-1/ })], t: 'Region — ap-south-1 (Asia Pacific, Mumbai) is chosen here.' },
    { n: 5, loc: [dlg().locator('summary'), dlg().locator('input'), btn('Show value')], all: true, t: 'The fields above — the name, key ID, secret and Show value, as filled in on the previous screenshot.' },
  ])
  await page.getByPlaceholder('AKIAIOSFODNN7EXAMPLE').fill(AK); await page.getByLabel('Secret access key').fill(SK)
  await btn('Connect').click(); await settle(8000)
  await shot(page, S('04-connected'), [
    { n: 1, loc: page.getByText(/Connected — ServiceHub can see/), t: 'The result: how many queues ServiceHub found, how many messages are dead-lettered right now, and whether this cloud can prove a replayed message stayed fixed (AWS cannot yet — it will say so honestly).' },
    { n: 2, loc: page.getByRole('button', { name: 'Open Home' }).or(page.getByRole('link', { name: 'Open Home' })), t: 'Open Home — closes the window and shows Home.' },
  ])
})

// ---------- Part 3: use ----------
await scene('05', async () => {
  await go('/')
  await shot(page, S('05-home'), [
    { n: 1, loc: page.getByText('Last 24 hours').first().locator('..'), t: 'Window — the period Home counts over (24 hours, 7 or 30 days). It changes what you see, never what happens.' },
    { n: 2, loc: [page.locator('main').getByText('Connected', { exact: true }), page.getByText(/^Region ap-south-1/)], t: 'Connected, and the region — ServiceHub can reach AWS with your key; the region is the one you chose.' },
    { n: 3, loc: page.getByText(/Counts messages/).locator('..'), t: 'What this cloud can do — ticks show what AWS lets ServiceHub do safely. AWS counts messages and records dead letters when you look; it cannot browse active messages or prove a fix held.' },
    { n: 4, loc: btn('Look now'), t: 'Look now — asks AWS for its dead letters (up to 100 per queue) and keeps them in ServiceHub. Each one read counts as one delivery attempt, which is why ServiceHub never does it on its own.' },
    { n: 5, loc: link('See dead letters →'), t: 'Dead letters — opens the list of messages that failed and were set aside.' },
    { n: 6, loc: link('See active messages →'), t: 'Active messages — on AWS this shows counts per queue only; opening a message there would count as a delivery.' },
    { n: 7, loc: link('See what was replayed →'), t: 'Replayed — everything put back, by whom, and how it went.' },
    { n: 8, loc: link('Manage rules →'), t: 'Auto Replay rules — opens the rules panel. On AWS a rule always waits for a person, because AWS cannot prove a fix held.' },
    { n: 9, loc: btn('Got it'), t: 'Got it — hides this explanation. It does not affect your data.' },
    { n: 10, loc: link('See all dead letters in AWS →'), t: 'See all dead letters in AWS — opens the Dead letters list for this cloud.' },
    { n: 11, loc: page.getByRole('link', { name: 'Help for this page' }), t: 'Help for this page (the book beside the title) — opens Help on this very page, over it. Every page has one.' },
    { n: 12, loc: page.getByRole('link', { name: 'Demo', exact: true }), t: 'Demo — a guided walk through the whole product with made-up data. No connection string is needed and nothing real is touched.' }
  ])
})
await scene('05b', async () => {
  await go('/'); await dismiss()
  await shot(page, S('05b-navigation'), [
    { n: 1, loc: btn('Back'), t: 'Back — returns to the previous place in the app.' },
    { n: 2, loc: btn('Forward'), t: 'Forward — goes to the place you came back from.' },
    { n: 3, loc: page.getByRole('button', { name: /Search clouds/ }), t: 'Search (⌘K) — jump to a cloud, queue or page. Searching never changes anything.' },
    { n: 4, loc: page.getByRole('button', { name: /waiting for you|needs you/ }).first(), t: 'The bell — the only place the Agent asks you something. A number appears when it needs you.' },
    { n: 5, loc: page.getByRole('link', { name: 'Simple', exact: true }), t: 'Simple — the two pages where you do the work.' },
    { n: 6, loc: page.getByRole('link', { name: 'Advanced', exact: true }), t: 'Advanced — four read-only pages.' },
    { n: 7, loc: page.getByText('This browser').locator('..').locator('..'), t: 'You — who ServiceHub records actions as, with your role.' },
    { n: 8, loc: link('Dead letters'), t: 'Dead letters — messages that failed, with the reason and a Replay button.' },
    { n: 9, loc: link('Active messages'), t: 'Active messages — what is waiting now, counted per queue on AWS.' },
    { n: 10, loc: link('Replayed'), t: 'Replayed — what was put back and whether it stayed fixed.' },
    { n: 11, loc: link('Auto Replay'), t: 'Auto Replay — rules for retries ServiceHub may do on its own.' },
    { n: 12, loc: page.getByText('Connected · 1 namespace').locator('..').locator('..'), t: 'Your cloud — pick it to see only that cloud. The dot shows the connection is healthy.' },
    { n: 13, loc: link('Connections'), t: 'Connections — your connected clouds, to test or remove.' },
    { n: 14, loc: link('Add a cloud'), t: 'Add a cloud — connect another cloud.' },
    { n: 15, loc: link('Settings'), t: 'Settings — connections, notifications, preferences, access and backup.' },
    { n: 16, loc: link('Help'), t: 'Help — answers and shortcuts.' },
    { n: 17, loc: link('Home'), t: 'Home — the overview: what needs attention, what the Agent is doing, and how replays ended.' },
  ])
})
await scene('06', async () => {
  await go('/?tab=dlq'); await dismiss(); await page.getByText(/Nothing has been recorded for AWS yet/).waitFor(); await settle(500)
  await shot(page, S('06-dead-letters'), [
    { n: 1, loc: page.getByRole('button', { name: /AWS · Namespace/ }), t: 'Namespace picker — all of AWS, or one account and region.' },
    { n: 2, loc: page.getByText(/ServiceHub doesn’t look in AWS on its own/).locator('xpath=ancestor::div[.//button[contains(., "Look at")]][1]'), t: 'Why the list starts empty — on AWS, reading a dead letter counts as one delivery attempt, so ServiceHub only looks when you ask. It reads up to 100 per queue and keeps them here.' },
    { n: 3, loc: btn('Look at AWS’s dead letters now'), t: 'Look at AWS’s dead letters now — receives up to 100 dead letters per queue, records them, and hands them straight back. It never deletes or moves anything; it does add one to each message’s delivery count.' },
    { n: 4, loc: page.getByText(/Nothing has been recorded for AWS yet/), t: 'The empty list — “nothing recorded” does not mean there are none: the queue holds 324. Press Look now to see them.' },
    { n: 5, loc: page.getByRole('link', { name: 'Help for this page' }), t: 'Help for this page — opens Help on this page’s step of the guide. The (?) beside it only re-shows the short explanation.' },
    { n: 6, loc: [page.locator('main').getByRole('link', { name: /^(Dead letters|Active|Replayed)/ }), btn('What am I looking at?')], all: true, t: 'The three tabs — Dead letters, Active and Replayed — switch the list below. The (?) beside the title brings back the short “What you’re looking at” card.' },
    { n: 7, loc: [page.locator('main button').filter({ hasText: /^(Stuck now|All time|All queues & topics)$/ }), page.getByPlaceholder(/Search ID/), btn('Refresh')], all: true, t: 'Filters and Refresh — Showing, Time window, queue or topic and search narrow the list; Refresh re-reads what ServiceHub has recorded. On AWS Refresh does not look at AWS: only Look now does.' },
  ])
})
await scene('06b', async () => {
  await go('/?tab=dlq'); await dismiss()
  await btn('Look at AWS’s dead letters now').click(); await page.getByText(/dead letters recorded/).waitFor({ timeout: 60000 }); await settle(1500); await dismiss()
  await shot(page, S('06b-after-look'), [
    { n: 1, loc: page.getByText(/Looked at \d/), t: 'What the look found — when it ran, how many queues had dead letters, and how many new ones were recorded.' },
    { n: 2, loc: page.getByText(/AWS hands back a sample/), t: 'AWS hands back a sample, not the whole queue: a dead letter recorded earlier and not seen this time stays in the list; it is not assumed gone.' },
    { n: 3, loc: [page.getByText('PaymentTimeout', { exact: true }), page.getByText('DownstreamUnavailable', { exact: true }), page.getByText('Other errors', { exact: true }), page.getByText('Total messages', { exact: true })], t: 'Why they failed — the biggest reasons among the dead letters recorded, counted.' },
    { n: 4, loc: page.getByRole('link', { name: /^Dead letters\s*\d*/ }).last(), t: 'Dead letters tab — the number is how many ServiceHub has recorded (not how many AWS holds).' },
    { n: 5, loc: btn('Look at AWS’s dead letters now'), t: 'Look again — records any dead letters not seen before. Each look adds one delivery attempt to the messages it reads.' },
    { n: 6, loc: page.getByRole('button', { name: /^\d+ [A-Za-z]/ }), all: true, t: 'Reason chips — click one to show only that reason.' },
    { n: 7, loc: [page.getByRole('button', { name: /AWS · Namespace/ }), btn('What am I looking at?'), page.locator('main').getByRole('link', { name: /^(Active|Replayed)/ })], all: true, t: 'The namespace picker, the (?) that re-shows the short explanation, and the Active and Replayed tabs.' },
    { n: 8, loc: [page.locator('main button').filter({ hasText: /^(Stuck now|All time|All queues & topics)$/ }), page.getByPlaceholder(/Search ID/), btn('Replay All Messages'), btn('Refresh')], all: true, t: 'Filters, Replay All Messages and Refresh — described on the next screenshot, where the table is in view.' },
    { n: 9, loc: page.getByRole('region', { name: 'Selected messages' }).first(), t: 'The selection bar — the tick selects the whole page; View details chooses columns; Replay selected stays dimmed until something is ticked.' },
    { n: 10, loc: page.getByRole('checkbox', { name: /Select all on this page/ }), t: 'The tick in the table heading — ticks every row on this page.' },
    { n: 11, loc: page.locator('table thead'), t: 'The table headings — each has an ⓘ that explains that column in a sentence; the next screenshot shows the table itself.' },
  ])
})

await scene('06c', async () => {
  await go('/')
  await scrollMain(520); await settle(600)
  await shot(page, S('05c-home-middle'), [
    { n: 1, loc: region('Why messages failed'), t: 'Why messages failed — the reasons among the dead letters ServiceHub has recorded, biggest first, with how many each has. It only counts what you have looked at.' },
    { n: 2, loc: region('How replays ended'), t: 'How replays ended — what happened to the replays in this window. On AWS they stay “watching”, because AWS cannot prove a fix held.' },
    { n: 3, loc: [region('Queues and topics needing attention'), page.getByRole('link', { name: /^Open/ })], all: true, t: 'Queues and topics needing attention — the queues with the most dead letters first. Open → shows that queue’s dead letters.' },
    { n: 4, loc: region('Latest recorded dead letters'), t: 'Latest recorded dead letters — the five most recent ServiceHub has recorded, each with Details →. “See all →” opens the full list.' },
    { n: 5, loc: region('Recent activity'), t: 'Recent activity — shown in full on the next screenshot.' },
  ])
  await scrollMain(99999); await settle(600)
  await shot(page, S('05d-home-end'), [
    { n: 1, loc: region('Latest recorded dead letters'), t: 'The five latest dead letters, each with Details → — the same message view as on the Dead letters tab.' },
    { n: 2, loc: region('Recent activity'), t: 'Recent activity — what ServiceHub and people did, newest first (Live means it updates by itself). Each line opens to show its detail; nothing here changes anything.' },
    { n: 3, loc: [region('Why messages failed'), region('Queues and topics needing attention'), page.getByRole('link', { name: /^Open/ })], all: true, t: 'Each reason in “Why messages failed” is a link that opens the Dead letters list filtered to that reason; Open → opens that queue’s dead letters.' },
  ])
})
await scene('07', async () => {
  await go('/?tab=dlq'); await dismiss(); await page.mouse.wheel(0, 340); await settle(600)
  await shot(page, S('07-filters-and-table'), [
    { n: 1, loc: page.getByText('Stuck now', { exact: true }), t: 'Showing — Stuck now, or messages that have since left the queue.' },
    { n: 2, loc: page.getByText('All time', { exact: true }), t: 'Time window — only messages set aside in this period.' },
    { n: 3, loc: page.getByPlaceholder(/Search ID/), t: 'Search — by message ID, queue, reason or error text.' },
    { n: 4, loc: btn('Replay All Messages'), t: 'Replay All Messages — opens a preview of every message still stuck in the cloud and namespace you are viewing. It ignores the filters, time window and search here. Nothing is sent until you confirm the preview.' },
    { n: 5, loc: [btn('Refresh'), btn('About Refresh')], t: 'Refresh — reads the queue again. It re-reads what ServiceHub has already recorded; it does not look at AWS (only Look now does). The ⓘ beside it says when it was last updated.' },
    { n: 6, loc: page.getByText('No messages selected'), t: 'Selection — tick rows to act on several; this line shows how many.' },
    { n: 7, loc: page.getByText('Replay selected…').first(), t: 'Replay selected — opens the same preview for just the ticked messages.' },
    { n: 8, loc: page.getByRole('link', { name: /Details of message/ }).first(), t: 'Details — opens the message: why it failed, its body, properties and delivery history.' },
    { n: 9, loc: page.getByRole('link', { name: /Replay message/ }).first(), t: 'Replay — opens the proposal for this one message. It does not send anything yet.' },
    { n: 10, loc: [page.locator('table thead'), page.locator('table tbody tr')], all: true, t: 'The table — a tick on every row (the one in the heading ticks the whole page), Details and Replay on every row, and an ⓘ About button on each column heading that explains that column in a sentence. “View details” in the heading chooses which columns show.' },
    { n: 11, loc: page.getByRole('button', { name: /^\d+ [A-Z]/ }), all: true, t: 'Reason chips — each shows a reason and how many messages have it. Click one to show only those; click it again to show all.' },
    { n: 12, loc: btn('All queues & topics'), t: 'All queues & topics — limit the list to one queue or topic.' },
    { n: 13, loc: page.getByRole('region', { name: 'Selected messages' }).first(), t: 'The selection bar — the tick selects every row on this page; View details chooses which columns show; Replay selected stays dimmed until something is ticked.' },
    { n: 14, loc: btn('Look at AWS’s dead letters now'), t: 'Look at AWS’s dead letters now — records any dead letters not seen before. Each look adds one delivery attempt to the messages it reads.' },
  ])
  await scrollMain(99999); await settle(600)
  await shot(page, S('07c-table-end'), [
    { n: 1, loc: [page.locator('table tbody tr'), page.getByRole('region', { name: /bottom of the table/ })], all: true, t: 'The foot of the table — the same ticks, Details and Replay on every row, and the selection bar repeated underneath.' },
    { n: 2, loc: page.getByRole('button', { name: /^(Rows per page|Previous page|Go to page \d+|Next page)$/ }), all: true, t: 'Paging — rows per page, previous, the page numbers and next. The list holds up to 100 dead letters per queue from each look.' },
  ])
})
await scene('08', async () => {
  await go('/?tab=dlq'); await dismiss(); await onlyValidation(); await page.getByRole('link', { name: /Details of message/ }).first().click(); await settle(2500)
  await shot(page, S('08-message-details'), [
    { n: 1, loc: page.getByRole('button', { name: /Expand/ }), t: 'Expand — widens the panel for long messages.' },
    { n: 2, loc: page.getByRole('tab', { name: 'Overview' }), t: 'Overview — reason, why it failed, the body with the bad field marked.' },
    { n: 3, loc: page.getByRole('tab', { name: 'Delivery' }), t: 'Delivery — how many times the message was received (ServiceHub’s own looks count) and when AWS set it aside.' },
    { n: 4, loc: page.getByText(/^Why it failed/).first(), t: 'Why it failed — a plain-words reading of the recorded reason. Marked Suggestion: it is a reading, not something AWS reported.' },
    { n: 5, loc: btn('Raw'), t: 'Formatted / Raw — switch the body view.' },
    { n: 6, loc: page.getByRole('link', { name: /Replay this message/ }).or(btn('Replay this message')), t: 'Replay this message — opens the proposal. Nothing is sent from here.' },
    { n: 7, loc: [page.getByRole('tab', { name: 'Body' }), page.getByRole('tab', { name: 'Properties' }), page.getByRole('tab', { name: 'Headers' })], t: 'Body, Properties and Headers — the other tabs; each is shown below.' },
    { n: 8, loc: [btn('Formatted'), btn('Copy body')], t: 'Formatted and Copy body — pretty-print the body, or copy it to your clipboard. The copy stays in your browser.' },
    { n: 9, loc: btn('Copy message ID'), t: 'Copy message ID — copies the ID so you can search for it elsewhere.' },
  ])
  await scrollDialog(99999); await settle(600)
  await shot(page, S('08b-message-details-end'), [
    { n: 1, loc: page.getByRole('link', { name: /Replay this message/ }).or(btn('Replay this message')), t: 'Replay this message — opens the proposal. Nothing is sent from here.' },
    { n: 2, loc: btn('Purge instead…'), t: 'Purge instead… — for a message not worth replaying. It only opens a small form: you give a reason, and nothing is deleted until you press “Purge for good”. Deleting is permanent, goes through the same checks as a replay, and is recorded with your name and reason.' },
    { n: 3, loc: dlg().getByRole('button', { name: /^(Formatted|Raw|Copy body|Copy message ID|Expand)/ }), all: true, t: 'Expand, the body view and the copy buttons — as on the previous screenshots.' },
  ])
  await scrollDialog(0); await settle(400)
  for (const [t, d] of [['Body', 'The message body exactly as it was sent.'], ['Properties', 'The message attributes the sender attached (for example shs-error-type) and the SQS system attributes.'], ['Headers', 'What SQS recorded: the sent time, the first receive time, how many times it was received, and which queue it was dead-lettered from.'], ['Delivery', 'How many times AWS delivered it before setting it aside. ServiceHub’s own looks are counted in this number.']]) {
    await page.getByRole('tab', { name: t }).click(); await settle(700)
    const others = page.getByRole('tab').filter({ hasNotText: t })
    await shot(page, S(`08-message-details-${t.toLowerCase()}`), [
      { n: 1, loc: page.getByRole('tab', { name: t }), t: d },
      { n: 2, loc: [btn('Expand'), others], all: true, t: 'Expand widens the panel; the other tabs (Overview, Body, Properties, Headers, Delivery) switch what is shown.' },
      ...(t === 'Body' ? [{ n: 3, loc: [btn('Formatted'), btn('Raw'), btn('Copy body')], t: 'Formatted / Raw switch the view; Copy body copies it to your clipboard.' }] : []),
    ])
  }
})
await scene('09', async () => {
  await go('/?tab=dlq'); await dismiss(); await onlyValidation(); await page.getByRole('link', { name: /Replay message/ }).first().click(); await settle(3500)
  await shot(page, S('09-replay-proposal'), [
    { n: 1, loc: page.getByText('THE MESSAGE'), t: 'The message — what is about to be replayed: ID, where it is stuck, when and why it was set aside, and its body.' },
    { n: 2, loc: dlg().getByRole('button', { name: 'Replay 1 message' }), all: true, t: 'Replay 1 message — sends it back. It is only sent when you press this.' },
    { n: 3, loc: dlg().getByRole('button', { name: /^(The message|What will happen|Safety checks|After it runs)/ }), all: true, t: 'What will happen, Safety checks, After it runs — fold open or closed; the next screenshot shows them open.' },
    { n: 4, loc: dlg().getByRole('button', { name: 'Cancel' }), t: 'Cancel — closes without sending.' },
  ])
  await scrollDialog(420); await settle(500)
  await shot(page, S('09-replay-proposal-middle'), [
    { n: 1, loc: dlg().getByRole('button', { name: /^(The message|What will happen|Safety checks|After it runs)/ }), all: true, t: 'The four sections of the proposal — The message, What will happen, Safety checks and After it runs. Each folds open or closed; nothing here sends anything.' },
    { n: 2, loc: [dlg().getByRole('button', { name: /^Replay 1 message/ }), dlg().getByRole('button', { name: 'Cancel' })], all: true, t: 'Replay 1 message sends it; Cancel closes without sending. Both are repeated at the foot of the window so they are always in reach.' },
  ])
  await scrollDialog(99999); await settle(500)
  await shot(page, S('09-replay-proposal-checks'), [
    { n: 1, loc: page.getByText('WHAT WILL HAPPEN'), t: 'What will happen — where it goes and what happens to the dead-letter copy (it is removed once AWS accepts the new copy).' },
    { n: 2, loc: page.getByText('SAFETY CHECKS'), t: 'Safety checks — every check that must pass; a failing one blocks the replay and says why.' },
    { n: 3, loc: page.getByText('AFTER IT RUNS'), t: 'After it runs — ServiceHub records it and watches for 24 hours; if it comes back, nothing retries it.' },
    { n: 4, loc: btn('Cancel'), t: 'Cancel — closes without sending.' },
    { n: 5, loc: dlg().getByRole('button', { name: /^(Replay 1 message|The message|What will happen|Safety checks|After it runs)/ }), all: true, t: 'The folding headings and the Replay button repeated at the end of the window.' },
  ])
})
await scene('13', async () => {
  await go('/?tab=dlq'); await dismiss(); await onlyValidation(); await scrollMain(0); await settle(600)
  await shot(page, S('07a-reason-filter'), [
    { n: 1, loc: [page.getByRole('button', { name: /^\d+ ValidationError/ }), btn('show all reasons')], t: 'Filtering by a reason — the chosen reason is highlighted and “show all reasons” clears the filter. The list below shows only that reason.' },
    { n: 2, loc: page.getByRole('button', { name: /^\d+ [A-Za-z]/ }), all: true, t: 'The reason chips — click one to show only that reason.' },
    { n: 3, loc: [page.getByRole('button', { name: /AWS · Namespace/ }), btn('What am I looking at?'), page.locator('main').getByRole('link', { name: /^(Dead letters|Active|Replayed)/ })], all: true, t: 'The namespace picker, the (?) that re-shows the short explanation, and the three tabs.' },
    { n: 4, loc: [page.locator('main button').filter({ hasText: /^(Stuck now|All time|All queues & topics)$/ }), page.getByPlaceholder(/Search ID/), btn('Replay All Messages'), btn('Refresh'), btn('Look at AWS’s dead letters now')], all: true, t: 'Filters, Replay All Messages, Refresh and Look now — described on the earlier screenshots.' },
    { n: 5, loc: [page.getByRole('region', { name: 'Selected messages' }).first(), page.getByRole('checkbox', { name: /Select all on this page/ }), page.locator('table thead'), page.locator('table tbody tr')], all: true, t: 'The selection bar (which now offers “Select all” for this reason), the table headings and the rows, as on the earlier screenshots.' },
  ])
  await scrollMain(99999); await settle(600)
  await shot(page, S('07a-reason-filter-end'), [
    { n: 1, loc: btn('show all reasons'), t: 'Show all reasons — at the foot of a filtered list too: clears the reason filter and brings every recorded dead letter back.' },
    { n: 2, loc: [page.locator('table tbody tr'), page.getByRole('region', { name: /bottom of the table/ })], all: true, t: 'The rows of this reason only, and the selection bar repeated underneath.' },
    { n: 3, loc: page.getByRole('button', { name: /^(Rows per page|Previous page|Go to page \d+|Next page)$/ }), all: true, t: 'Paging — rows per page, previous, the page numbers and next.' },
  ])
  await page.getByRole('checkbox').nth(2).check(); await page.getByRole('checkbox').nth(3).check(); await settle(500)
  await shot(page, S('07b-selection'), [
    { n: 1, loc: page.getByRole('checkbox').nth(2), t: 'Row tick — choose which messages to act on. The header tick chooses every row on the page.' },
    { n: 2, loc: page.getByText(/2 (messages )?selected/).first(), t: 'Selection count — how many messages are ticked.' },
    { n: 3, loc: page.getByRole('button', { name: 'Clear' }).first(), t: 'Clear — unticks everything. Nothing else changes.' },
    { n: 4, loc: page.getByRole('link', { name: /Replay selected/ }).first(), t: 'Replay selected — opens the bulk preview for just these messages. Nothing is sent yet.' },
    { n: 5, loc: [page.getByRole('button', { name: /^\d+ [A-Z]/ }), btn('Showing'), btn('Time window'), btn('All queues & topics'), page.getByPlaceholder(/Search ID/), btn('Replay All Messages'), btn('Refresh'), btn('Look at AWS’s dead letters now')], all: true, t: 'The filter row — reason chips, Showing, Time window, queue, search, Replay All Messages and Refresh, each explained on the previous screenshot.' },
    { n: 6, loc: [page.locator('table thead'), page.locator('table tbody tr')], all: true, t: 'The table — the ticks, Details and Replay on every row, and the whole-page tick in the heading.' },
    { n: 7, loc: page.getByRole('region', { name: 'Selected messages' }).first(), t: 'The selection bar — the whole-page tick and View details (which columns show).' },
  ])
  await page.getByRole('link', { name: /Replay selected/ }).first().click(); await settle(2500)
  await shot(page, S('13-bulk-replay'), [
    { n: 1, loc: page.getByText('PREVIEW — NOTHING HAS RUN YET'), t: 'Preview — nothing has run yet. Step 1 of Preview → Run → Watch.' },
    { n: 2, loc: page.getByText('held back — stays in dead letters').locator('..'), t: 'Held back — messages that fail a safety check stay in dead letters; they are never forced.' },
    { n: 3, loc: page.getByText('GROUPED BY HOW THEY FAILED'), t: 'Grouped by how they failed — so you can see whether you are fixing one problem or several.' },
    { n: 4, loc: page.getByText('WHERE EACH ONE GOES'), t: 'Where each one goes — the queue each message will be sent back to.' },
    { n: 5, loc: dlg().getByRole('button', { name: /Replay \d+ messages/ }), all: true, t: 'Replay N messages — sends them one at a time, re-checking each. You can stop partway.' },
    { n: 6, loc: dlg().getByRole('button', { name: 'Cancel' }), all: true, t: 'Cancel — nothing is sent.' },
    { n: 7, loc: [dlg().getByText(/usually fail again/), dlg().getByRole('button', { name: /Replay a sample/ })], t: 'A warning when the failures look like bad data — Replay one first? sends a sample of one, so you can see whether it holds before sending the rest.' },
    { n: 8, loc: dlg().getByRole('button', { name: /^(Previous page|Go to page|Next page)/ }), all: true, t: 'Page controls — move through the list when more messages are chosen than fit on one page.' },
  ])
  await scrollDialog(99999); await settle(500)
  await shot(page, S('13b-bulk-replay-end'), [
    { n: 1, loc: dlg().getByRole('button', { name: 'How it will run' }), t: 'How it will run — the pace (a few messages a second, gentle on your consumer) and the automatic stop: five sends in a row that are not accepted.' },
    { n: 2, loc: dlg().getByRole('button', { name: /After they are sent back/ }), t: 'After they are sent back — folds open to say what ServiceHub does next: it keeps watching, and on AWS it records each one as “verification required”, never as fixed.' },
    { n: 3, loc: dlg().getByRole('button', { name: /Purge instead/ }), t: 'Purge instead… — for messages not worth replaying. It does not delete anything yet: it opens a separate step that asks why, shows what would be purged, and needs you to type a confirmation. Deleting is for good, and each purge is recorded with your name and reason.' },
    { n: 4, loc: dlg().getByRole('button', { name: /^(Previous page|Go to page 1|Next page)$/ }), all: true, t: 'Page controls — move through the list when more messages are chosen than fit on one page.' },
    { n: 5, loc: dlg().getByRole('button', { name: /Replay \d+ messages/ }), all: true, t: 'Replay N messages — sends them one at a time, re-checking each. You can stop partway.' },
    { n: 6, loc: dlg().getByRole('button', { name: 'Cancel' }), all: true, t: 'Cancel — nothing is sent.' },
  ])
  await page.keyboard.press('Escape'); await settle(500)
})
await scene('10', async () => {
  await go('/?tab=dlq'); await dismiss(); await onlyValidation(); await page.getByRole('link', { name: /Replay message/ }).first().click(); await settle(3000)
  await dlg().getByRole('button', { name: 'Replay 1 message' }).last().click(); await settle(4000)
  await shot(page, S('10-replay-result'), [
    { n: 1, loc: page.getByText('Sent back', { exact: true }), t: 'The result, in plain words: it was sent back, and ServiceHub will watch for it coming back.' },
    { n: 2, loc: page.getByText('Recorded — you can find it under Replayed.').first(), t: 'It is recorded, with who did it, and shows under Replayed (and in Advanced → Recovery Ledger). On AWS the result will read “Verification required”: this cloud cannot prove the queue stayed empty, so ServiceHub never says it held.' },
    { n: 3, loc: btn('Done'), t: 'Done — closes the window.' },
    { n: 4, loc: [dlg().getByRole('link', { name: 'Active messages' }), dlg().getByRole('link', { name: 'Replayed' })], t: 'Active messages / Replayed — jump to where you can watch this message.' },
  ])
})
await scene('11', async () => {
  await go('/?tab=replayed'); await dismiss()
  await shot(page, S('11-replayed'), [
    { n: 1, loc: page.getByText('messages replayed').locator('..').locator('..'), t: 'Messages replayed — how many were put back in the window.' },
    { n: 2, loc: page.getByText('being watched now').locator('..').locator('..'), t: 'Being watched — replays still inside their watch window. AWS cannot prove whether each stayed fixed, so they wait for you to judge.' },
    { n: 3, loc: page.getByText('came back').locator('..').locator('..'), t: 'Came back — messages that failed the same way again. Nothing retries them on its own.' },
    { n: 4, loc: page.getByText('Any result', { exact: true }), t: 'Result filter — stayed fixed, being watched, came back.' },
    { n: 5, loc: page.getByRole('button', { name: /Download|Export/ }).last(), t: 'Download — saves this list as a file. It does not change anything.' },
    { n: 6, loc: page.getByRole('link', { name: /^Details/ }).last(), t: 'Details — the full record of this replay.' },
    { n: 7, loc: [page.getByRole('button', { name: /AWS · Namespace/ }), btn('All queues & topics'), btn('All', ), btn('Last 24 hours'), page.getByPlaceholder(/Message ID, queue, who/), btn('Refresh')], t: 'Filters — namespace, queue or topic, result, time window and search; Refresh reads the list again. They change what is listed, never what happened.' },
    { n: 9, loc: page.locator('main button').filter({ hasText: /^(Auto Replay rules|All|Last 24 hours)$/ }).or(page.getByRole('button', { name: /Auto Replay rules/ })).or(page.getByRole('button', { name: 'What am I looking at?' })), all: true, t: 'What am I looking at? — a short reading guide. Auto Replay rules — opens the rules page. All and Last 24 hours — the result and time filters.' },
    { n: 10, loc: [page.locator('main').getByRole('link', { name: /^(Dead letters|Active|Replayed)/ }), page.locator('main').getByRole('button', { name: /^About (Dead letters|Active|Replayed)$/ })], all: true, t: 'The three tabs — Dead letters, Active and Replayed. They switch the list below without leaving the page.' },
    { n: 8, loc: [page.locator('table thead'), page.locator('table tbody tr')], all: true, t: 'The table — a tick per replay (the heading tick ticks the page) and Details on each row.' },
  ])
  await scrollMain(99999); await settle(600)
  await shot(page, S('11b-replayed-end'), [
    { n: 1, loc: page.locator('table tbody tr'), all: true, t: 'The replays, newest first — each with a tick and Details.' },
    { n: 2, loc: page.getByRole('button', { name: /^(Rows per page|Previous page|Go to page \d+|Next page)$/ }), all: true, t: 'Paging — rows per page, previous, the page numbers and next.' },
    { n: 3, loc: [page.getByRole('button', { name: /AWS · Namespace/ }), page.locator('main button').filter({ hasText: /^(Any result|All queues & topics|All|Last 24 hours)$/ }), page.getByPlaceholder(/Message ID, queue, who/), btn('Refresh'), btn('Download as CSV')], all: true, t: 'Filters — result, queue or topic, who replayed it, time window and search; Refresh re-reads the list and Download as CSV saves it as a file. They change what is listed, never what happened.' },
    { n: 4, loc: [page.locator('main').getByRole('link', { name: /^(Dead letters|Active|Replayed)/ }), btn('Auto Replay rules'), page.locator('table thead')], all: true, t: 'The three tabs, the Auto Replay rules button and the table headings, as on the previous screenshot.' },
  ])
})
await scene('12', async () => {
  await go('/?tab=active'); await dismiss()
  await shot(page, S('12-active'), [
    { n: 1, loc: page.getByRole('link', { name: /Send a message|Send/ }).or(btn('Send a message')).first(), t: 'Send a message — puts a test message on a queue. This does change the queue, so use a dev queue.' },
    { n: 2, loc: page.getByText(/counts active messages but doesn’t open them/).locator('..'), t: 'Why there is no message list — on AWS there is no way to look at a message without it counting as a delivery, and watching could push it into the dead-letter queue by itself. So ServiceHub shows counts per queue, and “Follow live” is not offered.' },
    { n: 3, loc: page.getByRole('button', { name: /AWS · Namespace/ }), t: 'Namespace — all of AWS, or one account and region.' },
    { n: 4, loc: page.getByRole('tablist').or(page.locator('main nav')).first(), t: 'The three tabs — Dead letters, Active and Replayed — each with an ⓘ that explains it.' },
    { n: 5, loc: page.locator('table').first(), t: 'The table — one row per queue found in this region, including the dead-letter queue itself: Queue or topic, Waiting now (ready to be received) and Dead-lettered (moved here after failing too often). AWS counts them; ServiceHub never opens them. The ⓘ on each heading says what that count means.' },
  ])
})
await scene('14', async () => {
  await go('/?tab=dlq&panel=rules')
  await shot(page, S('14-auto-replay'), [
    { n: 1, loc: page.getByRole('button', { name: 'Auto Generate Rules' }), t: 'Auto Generate Rules — proposes rules from failures already seen. It only proposes; you decide what to turn on.' },
    { n: 2, loc: page.getByRole('button', { name: /Create rule/ }).first(), t: 'Create rule — start a rule from a failure you have already seen.' },
    { n: 3, loc: page.getByText('HOW AUTO REPLAY WORKS'), t: 'How it works — you name a failure, safety checks decide, it replays gently, it proves it worked and stops itself if it does not.' },
    { n: 4, loc: page.getByText('rules on').locator('..'), t: 'Totals — rules on, replayed by rules, waiting for a person, stopped themselves.' },
    { n: 5, loc: page.getByRole('button', { name: /AWS · Namespace/ }), t: 'Namespace — Auto Replay rules apply to every AWS account and region you connected; this picks which one the page shows.' },
    { n: 6, loc: btn('Refresh rules'), t: 'Refresh rules — reads the rules again.' },
    { n: 7, loc: btn('Create your first rule'), t: 'Create your first rule — the same as Create rule, shown while there are none.' },
  ])
  await page.getByRole('button', { name: /Create rule/ }).first().click(); await settle(1500)
  await shot(page, S('14-auto-replay-create'), [
    { n: 1, loc: page.getByText('Based on'), t: 'Based on — pick a failure ServiceHub has already seen. A rule can only be made from a seen failure.' },
    { n: 2, loc: page.getByText(/Rules never run in Production/), t: 'The guarantees: never in Production, same safety checks as you, and it stops itself if fewer than half stay fixed.' },
    { n: 3, loc: btn('Create and turn on'), t: 'Create and turn on — enabled once you choose a failure.' },
    { n: 4, loc: btn('Cancel'), t: 'Cancel — nothing is created.' },
    { n: 5, loc: [page.getByRole('button', { name: /AWS · Namespace/ }), btn('Refresh rules'), btn('Auto Generate Rules'), btn('How Auto Replay works')], t: 'The page header — namespace, Refresh rules, Auto Generate Rules (proposes only) and the “How Auto Replay works” fold, as on the previous screenshot.' },
  ])
})
await scene('15', async () => {
  await go('/'); await link('Settings').click(); await settle(1500)
  await shot(page, S('15-settings-connections'), [
    { n: 1, loc: dlg().getByRole('navigation').getByRole('link'), all: true, t: 'Sections — Connections, Notifications, Preferences, Access & security and Backup. Each jumps to that part of this window.' },
    { n: 2, loc: dlg().getByText('+ Add a cloud'), t: 'Add a cloud — connect another cloud.' },
    { n: 3, loc: btn('Test'), t: 'Test — checks the connection now and shows the result. Reads only.' },
    { n: 4, loc: dlg().getByRole('button', { name: /Remove AWS Dev/ }), t: 'Remove — forgets this connection and its stored credential. It does not delete anything in AWS.' },
    { n: 5, loc: dlg().getByRole('button', { name: 'Switch on' }), t: 'Confirm fixes on this cloud — Off until you switch it on. Without it, a replay here is sent back but cannot be confirmed as fixed. Switch on sets up the observer that lets ServiceHub see this cloud’s whole dead-letter queue.' }
  ], page.locator('#settings-connections'))
  const sect = async (k, f, calls) => { await dlg().getByRole('navigation').getByRole('link', { name: k }).first().click(); await settle(900); await shot(page, S(`15-settings-${f}`), calls, page.locator(`#settings-${f}`)) }
  await sect('Notifications', 'notifications', [
    { n: 1, loc: [dlg().getByRole('switch'), page.getByText('In-app bell and pop-up'), page.getByText('In-app bell is always on')], t: 'In-app bell and pop-up — always on. It cannot be switched off, so it can never be switched off by mistake.' },
    { n: 2, loc: page.getByText('+ Add webhook').first(), t: 'Add webhook (Slack) — paste a Slack incoming-webhook URL to also get the message there.' },
    { n: 3, loc: page.getByText('+ Add webhook').nth(1), t: 'Add webhook (Microsoft Teams) — the same for a Teams channel.' },
    { n: 4, loc: page.getByText('+ Add webhook').nth(2), t: 'Add webhook (any other system) — JSON to a URL you choose. Private and internal addresses are refused.' },
  ])
  await sect('Preferences', 'preferences', [
    { n: 1, loc: page.getByText('Theme', { exact: true }), t: 'Theme — Light today; Dark is marked soon.' },
    { n: 2, loc: [page.getByText('Times shown in', { exact: true }), btn('Times shown in')], t: 'Times shown in — your browser’s time zone, or UTC. It changes how times are displayed, never the data.' },
    { n: 3, loc: [page.getByText('Open on', { exact: true }), dlg().locator('button[role=radio]').filter({ hasText: /^(Simple|Last used)$/ })], all: true, t: 'Open on — start in Simple every time (Simple), or where you last were (Last used). Kept in this browser only.' },
    { n: 4, loc: page.getByPlaceholder(/tracing\.example\.com/), t: 'Your tracing tool — optional. Paste a link with {traceId} in it, and ServiceHub shows a link from each message to that trace in your own tool. It is kept in this browser only.' }
  ])
  await sect('Access & security', 'access', [
    { n: 1, loc: page.getByText(/You are/).first(), t: 'Who you are — shown from this browser session until roles are switched on. The credential key fingerprint shows what encrypts stored credentials.' },
    { n: 2, loc: page.getByText('Roles', { exact: true }).first(), t: 'Roles — Viewer sees, Operator replays, Approver answers the Agent, Admin connects clouds. Giving the first role switches roles on.' },
    { n: 3, loc: page.getByPlaceholder('API key name'), t: 'Who — the API key name or signed-in user the role is for.' },
    { n: 4, loc: [btn('Kind'), btn('Role'), btn('Where'), btn('Grant'), page.getByPlaceholder(/^Why/)], t: 'Kind, Role, Where and Grant — choose whether it is a key or a person, the role, whether it applies everywhere or to one namespace, then Grant. Granting the first role switches roles on for everyone.' },
    { n: 5, loc: [page.getByLabel(/^Why/), page.getByLabel('Type STOP to confirm'), btn('Switch emergency stop on')], all: true, t: 'Emergency stop — type STOP, say why, and switch it on to halt every automatic action at once. Nothing already done is undone; switch it off to resume.' },
  ])
  await sect('Backup', 'backup', [
    { n: 1, loc: page.getByRole('button', { name: /Take a backup now/ }), t: 'Take a backup now — saves a consistent copy of ServiceHub’s own database (not of your clouds).' },
  ])
})
await scene('16', async () => {
  await go('/'); await link('Help').click(); await settle(1500)
  await shot(page, S('16-help'), [
    { n: 1, loc: page.getByPlaceholder('How do I…'), t: 'Search — type what you are trying to do.' },
    { n: 2, loc: page.locator('section[aria-label=Everyday] summary'), all: true, t: 'An answer — click to open it in place; each links to the screen it is about.' },
    { n: 3, loc: page.locator('section[aria-label="Setting up"] summary'), all: true, t: 'Setting up answers — connecting, alerts and who may replay.' },
    { n: 4, loc: page.getByText('Keyboard', { exact: true }), t: 'Keyboard shortcuts — only ones that work are listed.' },
    { n: 5, loc: page.locator('summary').filter({ hasText: 'Every screen, with real screenshots' }), all: true, t: 'Step by step — one guide per cloud, each with every screen as a real screenshot and a numbered key for every button and link. Yours (AWS) is listed first. The same guides are the articles in docs/clouds.' },
  ])
  await page.getByRole('dialog').or(page.locator('aside')).last().evaluate((d) => { const sc = [...d.querySelectorAll('*')].find((e) => e.scrollHeight > e.clientHeight + 50 && /(auto|scroll)/.test(getComputedStyle(e).overflowY)); if (sc) sc.scrollTop = 99999 }).catch(() => {}); await settle(600)
  await shot(page, S('16b-help-end'), [
    { n: 1, loc: page.locator('summary'), all: true, t: 'Answers and guides — click one to open it in place; the “Every screen” entries are one guide per cloud, each a real screenshot with a numbered key for every button and link.' },
  ])
})
await scene('17', async () => {
  await go('/advanced', 3500); await dismiss()
  await shot(page, S('17-advanced-overview'), [
    { n: 1, loc: page.getByText('Scope', { exact: true }).locator('..'), t: 'Scope — all clouds, or one.' },
    { n: 2, loc: page.getByText('Window', { exact: true }).locator('..'), t: 'Window — the period the page counts over.' },
    { n: 3, loc: page.getByText('Insights', { exact: true }).first(), t: 'Insights — patterns ServiceHub noticed across your failures.' },
    { n: 4, loc: page.getByText(/Recovery — last/).first(), t: 'Recovery — how replays ended. “Recovered” means it did not come back, not that the business transaction completed.' },
    { n: 5, loc: page.getByText(/Authority — and why/).first(), t: 'Authority — what each failure may do on its own, and what holds it there.' },
    { n: 6, loc: page.getByText(/Capability — what each cloud/).first(), t: 'Capability — what each cloud can prove.' },
    { n: 9, loc: page.getByRole('link', { name: /^Why/ }), t: 'Why → — explains why this cloud cannot yet prove a replayed message stayed fixed, and what would unlock it. It opens Help; nothing changes.' },
    { n: 8, loc: btn('Overview'), t: 'Overview — the page you are on; the other three Advanced pages are in the bar at the top.' },
    { n: 7, loc: [page.getByRole('link', { name: /Being watched/ }), page.getByRole('link', { name: /All agents/ }), page.getByRole('link', { name: /Recovery Ledger →/ })], t: 'Being watched, All agents → and Recovery Ledger → — jump to the Agents page and the Recovery Ledger. They only open pages; nothing changes.' },
  ])
  await go('/advanced/ledger', 3500); await dismiss()
  await shot(page, S('17-advanced-ledger'), [
    { n: 1, loc: page.getByRole('button', { name: /Export evidence/ }), t: 'Export evidence — downloads the ledger so it can be verified offline. It changes nothing.' },
    { n: 2, loc: page.getByRole('button', { name: /^All\s*\d/ }).first(), t: 'Outcome chips — Waiting, Watching, Recovered, Unverified, Returned, Failed, Unknown, Declined. Recovered and Unverified are never counted together.' },
    { n: 3, loc: page.getByText('Cloud', { exact: true }).first().locator('..'), t: 'Filters — cloud, namespace, queue or topic, who did it, and search.' },
    { n: 4, loc: page.getByText(/^Details/).first(), t: 'Details — who took the action, what happened and its evidence. Each entry carries a fingerprint of the one before it.' },
    { n: 5, loc: [btn('Namespace'), btn('All queues & topics'), btn('By'), page.getByPlaceholder('Search').or(page.getByRole('textbox', { name: 'Search' })), page.getByRole('button', { name: /Details of ledger entry/ })], all: true, t: 'Filters and entries — namespace, queue or topic, who, and search narrow the list; each entry opens to show its evidence.' },
    { n: 6, loc: [btn('What am I looking at?'), btn('Window')], t: 'What am I looking at? — a short reading guide for this page. Window — the period it counts over.' },
    { n: 7, loc: page.getByRole('button', { name: /^(Waiting|Watching|Recovered|Unverified|Returned|Failed|Unknown|Declined)\s*\d/ }), all: true, t: 'Outcome chips with counts — click one to list only those entries; All shows everything again.' },
    { n: 8, loc: [btn('Rows per page'), btn('Previous page'), btn('Go to page 1'), btn('Next page')], t: 'Paging — how many rows per page, previous, the page number and next.' },
    { n: 9, loc: page.locator('thead'), t: 'The table headings — each has an ⓘ that explains that column in a sentence.' },
  ])
  await go('/advanced/signatures', 3500); await dismiss()
  await shot(page, S('17-advanced-signatures'), [
    { n: 1, loc: page.getByText('Trace a message', { exact: true }).first(), t: 'Trace a message — follow one message across clouds by its ID.' },
    { n: 2, loc: page.getByText(/^Growing/).first(), t: 'Growing — signatures whose recent days hold at least twice what earlier days did.' },
    { n: 3, loc: page.getByRole('button', { name: /most messages/ }).or(page.getByText('most messages')).first(), t: 'Sort — most messages, or others.' },
    { n: 4, loc: page.getByText('PaymentTimeout', { exact: true }).first(), t: 'A signature — one way of failing: the same queue and the same kind of error, so many messages become one thing to reason about.' },
    { n: 5, loc: page.getByText(/replayed, none verified|not replayed/).first(), t: 'Replays — how many of these were replayed and how many were verified to have held. On AWS none can be verified, and this says so.' },
    { n: 6, loc: [btn('Cloud'), btn('Namespace'), btn('All queues & topics'), btn('By'), page.getByRole('textbox', { name: 'Search' }).or(page.getByPlaceholder('Search'))], all: true, t: 'Filters — cloud, namespace, queue or topic, who, and search.' },
    { n: 7, loc: page.locator('main button').filter({ hasText: /No error text was recorded|^[A-Z]/ }).filter({ hasText: /recorded/ }), all: true, t: 'Each signature row — opens to show the failures grouped under it and what each replay did.' },
    { n: 8, loc: [btn('What am I looking at?'), btn('Window')], t: 'What am I looking at? — a short reading guide. Window — the period counted.' },
    { n: 9, loc: page.locator('button').filter({ hasText: /^(Signatures|All\s*\d|Replay helps|Replay doesn’t help)/ }), all: true, t: 'Chips — Signatures, All, Replay helps, Replay doesn’t help. Click one to list only those.' },
    { n: 10, loc: [btn('Rows per page'), btn('Previous page'), btn('Go to page 1'), btn('Next page')], t: 'Paging — rows per page, previous, the page number and next.' },
    { n: 11, loc: page.locator('thead'), t: 'The table headings — Signature, Messages, Days and Replays, each with an ⓘ that explains it.' },
  ])
  await scrollMain(99999); await settle(600)
  await shot(page, S('17c-advanced-signatures-end'), [
    { n: 1, loc: page.locator('main button').filter({ hasText: /recorded/ }), all: true, t: 'Each signature row — opens to show the failures grouped under it and what each replay did.' },
    { n: 2, loc: page.getByRole('button', { name: /^(Rows per page|Previous page|Go to page \d+|Next page)$/ }), all: true, t: 'Paging — rows per page, previous, the page numbers and next.' },
    { n: 3, loc: page.locator('main').locator('input, button').filter({ hasNot: page.locator('tbody') }), all: true, t: 'The controls above the list — Signatures, Trace a message, the filters, search, sort, and the ⓘ About buttons on the headings — as on the previous screenshot.' },
  ])
  await go('/advanced/agents', 3500); await dismiss()
  await shot(page, S('17-advanced-agents'), [
    { n: 1, loc: page.getByText('Acting Agents').first().locator('..').locator('..'), t: 'Acting agents — the only ones that can change anything, and only after the same safety checks you get.' },
    { n: 2, loc: page.getByText('Watching Agents').first().locator('..').locator('..'), t: 'Watching agents — they only look and record.' },
    { n: 3, loc: page.getByRole('button', { name: 'Pause' }).first(), t: 'Pause — stops that agent acting. It is the one thing Advanced can do, because it only removes authority. Nothing it already did is undone.' },
    { n: 4, loc: page.getByText(/Learn more/).first(), t: 'Learn more — a short explanation of how agents work.' },
    { n: 6, loc: btn('What am I looking at?'), t: 'What am I looking at? — a short reading guide for this page.' },
    { n: 5, loc: [page.getByRole('button', { name: /^\d+\s*Healthy/ }), page.getByRole('button', { name: /^Open / }), page.getByRole('button', { name: /^(Bulk Replay|Auto Replay)/ }), page.getByRole('button', { name: /^Pause / })], all: true, t: 'Health summary, agent rows and Open — the summary says whether every agent is running normally; a row opens that agent’s details; Open goes to the screen the agent works through.' },
  ])
  await scrollMain(99999); await settle(600)
  await shot(page, S('17b-advanced-agents-more'), [
    { n: 1, loc: page.getByText('Watching Agents').first().locator('..').locator('..'), t: 'Watching agents — they only look and record; they cannot change anything.' },
    { n: 2, loc: page.getByRole('button', { name: /^(Fix Confirmer|Replay Verifier|Auto Replay|Bulk Replay|Open |Pause )/ }), all: true, t: 'Each agent row opens to show what it may and may not do; Open goes to the screen it works through; Pause (acting agents only) stops it acting.' },
    { n: 3, loc: page.getByText(/Learn more/).first(), t: 'Learn more — a short explanation of how agents work.' },
  ])
})
await scene('18', async () => {
  await go('/'); await dismiss(); await page.keyboard.press('Meta+k'); await settle(1000)
  await shot(page, S('18-search'), [
    { n: 1, loc: page.getByRole('dialog').getByRole('combobox').or(page.getByRole('dialog').locator('input')).first(), t: 'Search box — type part of a cloud, queue or page name. Enter opens the first result. Esc closes. Searching never changes anything.' },
  ])
  await page.keyboard.press('Escape'); await settle(400)
  await page.getByRole('button', { name: /waiting for you|needs you/ }).first().click(); await settle(800)
  await shot(page, S('18-bell'), [
    { n: 1, loc: page.getByRole('dialog', { name: /Waiting for you/ }), t: 'The bell — the only place the Agent asks you something. It lists what is waiting for you and clears when it is resolved, not when you look.' },
    { n: 2, loc: page.getByRole('link', { name: /See all waiting/ }), t: 'See all waiting — opens the full list of what needs a person.' },
  ])
})

import fs2 from 'node:fs'
const gaps = JSON.parse(fs2.readFileSync(`${OUT}/uncovered.json`, 'utf8'))
const total = Object.values(gaps).reduce((n, v) => n + v.length, 0)
const unTotal = finishRun(OUT)
console.log(unTotal === 0 ? 'BELOW THE FOLD complete — nothing left unreached' : `BELOW THE FOLD: ${unTotal} controls never in a callout (see unreached.json)`)
console.log(total === 0 ? 'COVERAGE complete — every control has a callout' : `COVERAGE gaps: ${total} controls in ${Object.values(gaps).filter((v) => v.length).length} screenshots (see uncovered.json)`)
await browser.close()
