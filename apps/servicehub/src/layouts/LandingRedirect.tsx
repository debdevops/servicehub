import { useEffect, useRef } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { landingPath } from '../nav/navigation'
import { lastPage, readPreferences } from '../lib/preferences'

/**
 * Applies the landing rule (IA §2.4) once, when the app first opens with what is connected known.
 *
 * Only a bare visit to `/` is redirected: a deep link (`/?modal=…`, `/fleet`, an Advanced page) goes
 * where it says. And only once — afterwards Home is a place you can choose to go, so clicking it
 * must not bounce you to Fleet Overview.
 */
export function LandingRedirect({ ready, connectedCloudCount }: { ready: boolean; connectedCloudCount: number }) {
  const { pathname, search } = useLocation()
  const navigate = useNavigate()
  const applied = useRef(false)

  useEffect(() => {
    if (!ready || applied.current) return
    applied.current = true
    if (pathname !== '/' || search !== '') return
    // "Open on: last used" (Settings) wins over the landing rule; the landing rule is the default.
    const last = readPreferences().openOn === 'last' ? lastPage() : null
    const target = last ?? landingPath(connectedCloudCount)
    if (target !== '/') navigate(target, { replace: true })
  }, [ready, connectedCloudCount, pathname, search, navigate])

  return null
}
