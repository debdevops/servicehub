import assert from 'node:assert/strict'
import { after, describe, it } from 'node:test'
import { ask, asksToAct, MAX_QUESTION_CHARS } from '../src/answer.ts'
import type { LiveIntent } from '../src/types.ts'
import { openFreshKb } from './helpers.ts'

describe('asksToAct', () => {
  for (const q of ['replay them for me', 'Can you replay these?', 'could you please purge them', 'delete all', 'please approve it', 'do it on my behalf']) {
    it(`"${q}" asks the bot to act`, () => assert.equal(asksToAct(q), true))
  }
  for (const q of ['how do I replay dlq', 'can I replay this?', 'why can you not delete?', 'replay status', 'what does purge do']) {
    it(`"${q}" asks how, not to act`, () => assert.equal(asksToAct(q), false))
  }
})

describe('ask', () => {
  const kb = openFreshKb()
  after(() => kb.close())

  it('says plainly it does not act, then gives the path', async () => {
    const answer = await ask(kb, 'can you replay them for me')
    assert.match(answer.markdown, /^I can't do that for you/)
    assert.match(answer.markdown, /Dead-letter messages/)
    assert.ok(answer.links.length > 0)
  })

  it('does not add the "can\'t do that" line to a how-to question', async () => {
    const answer = await ask(kb, 'how do I replay dlq')
    assert.doesNotMatch(answer.markdown, /can't do that/)
  })

  it('offers help instead of guessing when nothing fits', async () => {
    const answer = await ask(kb, 'weather in paris')
    assert.equal(answer.status, 'unknown')
    assert.ok(answer.suggestions.length > 0)
  })

  it('says it does not know, rather than suggesting doc headings that share one word', async () => {
    assert.equal((await ask(kb, 'write me a poem')).status, 'unknown')
  })

  it('offers real questions as "Did you mean" when part of the question matches', async () => {
    const answer = await ask(kb, 'replay weather')
    assert.equal(answer.status, 'suggest')
    assert.ok(answer.suggestions.includes('Replay dead-lettered messages'), answer.suggestions.join(' | '))
  })

  it('answers nothing for a question with no meaningful words', async () => {
    assert.equal((await ask(kb, 'how do I?')).status, 'unknown')
    assert.equal((await ask(kb, '   ')).status, 'unknown')
  })

  it('reads FTS5 syntax in a question as plain words', async () => {
    for (const q of ['replay" OR "x', 'dlq NEAR(replay)', 'title:replay*', '-replay ^dlq']) {
      await assert.doesNotReject(ask(kb, q), q)
    }
  })

  it('cuts an over-long paste instead of failing', async () => {
    const answer = await ask(kb, `how do I replay dlq ${'x'.repeat(MAX_QUESTION_CHARS * 4)}`)
    assert.equal(answer.source?.id, 'dlq-replay')
  })

  it('links only to app addresses or https documentation', async () => {
    for (const q of ['how do I replay dlq', 'did my replay work', 'which clouds are connected', 'auto replay rule', 'run with docker', 'rotate encryption key']) {
      for (const link of (await ask(kb, q)).links) {
        assert.match(link.href, /^(\/[a-z0-9/-]*|https:\/\/github\.com\/debdevops\/servicehub\/.+)$/, `${q}: ${link.href}`)
      }
    }
  })

  it('puts live numbers above the how-to when the entry asks for them', async () => {
    const asked: LiveIntent[] = []
    const answer = await ask(kb, 'how do I replay dlq', {
      live: async (intent) => {
        asked.push(intent)
        return { markdown: '**Right now:** 3 active dead letters.', links: [{ label: 'Open the 3', href: '/azure' }] }
      },
      now: () => new Date('2026-11-15T12:00:00Z'),
    })
    assert.deepEqual(asked, ['dlq-replay'])
    assert.ok(answer.markdown.indexOf('Right now') < answer.markdown.indexOf('One message'))
    assert.deepEqual(answer.live, { ok: true, asOf: '2026-11-15T12:00:00.000Z' })
    assert.equal(answer.links[0]?.href, '/azure')
  })

  it('still gives the how-to, and says why, when live numbers cannot be read', async () => {
    const answer = await ask(kb, 'how do I replay dlq', {
      live: async () => {
        throw new Error('ServiceHub did not answer within 5 seconds')
      },
    })
    assert.equal(answer.status, 'answered')
    assert.equal(answer.live?.ok, false)
    assert.match(answer.markdown, /couldn't read your live numbers: ServiceHub did not answer/)
    assert.match(answer.markdown, /One message/)
  })

  it('never asks for live data for an entry without a live intent', async () => {
    let called = false
    await ask(kb, 'rotate encryption key', { live: async () => ((called = true), { markdown: '', links: [] }) })
    assert.equal(called, false)
  })
})
