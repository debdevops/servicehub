import axios from 'axios'
import { beforeEach, describe, expect, it } from 'vitest'
import { FORCE_KEY, installForceStates } from '@/dev/forceStates'

function client() {
  const api = axios.create({ baseURL: '/api/v1' })
  // The "real" backend: a list with a total, so `empty` has something to blank.
  api.defaults.adapter = (config) => Promise.resolve({ data: { items: [1, 2], total: 2 }, status: 200, statusText: 'OK', headers: {}, config })
  installForceStates(api)
  return api
}
const force = () => (window as unknown as { force: { add: (r: object) => void; clear: () => void; list: () => object[] } }).force

describe('forced states (6.1, dev only)', () => {
  beforeEach(() => { window.localStorage.removeItem(FORCE_KEY); window.history.pushState({}, '', '/') })

  it('passes everything through when nothing is forced', async () => {
    const api = client()
    expect((await api.get('/dead-letters')).data.total).toBe(2)
  })

  it('error → a 500, forbidden → a 403, network → no response', async () => {
    const api = client()
    force().add({ match: 'dead-letters', mode: 'error' })
    await expect(api.get('/dead-letters')).rejects.toMatchObject({ response: { status: 500 } })
    force().add({ match: 'dead-letters', mode: 'forbidden' })
    await expect(api.get('/dead-letters')).rejects.toMatchObject({ response: { status: 403 } })
    force().add({ match: 'dead-letters', mode: 'network' })
    const err = await api.get('/dead-letters').catch((e: unknown) => e as { code: string; response?: unknown })
    expect(err).toMatchObject({ code: 'ERR_NETWORK' })
    expect((err as { response?: unknown }).response).toBeUndefined()
  })

  it('a rule with a method only hits that method', async () => {
    const api = client()
    force().add({ match: 'dead-letters', mode: 'error', method: 'post' })
    expect((await api.get('/dead-letters')).data.total).toBe(2)
    await expect(api.post('/dead-letters')).rejects.toMatchObject({ response: { status: 500 } })
  })

  it('only the matching path is forced', async () => {
    const api = client()
    force().add({ match: 'recovery', mode: 'error' })
    expect((await api.get('/dead-letters')).data.total).toBe(2)
  })

  it('empty keeps the shape and blanks lists and totals; a given body wins', async () => {
    const api = client()
    force().add({ match: 'dead-letters', mode: 'empty' })
    expect((await api.get('/dead-letters')).data).toEqual({ items: [], total: 0 })
    force().add({ match: 'dead-letters', mode: 'empty', body: { hello: 1 } })
    expect((await api.get('/dead-letters')).data).toEqual({ hello: 1 })
  })

  it('empty also works on a real adapter that returns the raw JSON text', async () => {
    const api = client()
    api.defaults.adapter = (config) => Promise.resolve({ data: '{"items":[1],"total":1}', status: 200, statusText: 'OK', headers: {}, config })
    force().add({ match: 'dead-letters', mode: 'empty' })
    expect((await api.get('/dead-letters')).data).toEqual({ items: [], total: 0 })
  })

  it('slow holds the real answer back', async () => {
    const api = client()
    force().add({ match: 'dead-letters', mode: 'slow', ms: 60 })
    const t = Date.now()
    await api.get('/dead-letters')
    expect(Date.now() - t).toBeGreaterThanOrEqual(50)
  })

  it('?force= starts rules, survives, and ?force=off ends them', () => {
    window.history.pushState({}, '', '/?force=signatures:empty,recovery')
    client()
    expect(force().list()).toEqual([{ match: 'signatures', mode: 'empty' }, { match: 'recovery', mode: 'error' }])
    window.history.pushState({}, '', '/?force=off')
    client()
    expect(force().list()).toEqual([])
  })
})
