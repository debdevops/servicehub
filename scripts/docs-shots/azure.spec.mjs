// Azure walkthrough: real screenshots of the shipped build against a real Service Bus namespace, numbered callouts, and a
// keys.json that the markdown + Help article are generated from. Run via run-azure.sh (fresh DB + reseeded queue).
import { open, shot, BASE, ROOT } from './lib.mjs'
const OUT = process.env.DOCS_OUT || `${ROOT}/docs/screenshots/azure`
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

// ---------- Part 1: connect ----------
await scene('01', async () => {
  await go('/')
  await shot(page, S('01-welcome'), [
    { n: 1, loc: page.getByRole('link', { name: 'Connect Azure' }), t: 'Connect Azure — opens the Add a cloud window on the Azure tab. It only asks for a connection string; nothing is read until you press Connect there.' },
    { n: 2, loc: link('Add a cloud'), t: 'Add a cloud — the same window, from anywhere in the app. Use it later to connect AWS or Google Cloud as well.' },
    { n: 3, loc: page.getByRole('link', { name: 'Advanced' }), t: 'Simple | Advanced — Simple is where you act. Advanced is read-only pages (ledger, signatures, agents); it never changes anything.' },
    { n: 4, loc: link('Help'), t: 'Help — opens the Help panel with task-shaped answers and the keyboard shortcuts.' },
    { n: 5, loc: [page.getByRole('link', { name: 'Connect AWS' }), page.getByRole('link', { name: 'Connect Google' })], t: 'Connect AWS / Connect Google — the same Add a cloud window, on the AWS or Google Cloud tab. You do not need them for Azure.' },
  ])
})
await scene('02', async () => {
  await go('/?modal=add-cloud', 1500)
  await shot(page, S('02-add-cloud'), [
    { n: 1, loc: dlg().getByRole('button', { name: /^(Azure Service Bus|AWS SQS|Google Pub)/ }), all: true, t: 'Cloud tabs — Azure Service Bus is selected. The AWS and Google tabs ask for different credentials.' },
    { n: 2, loc: page.getByText('Where do I get the Service Bus connection string?'), t: 'Where do I get the connection string? — expands the portal steps, with the permissions to give.' },
    { n: 3, loc: page.getByPlaceholder('orders-dev'), t: 'Name — what you will see in ServiceHub. It is a label only; it does not change anything in Azure.' },
    { n: 4, loc: dlg().locator('input[type=radio]'), all: true, t: 'Environment — Development, UAT or Production. Start with Development; Auto Replay never acts in Production.' },
    { n: 5, loc: page.getByPlaceholder(/Endpoint=sb/), t: 'Connection string — paste the Primary connection string of a Shared access policy (Manage, Send, Listen). It is encrypted on this server and never shown again.' },
    { n: 6, loc: btn('Show value'), t: 'Show value — reveals what you typed so you can check it. It cannot reveal a stored string.' },
    { n: 7, loc: btn('Cancel'), t: 'Cancel — closes the window; nothing is saved.' },
    { n: 8, loc: btn('Connect'), t: 'Connect — tests the string, then saves it. It only reads queues; it never sends or deletes anything by itself.' },
    { n: 9, loc: page.getByRole('dialog').getByRole('link', { name: /^Help for/ }), t: '(?) — opens Help on this very screen. Every window has one.' },
    { n: 10, loc: page.getByRole('dialog').getByRole('button', { name: 'Close' }), t: '✕ — closes the window without saving (Esc does the same).' },
  ])
  await page.getByText('Where do I get the Service Bus connection string?').click(); await settle(600)
  await shot(page, S('02-add-cloud-help'), [
    { n: 1, loc: page.getByText(/Choose Settings/).first(), t: 'Settings → Shared access policies — in the Azure portal, on your Service Bus namespace.' },
    { n: 2, loc: page.getByText(/Tick Manage/).first(), t: 'Tick Manage, Send and Listen. Azure only lets Manage list your queues and count messages; Send is used only when you replay.' },
    { n: 3, loc: page.getByText(/copy the Primary connection string/).first(), t: 'Copy the Primary connection string — it starts with Endpoint=sb://.' },
    { n: 4, loc: [dlg().getByRole('button', { name: /^(Azure Service Bus|AWS SQS|Google Pub)/ }), dlg().locator('input'), btn('Show value'), dlg().locator('summary')], all: true, t: 'The form underneath — the cloud tabs, Name, Environment, Connection string and Show value, exactly as in the previous screenshot.' },
  ])
})
await scene('03', async () => {
  await go('/?modal=add-cloud', 1500)
  await page.getByPlaceholder('orders-dev').fill('Azure Dev')
  await page.getByPlaceholder(/Endpoint=sb/).fill(process.env.AZ_SB_CS)
  await shot(page, S('03-add-cloud-filled'), [
    { n: 1, loc: page.getByPlaceholder('orders-dev'), t: 'A name you will recognise.' },
    { n: 2, loc: page.locator('input[type=password]'), t: 'The connection string, hidden as you paste it.' },
    { n: 3, loc: btn('Connect'), t: 'Connect — tests the connection, then saves it.' },
    { n: 4, loc: [dlg().getByRole('button', { name: /^(Azure Service Bus|AWS SQS|Google Pub|Show value|Cancel)/ }), dlg().locator('input[type=radio]'), dlg().locator('summary')], all: true, t: 'The rest of the form — cloud tabs, Environment, Show value and Cancel, as described on the previous screenshot.' },
  ])
  await btn('Connect').click(); await settle(5500)
  await shot(page, S('04-connected'), [
    { n: 1, loc: page.getByText(/Connected — ServiceHub can see/), t: 'The result: how many queues, topics and subscriptions ServiceHub found, how many messages are dead-lettered, and whether this cloud can prove a replay held.' },
    { n: 2, loc: page.getByRole('button', { name: 'Open Home' }).or(page.getByRole('link', { name: 'Open Home' })), t: 'Open Home — closes the window and shows Home.' },
  ])
})

// ---------- Part 2: use ----------
await scene('05', async () => {
  await go('/')
  await shot(page, S('05-home'), [
    { n: 1, loc: page.getByText('Last 24 hours').locator('..'), t: 'Window — the period Home counts over (24 hours, 7 or 30 days). It changes what you see, never what happens.' },
    { n: 2, loc: page.getByText('ServiceHub Agent').locator('..').locator('..'), t: 'The Agent bar — what the Agent is doing, how many replays it verified, and how many it is still watching.' },
    { n: 3, loc: btn('Pause'), t: 'Pause — stops the Agent acting until someone resumes it. It keeps watching and recording. Your own replays still work.' },
    { n: 4, loc: page.getByText(/Counts messages/).locator('..'), t: 'What this cloud can do — ticks show what Azure lets ServiceHub do safely (count, watch, browse, prove a fix held, scheduled messages).' },
    { n: 5, loc: link('See dead letters →'), t: 'Dead letters — opens the list of messages that failed and were set aside.' },
    { n: 6, loc: link('See active messages →'), t: 'Active messages — browse what is waiting. On Azure, looking is not a delivery.' },
    { n: 7, loc: link('See what was replayed →'), t: 'Replayed — everything put back, by whom, and how it went.' },
    { n: 8, loc: link('Manage rules →'), t: 'Auto Replay rules — opens the rules panel. A rule only acts once a failure has earned it.' },
    { n: 9, loc: btn('Got it'), t: 'Got it — hides this explanation. It does not affect your data.' },
    { n: 10, loc: link('See all dead letters in Azure →'), t: 'See all dead letters in Azure — opens the Dead letters list for this cloud.' },
    { n: 11, loc: page.getByRole('link', { name: 'Help for this page' }), t: 'Help for this page (the book beside the title) — opens Help on this very page, over it. Every page has one.' },
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
    { n: 9, loc: link('Active messages'), t: 'Active messages — what is waiting now.' },
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
  await go('/?tab=dlq'); await dismiss(); await page.getByText('Total messages').first().waitFor(); await settle(800)
  await shot(page, S('06-dead-letters'), [
    { n: 1, loc: page.getByText(/Azure · Namespace/i).locator('..').locator('..'), t: 'Namespace picker — all of Azure, or one namespace.' },
    { n: 2, loc: page.getByText('Other errors').locator('xpath=ancestor::div[contains(., "Total messages")][1]'), t: 'Why they failed — the biggest reasons, counted. The reason comes from what your sender wrote when it dead-lettered the message.' },
    { n: 3, loc: page.getByRole('link', { name: /^Dead letters\s*\d*/ }).last(), t: 'Dead letters tab — messages set aside. The number is how many are stuck now.' },
    { n: 4, loc: page.getByRole('link', { name: /^Active/ }).last(), t: 'Active tab — what is waiting in the queue.' },
    { n: 5, loc: page.getByRole('link', { name: /^Replayed/ }).last(), t: 'Replayed tab — what has been put back.' },
    { n: 6, loc: page.getByText('3 PaymentDeclined').or(page.getByRole('button', { name: /PaymentDeclined/ })).first(), t: 'Reason chips — click one to show only that reason.' },
    { n: 7, loc: page.getByRole('link', { name: 'Help for this page' }), t: 'Help for this page — opens Help on this page’s step of the guide. The (?) beside it only re-shows the short explanation.' },
  ])
})
await scene('07', async () => {
  await go('/?tab=dlq'); await dismiss(); await page.mouse.wheel(0, 340); await settle(600)
  await shot(page, S('07-filters-and-table'), [
    { n: 1, loc: page.getByText('Stuck now', { exact: true }), t: 'Showing — Stuck now, or messages that have since left the queue.' },
    { n: 2, loc: page.getByText('All time', { exact: true }), t: 'Time window — only messages set aside in this period.' },
    { n: 3, loc: page.getByPlaceholder(/Search ID/), t: 'Search — by message ID, queue, reason or error text.' },
    { n: 4, loc: btn('Replay All Messages'), t: 'Replay All Messages — opens a preview of everything shown. Nothing is sent until you confirm the preview.' },
    { n: 5, loc: [btn('Refresh'), btn('About Refresh')], t: 'Refresh — reads the queue again. On Azure this is free. The ⓘ beside it says when it was last read.' },
    { n: 6, loc: page.getByText('No messages selected'), t: 'Selection — tick rows to act on several; this line shows how many.' },
    { n: 7, loc: page.getByText('Replay selected…').first(), t: 'Replay selected — opens the same preview for just the ticked messages.' },
    { n: 8, loc: page.getByRole('link', { name: /Details of message/ }).first(), t: 'Details — opens the message: why it failed, its body, properties and delivery history.' },
    { n: 9, loc: page.getByRole('link', { name: /Replay message/ }).first(), t: 'Replay — opens the proposal for this one message. It does not send anything yet.' },
    { n: 10, loc: [page.locator('table thead'), page.locator('table tbody tr')], all: true, t: 'The table — a tick on every row (the one in the heading ticks the whole page), Details and Replay on every row, and an ⓘ About button on each column heading that explains that column in a sentence. “View details” in the heading chooses which columns show.' },
    { n: 11, loc: page.getByRole('button', { name: /^\d+ [A-Z]/ }), all: true, t: 'Reason chips — each shows a reason and how many messages have it. Click one to show only those; click it again to show all.' },
    { n: 12, loc: btn('All queues & topics'), t: 'All queues & topics — limit the list to one queue or topic.' },
    { n: 13, loc: page.getByRole('region', { name: 'Selected messages' }).first(), t: 'The selection bar — the tick selects every row on this page; View details chooses which columns show; Replay selected stays dimmed until something is ticked.' },
  ])
})
await scene('08', async () => {
  await go('/?tab=dlq&message=6', 3000)
  await shot(page, S('08-message-details'), [
    { n: 1, loc: page.getByRole('button', { name: /Expand/ }), t: 'Expand — widens the panel for long messages.' },
    { n: 2, loc: page.getByRole('tab', { name: 'Overview' }), t: 'Overview — reason, why it failed, the body with the bad field marked.' },
    { n: 3, loc: page.getByRole('tab', { name: 'Delivery' }), t: 'Delivery — the delivery history Azure recorded.' },
    { n: 4, loc: page.getByText(/^Why it failed/).first(), t: 'Why it failed — a plain-words reading of the recorded reason. Marked Suggestion: it is a reading, not something Azure reported.' },
    { n: 5, loc: btn('Raw'), t: 'Formatted / Raw — switch the body view.' },
    { n: 6, loc: page.getByRole('link', { name: /Replay this message/ }).or(btn('Replay this message')), t: 'Replay this message — opens the proposal. Nothing is sent from here.' },
    { n: 7, loc: [page.getByRole('tab', { name: 'Body' }), page.getByRole('tab', { name: 'Properties' }), page.getByRole('tab', { name: 'Headers' })], t: 'Body, Properties and Headers — the other tabs; each is shown below.' },
    { n: 8, loc: [btn('Formatted'), btn('Copy body')], t: 'Formatted and Copy body — pretty-print the body, or copy it to your clipboard. The copy stays in your browser.' },
    { n: 9, loc: btn('Copy message ID'), t: 'Copy message ID — copies the ID so you can search for it elsewhere.' },
  ])
  for (const [t, d] of [['Body', 'The message body exactly as it was sent.'], ['Properties', 'Application properties and system properties (message ID, content type, subject).'], ['Headers', 'Broker headers: enqueued time, lock and sequence information.'], ['Delivery', 'How many times Azure tried, and when it set the message aside.']]) {
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
  await go('/?tab=dlq&replay=1&modal=replay', 3500)
  await shot(page, S('09-replay-proposal'), [
    { n: 1, loc: page.getByText('THE MESSAGE'), t: 'The message — what is about to be replayed: ID, where it is stuck, when and why it was set aside, and its body.' },
    { n: 2, loc: dlg().getByRole('button', { name: 'Replay 1 message' }), all: true, t: 'Replay 1 message — sends it back. It is only sent when you press this.' },
    { n: 3, loc: dlg().getByRole('button', { name: /^(What will happen|Safety checks|After it runs)/ }), all: true, t: 'What will happen, Safety checks, After it runs — fold open or closed; the next screenshot shows them open.' },
    { n: 4, loc: dlg().getByRole('button', { name: 'Cancel' }), t: 'Cancel — closes without sending.' },
  ])
  await scrollDialog(99999); await settle(500)
  await shot(page, S('09-replay-proposal-checks'), [
    { n: 1, loc: page.getByText('WHAT WILL HAPPEN'), t: 'What will happen — where it goes and what happens to the dead-letter copy (it is removed once Azure accepts the new one).' },
    { n: 2, loc: page.getByText('SAFETY CHECKS'), t: 'Safety checks — every check that must pass; a failing one blocks the replay and says why.' },
    { n: 3, loc: page.getByText('AFTER IT RUNS'), t: 'After it runs — ServiceHub records it and watches for 24 hours; if it comes back, nothing retries it.' },
    { n: 4, loc: btn('Cancel'), t: 'Cancel — closes without sending.' },
    { n: 5, loc: dlg().getByRole('button', { name: /^(Replay 1 message|The message|What will happen)/ }), all: true, t: 'The folding headings and the Replay button repeated at the end of the window.' },
  ])
})
await scene('13', async () => {
  await go('/?tab=dlq'); await dismiss()
  await page.getByRole('checkbox').nth(2).check(); await page.getByRole('checkbox').nth(3).check(); await settle(500)
  await shot(page, S('07b-selection'), [
    { n: 1, loc: page.getByRole('checkbox').nth(2), t: 'Row tick — choose which messages to act on. The header tick chooses every row on the page.' },
    { n: 2, loc: page.getByText(/2 (messages )?selected/).first(), t: 'Selection count — how many messages are ticked.' },
    { n: 3, loc: page.getByRole('button', { name: 'Clear' }).first(), t: 'Clear — unticks everything. Nothing else changes.' },
    { n: 4, loc: page.getByRole('link', { name: /Replay selected/ }).first(), t: 'Replay selected — opens the bulk preview for just these messages. Nothing is sent yet.' },
    { n: 5, loc: [page.getByRole('button', { name: /^\d+ [A-Z]/ }), btn('Showing'), btn('Time window'), btn('All queues & topics'), page.getByPlaceholder(/Search ID/), btn('Replay All Messages'), btn('Refresh')], all: true, t: 'The filter row — reason chips, Showing, Time window, queue, search, Replay All Messages and Refresh, each explained on the previous screenshot.' },
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
    { n: 7, loc: dlg().getByRole('button', { name: /(page|Previous|Next)/i }), all: true, t: 'Page controls — move through the list when more messages are chosen than fit on one page.' },
    { n: 8, loc: dlg().getByRole('button', { name: 'How it will run' }), t: 'How it will run — folds open to show the pace and the automatic stop (five failures in a row).' },
  ])
  await page.keyboard.press('Escape'); await settle(500)
})
await scene('10', async () => {
  await go('/?tab=dlq&replay=1&modal=replay', 3000)
  await dlg().getByRole('button', { name: 'Replay 1 message' }).last().click(); await settle(4000)
  await shot(page, S('10-replay-result'), [
    { n: 1, loc: page.getByText('Sent back', { exact: true }), t: 'The result, in plain words: it was sent back, and ServiceHub will watch for it coming back.' },
    { n: 2, loc: page.getByText('Recorded in the ledger.').first(), t: 'It is recorded in the ledger (Advanced → Recovery Ledger), with who did it.' },
    { n: 3, loc: btn('Done'), t: 'Done — closes the window.' },
    { n: 4, loc: [dlg().getByRole('link', { name: 'Active messages' }), dlg().getByRole('link', { name: 'Replayed' })], t: 'Active messages / Replayed — jump to where you can watch this message.' },
  ])
})
await scene('11', async () => {
  await go('/?tab=replayed'); await dismiss()
  await shot(page, S('11-replayed'), [
    { n: 1, loc: page.getByText('messages replayed').locator('..').locator('..'), t: 'Messages replayed — how many were put back in the window.' },
    { n: 2, loc: page.getByText('being watched now').locator('..').locator('..'), t: 'Being watched — replays still inside their watch window. Azure can prove whether each stayed fixed.' },
    { n: 3, loc: page.getByText('came back').locator('..').locator('..'), t: 'Came back — messages that failed the same way again. Nothing retries them on its own.' },
    { n: 4, loc: page.getByText('Any result', { exact: true }), t: 'Result filter — stayed fixed, being watched, came back.' },
    { n: 5, loc: page.getByRole('button', { name: /Download|Export/ }).last(), t: 'Download — saves this list as a file. It does not change anything.' },
    { n: 6, loc: page.getByRole('link', { name: /^Details/ }).last(), t: 'Details — the full record of this replay.' },
    { n: 7, loc: [page.getByRole('button', { name: /Azure · Namespace/ }), btn('All queues & topics'), btn('All', ), btn('Last 24 hours'), page.getByPlaceholder(/Message ID, queue, who/), btn('Refresh')], t: 'Filters — namespace, queue or topic, result, time window and search; Refresh reads the list again. They change what is listed, never what happened.' },
    { n: 9, loc: page.locator('main button').filter({ hasText: /^(Auto Replay rules|All|Last 24 hours)$/ }).or(page.getByRole('button', { name: /Auto Replay rules/ })).or(page.getByRole('button', { name: 'What am I looking at?' })), all: true, t: 'What am I looking at? — a short reading guide. Auto Replay rules — opens the rules page. All and Last 24 hours — the result and time filters.' },
    { n: 10, loc: [page.locator('main').getByRole('link', { name: /^(Dead letters|Active|Replayed)/ }), page.locator('main').getByRole('button', { name: /^About (Dead letters|Active|Replayed)$/ })], all: true, t: 'The three tabs — Dead letters, Active and Replayed. They switch the list below without leaving the page.' },
    { n: 8, loc: [page.locator('table thead'), page.locator('table tbody tr')], all: true, t: 'The table — a tick per replay (the heading tick ticks the page) and Details on each row.' },
  ])
})
await scene('12', async () => {
  await go('/?tab=active'); await dismiss()
  await shot(page, S('12-active'), [
    { n: 1, loc: page.getByRole('link', { name: /Send a message|Send/ }).or(btn('Send a message')).first(), t: 'Send a message — puts a test message on the queue. This does change the queue, so use a dev queue.' },
    { n: 2, loc: page.getByText('waiting in docs-orders').locator('..').locator('..'), t: 'Counts — waiting, oldest, delivered before, dead-lettered.' },
    { n: 3, loc: page.getByText('Message state').locator('..'), t: 'Filters — queue or topic, message state, time window and search.' },
    { n: 4, loc: page.getByLabel(/Follow live/).or(page.getByText('Follow live')), t: 'Follow live — shows new messages as they arrive. On Azure, looking does not count as a delivery.' },
    { n: 5, loc: page.getByText(/Auto-refresh every/), t: 'Auto-refresh — re-reads the queue every 15 seconds.' },
    { n: 6, loc: btn('Scheduled'), t: 'Scheduled — messages set to arrive later.' },
    { n: 7, loc: page.getByText('Peek message').or(btn('Peek message')).first(), t: 'Peek message — opens one message to read it. Peeking does not remove or lock it.' },
    { n: 8, loc: [page.getByRole('button', { name: /Azure · Namespace/ }), page.getByRole('button', { name: /waiting$/ }), btn('All time'), page.getByPlaceholder(/Message ID, correlation/), page.getByRole('button', { name: 'Auto-refresh interval' }), btn('Refresh'), btn('Download body')], t: 'Filters and tools — namespace, queue, time window, search, how often the list refreshes, Refresh now, and Download body (saves the shown messages’ bodies as a file; changes nothing).' },
    { n: 9, loc: [page.getByRole('link', { name: /Dead-lettered/ })], t: 'Dead-lettered — jumps to the dead letters in this queue.' },
    { n: 11, loc: page.locator('main button').filter({ hasText: /^(docs-orders · \d+ waiting|All time)$/ }).or(page.getByRole('button', { name: 'What am I looking at?' })), all: true, t: 'What am I looking at? — a short reading guide. The queue and time-window pickers narrow what is listed.' },
    { n: 12, loc: [page.locator('main').getByRole('link', { name: /^(Dead letters|Active|Replayed)/ }), page.locator('main').getByRole('button', { name: /^About (Dead letters|Active|Replayed)$/ })], all: true, t: 'The three tabs — Dead letters, Active and Replayed. They switch the list below without leaving the page.' },
    { n: 10, loc: [page.locator('table thead'), page.locator('table tbody tr')], all: true, t: 'The table — a tick per message (the heading tick ticks the page) and Details on each row.' },
  ])
})
await scene('14', async () => {
  await go('/?tab=dlq&panel=rules')
  await shot(page, S('14-auto-replay'), [
    { n: 1, loc: page.getByRole('button', { name: 'Auto Generate Rules' }), t: 'Auto Generate Rules — proposes rules from failures already seen. It only proposes; you decide what to turn on.' },
    { n: 2, loc: page.getByRole('button', { name: /Create rule/ }).first(), t: 'Create rule — start a rule from a failure you have already seen.' },
    { n: 3, loc: page.getByText('HOW AUTO REPLAY WORKS'), t: 'How it works — you name a failure, safety checks decide, it replays gently, it proves it worked and stops itself if it does not.' },
    { n: 4, loc: page.getByText('rules on').locator('..'), t: 'Totals — rules on, replayed by rules, waiting for a person, stopped themselves.' },
    { n: 5, loc: page.getByRole('button', { name: /Azure · Namespace/ }), t: 'Namespace — Auto Replay rules apply to every Azure namespace; this picks which one the page shows.' },
    { n: 6, loc: btn('Refresh rules'), t: 'Refresh rules — reads the rules again.' },
    { n: 7, loc: btn('Create your first rule'), t: 'Create your first rule — the same as Create rule, shown while there are none.' },
  ])
  await page.getByRole('button', { name: /Create rule/ }).first().click(); await settle(1500)
  await shot(page, S('14-auto-replay-create'), [
    { n: 1, loc: page.getByText('Based on'), t: 'Based on — pick a failure ServiceHub has already seen. A rule can only be made from a seen failure.' },
    { n: 2, loc: page.getByText(/Rules never run in Production/), t: 'The guarantees: never in Production, same safety checks as you, and it stops itself if fewer than half stay fixed.' },
    { n: 3, loc: btn('Create and turn on'), t: 'Create and turn on — enabled once you choose a failure.' },
    { n: 4, loc: btn('Cancel'), t: 'Cancel — nothing is created.' },
    { n: 5, loc: [page.getByRole('button', { name: /Azure · Namespace/ }), btn('Refresh rules'), btn('Auto Generate Rules'), btn('How Auto Replay works')], t: 'The page header — namespace, Refresh rules, Auto Generate Rules (proposes only) and the “How Auto Replay works” fold, as on the previous screenshot.' },
  ])
})
await scene('15', async () => {
  await go('/'); await link('Settings').click(); await settle(1500)
  await shot(page, S('15-settings-connections'), [
    { n: 1, loc: dlg().getByRole('navigation').getByRole('link'), all: true, t: 'Sections — Connections, Notifications, Preferences, Access & security and Backup. Each jumps to that part of this window.' },
    { n: 2, loc: dlg().getByText('+ Add a cloud'), t: 'Add a cloud — connect another cloud.' },
    { n: 3, loc: btn('Test'), t: 'Test — checks the connection now and shows the result. Reads only.' },
    { n: 4, loc: dlg().getByRole('button', { name: /Remove Azure Dev/ }), t: 'Remove — forgets this connection and its stored credential. It does not delete anything in Azure.' },
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
  ])
  await sect('Access & security', 'access', [
    { n: 1, loc: page.getByText(/You are/).first(), t: 'Who you are — shown from this browser session until roles are switched on. The credential key fingerprint shows what encrypts stored credentials.' },
    { n: 2, loc: page.getByText('Roles', { exact: true }).first(), t: 'Roles — Viewer sees, Operator replays, Approver answers the Agent, Admin connects clouds. Giving the first role switches roles on.' },
    { n: 3, loc: page.getByPlaceholder('API key name'), t: 'Who — the API key name or signed-in user the role is for.' },
    { n: 4, loc: [btn('Kind'), btn('Role'), btn('Where'), btn('Grant')], t: 'Kind, Role, Where and Grant — choose whether it is a key or a person, the role, whether it applies everywhere or to one namespace, then Grant. Granting the first role switches roles on for everyone.' },
    { n: 5, loc: [page.getByLabel('Why'), page.getByLabel('Type STOP to confirm'), btn('Switch emergency stop on')], t: 'Emergency stop — type STOP, say why, and switch it on to halt every automatic action at once. Nothing already done is undone; switch it off to resume.' },
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
    { n: 5, loc: page.getByText('Every screen, with real screenshots'), t: 'Step by step — every screen with real screenshots and a numbered key for each button and link. This is the same guide as the article in docs/clouds/azure.md.' },
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
  ])
  await go('/advanced/signatures', 3500); await dismiss()
  await shot(page, S('17-advanced-signatures'), [
    { n: 1, loc: page.getByText('Trace a message', { exact: true }).first(), t: 'Trace a message — follow one message across clouds by its ID.' },
    { n: 2, loc: page.getByText(/^Growing/).first(), t: 'Growing — signatures whose recent days hold at least twice what earlier days did.' },
    { n: 3, loc: page.getByRole('button', { name: /most messages/ }).or(page.getByText('most messages')).first(), t: 'Sort — most messages, or others.' },
    { n: 4, loc: page.getByText('PaymentDeclined').first(), t: 'A signature — one way of failing: the same queue and the same kind of error, so many messages become one thing to reason about.' },
    { n: 5, loc: page.getByText(/replayed/).first(), t: 'Replays — how many of these were replayed and how many were verified to have held.' },
    { n: 6, loc: [btn('Cloud'), btn('Namespace'), btn('All queues & topics'), btn('By'), page.getByRole('textbox', { name: 'Search' }).or(page.getByPlaceholder('Search'))], all: true, t: 'Filters — cloud, namespace, queue or topic, who, and search.' },
    { n: 7, loc: page.getByRole('button', { name: /(declined|out of stock|is required)/ }), all: true, t: 'Each signature row — opens to show the failures grouped under it and what each replay did.' },
    { n: 8, loc: [btn('What am I looking at?'), btn('Window')], t: 'What am I looking at? — a short reading guide. Window — the period counted.' },
    { n: 9, loc: page.locator('button').filter({ hasText: /^(Signatures|All\s*\d|Replay helps|Replay doesn’t help)/ }), all: true, t: 'Chips — Signatures, All, Replay helps, Replay doesn’t help. Click one to list only those.' },
    { n: 10, loc: [btn('Rows per page'), btn('Previous page'), btn('Go to page 1'), btn('Next page')], t: 'Paging — rows per page, previous, the page number and next.' },
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
  await page.keyboard.press('Escape'); await go('/'); await dismiss()
  await page.getByText('This browser').first().click(); await settle(800)
  await shot(page, S('18-user-menu'), [
    { n: 1, loc: page.getByRole('menuitem', { name: 'Settings' }), t: 'Settings — opens Settings (connections, notifications, preferences, access, backup).' },
    { n: 2, loc: page.getByRole('menuitem', { name: /Help and shortcuts/ }), t: 'Help and shortcuts — opens Help.' },
    { n: 3, loc: page.getByRole('menu'), t: 'You — who ServiceHub records your actions as, and whether this server has sign-in turned on.' },
  ], page.getByRole('menu'))
})
import fs from 'node:fs'
const gaps = JSON.parse(fs.readFileSync(`${OUT}/uncovered.json`, 'utf8'))
const total = Object.values(gaps).reduce((n, v) => n + v.length, 0)
console.log(total === 0 ? 'COVERAGE complete — every control has a callout' : `COVERAGE gaps: ${total} controls in ${Object.values(gaps).filter((v) => v.length).length} screenshots (see uncovered.json)`)
await browser.close()
