import { describe, expect, it } from 'vitest'
import type { Agent } from './api/agents'
import type { Namespace } from './api/namespaces'
import { agentLine } from './agentLine'

const ns = (proves: boolean, peeks = proves) => ({ id: String(Math.random()), capabilities: { canProveDlqAbsence: proves, supportsRepeatablePeek: peeks } }) as unknown as Namespace
const agent = (id: string, canAct: boolean, isPaused = false) => ({ id, name: id, canAct, isPaused }) as unknown as Agent
const agents = [agent('Auto Replay', true), agent('Bulk Replay', true), agent('DLQ Monitor', false)]

describe('agentLine — computed from capability, never from a cloud name', () => {
  it('says the Agent may act where the cloud can prove a fix held', () => {
    const l = agentLine('Azure', [ns(true)], 12, agents)
    expect(l.status).toBe('watching')
    expect(l.text).toBe('Watching 12 queues and subscriptions · Azure can prove a fix held, so the Agent may act here on its own once a failure has earned it')
  })
  it('says it asks first where the cloud cannot prove it — and that it is not watching on its own', () => {
    const l = agentLine('AWS', [ns(false)], 3, agents)
    expect(l.text).toContain('Not watching AWS on its own')
    expect(l.text).toContain('asks you before every replay')
  })
  it('does not trust a name: an "Azure" namespace without proof still asks first', () => expect(agentLine('Azure', [ns(false, true)], 1, agents).text).toContain('asks you before every replay'))
  it('says "will not act", never "stopped", when every acting agent is paused — and keeps watching', () => {
    const l = agentLine('Azure', [ns(true)], 12, [agent('Auto Replay', true, true), agent('Bulk Replay', true, true), agent('DLQ Monitor', false)])
    expect(l.status).toBe('will-not-act')
    expect(l.text).toContain('Watching 12 queues')
    expect(l.text).toContain('will not act')
    expect(l.text).not.toMatch(/stopped/i)
  })
  it('names what is paused when only some are', () => expect(agentLine('Azure', [ns(true)], 12, [agent('Auto Replay', true, true), agent('Bulk Replay', true)]).text).toContain('Auto Replay paused'))
})
