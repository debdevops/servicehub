import { BookOpen } from 'lucide-react'
import { Link, useLocation } from 'react-router-dom'
import { guideTopic } from '../../content/guides/links'
import { pageHelpStep } from '../../content/help'
import { useGuideCloud } from './useGuideCloud'

/** The small book beside a page's title: opens Help on that page's step of the guide, over the page you are on. */
export default function PageHelpLink({ page }: { page: keyof typeof pageHelpStep }) {
  const { pathname, search } = useLocation()
  const cloud = useGuideCloud()
  const next = new URLSearchParams(search)
  next.delete('modal')
  next.set('panel', 'help'); next.set('topic', guideTopic(cloud)); next.set('step', pageHelpStep[page])
  return (
    <Link to={`${pathname}?${next}`} aria-label="Help for this page" title="Help for this page" className="rounded-full p-1 text-[var(--color-text-muted)] hover:bg-[var(--color-surface-muted)]">
      <BookOpen className="h-4 w-4" aria-hidden="true" />
    </Link>
  )
}
