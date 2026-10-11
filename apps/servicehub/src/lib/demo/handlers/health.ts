import type { Route } from './http'

export const health: readonly Route[] = [['get', /^\/health$/, () => ({ status: 'Healthy' })]]
