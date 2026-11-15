import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { createLiveProvider } from '../src/live.ts'
import { AWS, AZ, AZ_RO, fakeReader, GCP, SECRET } from './fixtures.ts'

describe('live: dead letters (replay)', async () => {
  const reader = fakeReader()
  const { markdown, links } = await createLiveProvider(reader)('dlq-replay')

  it('lists each connection on its own, never a total across clouds', () => {
    assert.match(markdown, /\*\*Shop\*\* \(Azure · Service Bus\): \*\*120\*\* dead letters/)
    assert.doesNotMatch(markdown, /\b(162|164|262)\b/) // 120 + 40 (+2), or with the 100 recorded: no sum anywhere
  })

  it('reads reasons per connection, top three plus the rest', () => {
    assert.match(markdown, /by reason: 90 MaxDeliveryCountExceeded, 25 DeserializationError, 3 Custom, 2 more of 1 other kind/)
    assert.match(markdown, /by reason: 2 TTLExpiredException, 5 more of 2 other kinds/)
    assert.match(markdown, /100 with no reason recorded/)
  })

  it('names the busiest places, with the topic for a subscription, and skips empty ones', () => {
    assert.match(markdown, /most in: `orders` \(100\), `events\/audit-sub` \(20\)/)
    assert.doesNotMatch(markdown, /quiet/)
  })

  it('never shows a number the cloud did not give as exact', () => {
    assert.match(markdown, /about \*\*40\*\* dead letters \(the cloud's own estimate\)/)
    assert.match(markdown, /\*\*100\+\*\* recorded at the last \*\*Look now\*\* \(2026-11-15 10:05 UTC\)/)
  })

  it('says why a connection could not be read', () => {
    assert.match(markdown, /\*\*Pipeline\*\* \(Google Cloud · Pub\/Sub\): couldn't be read: Permission denied on projects\/x/)
  })

  it('leaves out components that offer no replay (from the coverage list, not the cloud name)', () => {
    assert.doesNotMatch(markdown, /Step Functions/)
  })

  it('warns about a read-only connection when the question is about replaying', () => {
    assert.match(markdown, /\*\*Audit\*\*[^\n]*\n(  - [^\n]*\n)*  - this connection is \*\*read-only\*\*/)
  })

  it('gives a fix-first tip only for reasons whose meaning is certain', () => {
    assert.match(markdown, /⚠️ \*\*MaxDeliveryCountExceeded\*\* means the consumer failed on every delivery/)
    assert.match(markdown, /⚠️ \*\*TTLExpiredException\*\*/)
    assert.doesNotMatch(markdown, /⚠️ \*\*Custom\*\*/)
  })

  it('links straight to the connection\'s Dead-letter messages tab, filtered on its top reason', () => {
    assert.ok(links.some((l) => l.href === `/azure/c/service-bus/${AZ}?tab=dead-letters&reason=MaxDeliveryCountExceeded` && l.label === 'Open the 90 MaxDeliveryCountExceeded on Shop'))
    assert.ok(links.some((l) => l.href === `/aws/c/sqs/${AWS}?tab=dead-letters&reason=__none__`), 'no-reason filter')
    assert.ok(links.some((l) => l.href === '/azure/approvals'))
    assert.ok(!links.some((l) => l.href.includes(GCP)), 'nothing to open where nothing was read')
  })

  it('shows waiting approvals', () => assert.match(markdown, /\*\*7\*\* wait in \*\*Pending approvals\*\*/))

  it('never prints a message body', () => assert.ok(!markdown.includes(SECRET) && !JSON.stringify(links).includes(SECRET)))

  it('asks only for active dead letters, one row, per connection that can replay', () => {
    const lists = reader.calls.filter((c) => c.path === '/dead-letters')
    assert.deepEqual(lists.map((c) => c.query.namespaceId).sort(), [AZ, AWS, GCP, AZ_RO].sort())
    for (const c of lists) assert.deepEqual({ ...c.query, namespaceId: undefined }, { namespaceId: undefined, status: 'active', pageSize: 1 })
  })
})

describe('live: dead letters (status)', () => {
  it('gives no fix-first tips or read-only notes when the question is not about replaying', async () => {
    const { markdown } = await createLiveProvider(fakeReader())('dlq-status')
    assert.doesNotMatch(markdown, /⚠️|read-only/)
  })

  it('still answers when one connection\'s reasons cannot be read', async () => {
    const { markdown } = await createLiveProvider(fakeReader({}, { namespaceId: AZ }))('dlq-status')
    assert.match(markdown, /\*\*Shop\*\*[^\n]*\n  - reasons could not be read/)
    assert.match(markdown, /2 TTLExpiredException/)
  })

  it('says when nothing is connected', async () => {
    const { markdown, links } = await createLiveProvider(fakeReader({ '/fleet/components': { components: [] } }))('dlq-status')
    assert.match(markdown, /no cloud is connected yet/)
    assert.deepEqual(links, [{ label: 'Open Connections', href: '/connections' }])
  })

  it('says when nothing is in dead-letter', async () => {
    const quiet = { components: [{ namespaceId: AZ, componentKey: 'service-bus', cloud: 'azure', name: 'Shop', componentName: 'Service Bus', environment: 'dev', canSend: true, reachable: true, lastError: null, watched: true, deadLetters: { value: 0, kind: 'exact' }, recorded: null, approvals: 0, handOffs: 0, entities: [] }] }
    const { markdown, links } = await createLiveProvider(fakeReader({ '/fleet/components': quiet }))('dlq-replay')
    assert.match(markdown, /Nothing is in dead-letter on your connections right now/)
    assert.deepEqual(links, [])
  })

  it('lets a failed read fail the live part (the answer then says why)', async () => {
    await assert.rejects(createLiveProvider(fakeReader({}, { path: '/fleet/components' }))('dlq-status'), /fixture/)
  })
})

describe('live: replay results', () => {
  it('uses the outcome words, per cloud, and the stayed-fixed rate only where there is one', async () => {
    const { markdown, links } = await createLiveProvider(fakeReader())('replay-results')
    assert.match(markdown, /\*\*Azure:\*\* 6 Verified, 2 Came back; also 12 Declined by a person/)
    assert.match(markdown, /\*\*75%\*\* stayed fixed/)
    assert.match(markdown, /\*\*AWS:\*\* 5 Unverified, 4 Watching, 1 Not sent$/m)
    assert.equal(markdown.match(/stayed fixed/g)?.length, 1, 'AWS has no rate: none is shown, not 0%')
    assert.deepEqual(links, [{ label: 'Open Replay history', href: '/all/replays' }])
  })

  it('says "no replays" for an empty week', async () => {
    const { markdown } = await createLiveProvider(fakeReader({ '/recovery/summary': { window: '7d', total: 0, states: [], byProvider: [], stayedFixedRate: null } }))('replay-results')
    assert.match(markdown, /no replays/)
  })
})

describe('live: connections', () => {
  it('lists every connection, including one with no component switched on, with its last test', async () => {
    const { markdown } = await createLiveProvider(fakeReader())('connections')
    assert.match(markdown, /\*\*Shop\*\* \(Azure, prod\): Service Bus; last connection test passed \(2026-11-15 08:00 UTC\)\./)
    assert.match(markdown, /\*\*aws-eu\*\* \(AWS, dev\): SQS, Step Functions; last connection test \*\*failed\*\*/)
    assert.match(markdown, /\*\*Pipeline\*\* \(Google Cloud, uat, switched off\): Pub\/Sub; never tested\. ⚠️ Its last read failed: Permission denied/)
    assert.match(markdown, /\*\*Audit\*\* \(Azure, dev, read-only\)/)
  })
})
