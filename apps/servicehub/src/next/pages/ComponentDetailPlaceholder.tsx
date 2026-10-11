import { useSearchParams } from 'react-router-dom'
import { componentTabs, type NextPage } from '../nav/navigation'
import { NextPlaceholderPage } from './NextPlaceholderPage'

/** A component's own page, before unit D2: the placeholder for whichever tab the address names (`?tab=`), or the first tab. */
export function ComponentDetailPlaceholder({ entry }: { entry: NextPage }) {
  const [params] = useSearchParams()
  const tab = componentTabs.find((t) => t.value === params.get('tab')) ?? componentTabs[0]
  return <NextPlaceholderPage entry={entry} tab={tab} />
}
