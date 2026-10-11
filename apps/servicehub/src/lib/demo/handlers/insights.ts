import type { InsightList } from '../../api/insights'
import { lowerProvider } from '../world/derive'
import type { Route } from './http'

export const insights: readonly Route[] = [
  ['get', /^\/insights$/, (_m, { w, params, now }): InsightList => {
    const provider = lowerProvider(params.provider)
    const mine = w.insights.filter((i) => !provider || i.provider === provider)
    return { current: mine.filter((i) => !i.clearedAt), cleared: params.cleared ? mine.filter((i) => i.clearedAt) : [], lastLookedAt: new Date(now - 120_000).toISOString() }
  }],
]
