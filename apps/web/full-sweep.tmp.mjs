import { chromium } from 'playwright';

const AZ = '4172a33a-f9ab-4f6b-86f0-85f0d0f2d93d';
const AWS = 'a7dffab5-4824-4678-8431-8eb0f3c2751e';
const S = process.env.SCRATCH;

const browser = await chromium.launch();
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } });
await ctx.addInitScript(() => localStorage.setItem('servicehub_tour_completed', 'true'));
const page = await ctx.newPage();

const errors = [];
const results = [];
page.on('response', async r => {
  if (r.url().includes('/api/') && r.status() >= 400 && !r.url().includes('simulator/status')) {
    let b=''; try{b=(await r.text()).slice(0,100);}catch{}
    errors.push(`${r.status()} ${r.request().method()} ${r.url().replace('http://localhost:3000','').split('?')[0]} ${b}`);
  }
});
page.on('pageerror', e => errors.push('PAGEERROR: ' + String(e).slice(0, 120)));
const prov = () => page.evaluate(() => document.documentElement.dataset.provider);
const ok = (name, cond) => { results.push(`${cond ? 'PASS' : 'FAIL'}  ${name}`); };
const shot = (n) => page.screenshot({ path: `${S}/sweep-${n}.png` });
const toasts = () => page.locator('[role="status"], [class*="toast"]').allTextContents();

// ════════ AZURE ════════
await page.goto(`http://localhost:3000/messages?namespace=${AZ}&queue=testqueue&queueType=active`, { waitUntil: 'load' });
await page.waitForTimeout(6000);
ok('A1 azure theme on queue view', (await prov()) === 'azure');
const azTabs = await page.getByRole('button', { name: /Active \(|Dead-Letter \(/ }).allTextContents();
ok('A1 azure tab labels', azTabs.length === 2 && azTabs[0].startsWith('Active'));
await shot('a1-azure-queue');

// message detail panel
const card = page.locator('div.cursor-pointer').filter({ hasText: 'azureCheck' }).first();
if (await card.count()) {
  await card.click(); await page.waitForTimeout(1500);
  const detailTabs = await page.getByRole('button', { name: /^(Properties|Body|AI Insights|Headers)$/ }).count();
  ok('A2 detail panel tabs', detailTabs === 4);
  await shot('a2-azure-detail');
} else { ok('A2 detail panel (no active msg card)', false); }

// DLQ tab + replay
await page.getByRole('button', { name: /Dead-Letter \(/ }).click();
await page.waitForTimeout(5000);
const dlqCard = page.locator('div.cursor-pointer').filter({ hasText: 'azureCheck' }).first();
try {
  await dlqCard.click({ timeout: 5000 }); await page.waitForTimeout(1200);
  const replayBtn = page.getByRole('button', { name: /^Replay/ }).first();
  const enabled = (await replayBtn.count()) > 0 && !(await replayBtn.isDisabled());
  ok('A3 replay enabled on DLQ msg', enabled);
  if (enabled) {
    await replayBtn.click({ timeout: 5000 }); await page.waitForTimeout(600);
    await page.getByRole('button', { name: /^Confirm$/ }).last().click({ timeout: 5000 });
    await page.waitForTimeout(4000);
    ok('A3 replay toast', (await toasts()).some(t => t.includes('replayed successfully')));
  }
  await shot('a3-azure-replay');
} catch (e) { ok('A3 replay flow: ' + String(e).slice(0, 60), false); }

// FAB: send message
await page.locator('button[title="Open message menu"]').click(); await page.waitForTimeout(500);
await shot('a4-azure-fab');
await page.getByRole('button', { name: /Send Message/ }).click(); await page.waitForTimeout(800);
await page.getByRole('button', { name: /^Send( Message)?$/i }).last().click();
await page.waitForTimeout(3500);
ok('A4 FAB send toast', (await toasts()).some(t => t.includes('sent successfully') || t.includes('Message sent')));

// FAB: test DLQ
await page.locator('button[title="Open message menu"]').click(); await page.waitForTimeout(500);
await page.getByRole('button', { name: /Test DLQ/ }).click();
await page.waitForTimeout(5000);
ok('A5 FAB test-dlq toast', (await toasts()).some(t => t.includes('DLQ') || t.includes('dead-letter')));

// topic with no subscriptions
await page.locator('aside').getByText('testtopic', { exact: true }).click();
await page.waitForTimeout(1500);
const noSubs = await page.locator('aside').getByText(/No subscriptions/i).count();
ok('A6 topic shows "No subscriptions" hint', noSubs > 0);
await shot('a6-azure-topic');

// dashboard blue
await page.goto('http://localhost:3000/dashboard', { waitUntil: 'load' });
await page.waitForTimeout(3000);
ok('A7 dashboard stays azure theme', (await prov()) === 'azure');
await shot('a7-azure-dashboard');

// quick access pages under azure theme
for (const [path, n] of [['/rules','a8-rules'],['/dlq-history','a8-dlq-history'],['/health','a8-health'],['/scheduled','a8-scheduled'],['/cross-cloud-trace','a8-trace'],['/audit','a8-audit'],['/cloud-bridge','a8-bridge']]) {
  await page.goto('http://localhost:3000' + path, { waitUntil: 'load' }).catch(()=>{});
  await page.waitForTimeout(2200);
  await shot(n);
}
ok('A8 quick-access pages visited', true);

// ════════ AWS ════════
await page.goto(`http://localhost:3000/messages?namespace=${AZ}&queue=testqueue&queueType=active`, { waitUntil: 'load' });
await page.waitForTimeout(2500);
await page.locator('aside').getByText('DevAWS').click();
await page.waitForTimeout(800);
ok('B1 click AWS ns header → orange', (await prov()) === 'aws');

await page.goto(`http://localhost:3000/messages?namespace=${AWS}&queue=servicehub-study-orders&queueType=active`, { waitUntil: 'load' });
await page.waitForTimeout(5000);
const awsTabs = await page.getByRole('button', { name: /Queue \(|DLQ \(/ }).allTextContents();
ok('B1 AWS tab labels Queue/DLQ', awsTabs.length === 2);
ok('B1 AWS notice banner', (await page.locator('text=AWS SQS counts every view').count()) > 0);
await shot('b1-aws-queue');

// nested DLQ + replay
await page.locator('aside').getByText('DLQ: servicehub-study-orders-dlq').click();
await page.waitForTimeout(8000);
await shot('b2-aws-dlq');
const awsDlqCard = page.locator('div.cursor-pointer').filter({ hasText: /STUDY-1|plain text|xml|orderId|note/ }).first();
try {
  await awsDlqCard.click({ timeout: 8000 }); await page.waitForTimeout(1200);
  const rb = page.getByRole('button', { name: /^Replay/ }).first();
  const en = (await rb.count()) && !(await rb.isDisabled());
  ok('B2 AWS replay enabled', !!en);
  if (en) {
    await rb.click({ timeout: 5000 }); await page.waitForTimeout(600);
    await page.getByRole('button', { name: /^Confirm$/ }).last().click({ timeout: 5000 });
    await page.waitForTimeout(6000);
    ok('B2 AWS replay toast', (await toasts()).some(t => t.includes('replayed successfully')));
  }
} catch (e) { ok('B2 AWS replay flow: ' + String(e).slice(0, 60), false); }

// topic fan-out
await page.locator('aside').getByText('servicehub-study-ev').first().click();
await page.waitForTimeout(3500);
ok('B3 fan-out dashboard', (await page.locator('[data-testid="aws-topic-fanout"]').count()) > 0);
await shot('b3-aws-fanout');
await page.getByRole('button', { name: /Publish message/ }).click({ timeout: 8000 }).catch(() => ok('B3 publish button', false)); await page.waitForTimeout(800);
await page.getByRole('button', { name: /^Send( Message)?$/i }).last().click();
await page.waitForTimeout(3500);
ok('B3 fan-out publish toast', (await toasts()).some(t => t.includes('sent') || t.includes('Sent')));

// FAB on AWS all enabled
await page.getByRole('button', { name: /View messages/ }).first().click(); await page.waitForTimeout(2500);
await page.locator('button[title="Open message menu"]').click(); await page.waitForTimeout(500);
let fabOk = true;
for (const nm of ['Test DLQ','Generate Messages','Send Message']) {
  const b = page.getByRole('button', { name: new RegExp(nm) }).first();
  if (!(await b.count()) || (await b.isDisabled())) fabOk = false;
}
ok('B4 AWS FAB all actions enabled', fabOk);
await shot('b4-aws-fab');

// dashboard orange
await page.goto('http://localhost:3000/dashboard', { waitUntil: 'load' });
await page.waitForTimeout(3000);
ok('B5 dashboard stays aws theme', (await prov()) === 'aws');
await shot('b5-aws-dashboard');

console.log(results.join('\n'));
console.log('\nAPI/page errors:', errors.length ? '\n' + [...new Set(errors)].join('\n') : 'none');
await browser.close();
