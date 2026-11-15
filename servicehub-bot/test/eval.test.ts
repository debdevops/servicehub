import assert from 'node:assert/strict'
import { after, describe, it } from 'node:test'
import { ask } from '../src/answer.ts'
import { evalCases, outOfScope } from './eval-cases.ts'
import { openFreshKb } from './helpers.ts'

describe('eval: the right entry answers each real question', () => {
  const kb = openFreshKb()
  after(() => kb.close())

  for (const { q, expect } of evalCases) {
    it(`"${q}" → ${expect}`, async () => {
      const answer = await ask(kb, q)
      const ranked = kb.search(q, 3).hits.map((h) => `${h.entry.id} (${h.score.toFixed(2)})`)
      assert.equal(answer.status, 'answered', `not answered; top: ${ranked.join(', ')}`)
      assert.equal(answer.source?.id, expect, `top: ${ranked.join(', ')}`)
    })
  }

  for (const q of outOfScope) {
    it(`"${q}" is not answered`, async () => {
      const answer = await ask(kb, q)
      assert.notEqual(answer.status, 'answered', `answered with ${answer.source?.id}`)
    })
  }
})
