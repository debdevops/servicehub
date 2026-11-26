import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { ftsAny, groups, similarity, stem, terms } from '../src/text.ts'

describe('terms', () => {
  it('keeps the meaningful words, lowercased, once each', () => {
    assert.deepEqual(terms('How do I replay my DLQ? Replay it!'), ['replay', 'dlq'])
  })
  it('keeps numbers and drops one-letter words', () => {
    assert.deepEqual(terms('a 5 x 10 retries'), ['5', '10', 'retries'])
  })
  it('stops after 12 words', () => {
    assert.equal(terms(Array.from({ length: 40 }, (_, i) => `word${i}`).join(' ')).length, 12)
  })
})

describe('groups', () => {
  it('adds synonyms after the word itself', () => {
    assert.deepEqual(groups('replay dlq', new Map([['dlq', ['dead letter']]])), [['replay'], ['dlq', 'dead letter']])
  })
})

describe('ftsAny', () => {
  it('quotes every alternative so nothing is read as FTS5 syntax', () => {
    assert.equal(ftsAny(['dead letter', 'a"b']), '"dead letter" OR "a""b"')
  })
})

describe('similarity', () => {
  it('is 1 for the same meaningful words', () => {
    assert.equal(similarity([['replay'], ['work']], 'Did my replay work?'), 1)
  })
  it('matches a phrase synonym only when all its words are there', () => {
    assert.equal(similarity([['dlq', 'dead letter']], 'Dead letters'), 1)
    assert.equal(similarity([['dlq', 'dead letter']], 'Dead people'), 0)
  })
  it('is lower when the stored question says much more', () => {
    assert.ok(similarity([['replay']], 'Replay dead-lettered messages from a queue') < 0.5)
  })
})

describe('stem', () => {
  it('folds common endings', () => {
    assert.deepEqual(['letters', 'lettered', 'replaying', 'messages', 'queues'].map(stem), ['letter', 'letter', 'replay', 'message', 'queue'])
  })
  it('makes singular and plural meet', () => {
    assert.equal(stem('messages'), stem('message'))
    assert.equal(stem('connections'), stem('connection'))
  })
  it('leaves short words and -ss alone', () => {
    assert.deepEqual(['runs', 'bus', 'sqs', 'access'].map(stem), ['runs', 'bus', 'sqs', 'access'])
  })
})
