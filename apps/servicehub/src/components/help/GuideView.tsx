import type { Guide } from '../../content/guides/types'

/** The step-by-step guide for one cloud: real screenshots, each with a numbered key. Plain markdown-ish text, no tour. */
export default function GuideView({ guide, openStep }: { guide: Guide; openStep?: string | null }) {
  return (
    <div className="space-y-4">
      {guide.sections.map((section) => (
        <section key={section.part} aria-label={section.part}>
          <h4 className="mb-1 text-[10.5px] font-bold uppercase tracking-[0.6px] text-[var(--color-text-muted)]">{section.part}</h4>
          <ul className="divide-y divide-[var(--color-border)] rounded-xl border border-[var(--color-border)]">
            {section.steps.map((step) => (
              <li key={step.title} id={`guide-step-${step.title.split(' ')[0]}`}>
                <details open={!!openStep && step.title.startsWith(`${openStep} `)}>
                  <summary className="cursor-pointer px-4 py-2.5 font-semibold">{step.title}</summary>
                  <div className="space-y-3 px-4 pb-3 text-[var(--color-text-muted)]">
                    <p>{step.text.replace(/\*\*/g, '')}</p>
                    {step.shots.map((shot) => (
                      <figure key={shot.image} className="space-y-2">
                        <a href={shot.image} target="_blank" rel="noreferrer">
                          <img src={shot.image} alt={shot.alt} loading="lazy" className="w-full rounded-lg border border-[var(--color-border)]" />
                        </a>
                        {shot.keys.length > 0 && (
                          <ol className="space-y-1">
                            {shot.keys.map((k) => (
                              <li key={k.n} className="flex gap-2">
                                <span aria-hidden="true" className="mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-rose-600 text-[11px] font-bold text-white">{k.n}</span>
                                <span>{k.text}</span>
                              </li>
                            ))}
                          </ol>
                        )}
                      </figure>
                    ))}
                  </div>
                </details>
              </li>
            ))}
          </ul>
        </section>
      ))}
      <section aria-label="If something goes wrong">
        <h4 className="mb-1 text-[10.5px] font-bold uppercase tracking-[0.6px] text-[var(--color-text-muted)]">If something goes wrong</h4>
        <ul className="space-y-1 text-[var(--color-text-muted)]">
          {guide.troubleshooting.map((t) => <li key={t.q}><b className="text-[var(--color-text)]">{t.q}.</b> {t.a.replace(/\*\*/g, '')}</li>)}
        </ul>
      </section>
    </div>
  )
}
