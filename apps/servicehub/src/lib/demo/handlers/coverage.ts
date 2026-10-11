import type { CoverageResponse } from '../../api/coverage'
// The demo's copy of the catalogue. `CoverageEndpointTests` (backend) fails if it differs from the real one, so it is never edited by hand.
import raw from '../world/coverage.json?raw'
import type { Route } from './http'

const catalogue = JSON.parse(raw) as CoverageResponse

export const coverage: readonly Route[] = [['get', /^\/fleet\/coverage$/, (): CoverageResponse => catalogue]]
