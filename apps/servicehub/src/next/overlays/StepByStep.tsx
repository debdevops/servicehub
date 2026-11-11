import { useState } from 'react'
import { guides } from '../../content/guides'
import GuideView from '../../components/help/GuideView'

/** One cloud's guide. Its body (dozens of steps with pictures) is mounted only once the section has been opened. */
function Guide({ cloud, id, open }: { cloud: string; id: string; open: boolean }) {
  const [opened, setOpened] = useState(open)
  return (
    <details id={id} open={open || undefined} onToggle={(e) => { if (e.currentTarget.open) setOpened(true) }} className="rounded-xl border border-[var(--color-border)]">
      <summary className="cursor-pointer px-4 py-2.5 font-semibold">{guides[cloud].title}</summary>
      {opened && (
        <div className="px-3 pb-3">
          {/* Until the pictures are re-shot (build-plan unit I3): Part 1 is the cloud's own console and is current; the rest shows 4.2.0. */}
          <p role="note" className="mb-3 rounded-lg border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900">
            <strong>The ServiceHub pictures below are from version 4.2.0.</strong> Part 1, in the cloud’s own console, is current. From Part 2 on, the screens look different in this version: everything is now in the menu on the left. The steps are the same; the (i) beside each heading says what a page does.
          </p>
          <GuideView guide={guides[cloud]} />
        </div>
      )}
    </details>
  )
}

/** The three per-cloud guides inside the 5.0.0 Help (lazy-loaded by `HelpPanel`); `?topic=guide-<cloud>` opens one. */
export default function StepByStep({ topic, clouds, topicOf }: { topic: string | null; clouds: readonly string[]; topicOf: (cloud: string) => string }) {
  return (
    <div className="space-y-2">
      {clouds.map((cloud) => <Guide key={cloud} cloud={cloud} id={`help-${topicOf(cloud)}`} open={topic === topicOf(cloud)} />)}
    </div>
  )
}
