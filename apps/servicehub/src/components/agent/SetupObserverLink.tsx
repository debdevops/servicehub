import { Link, useInRouterContext, useLocation } from 'react-router-dom'
import { withoutDrawers } from '../../lib/urlState'

/** "Set up the observer →": opens Connections over the current page, where each cloud's observer is set up. Nothing outside a router. */
export function SetupObserverLink() {
  return useInRouterContext() ? <Inner /> : null
}

function Inner() {
  const { search } = useLocation()
  const next = withoutDrawers(new URLSearchParams(search))
  next.set('panel', 'connections')
  return (
    <Link to={`?${next}`} className="ml-1 whitespace-nowrap text-[var(--color-primary-700)] hover:underline">
      Set up the observer →
    </Link>
  )
}
