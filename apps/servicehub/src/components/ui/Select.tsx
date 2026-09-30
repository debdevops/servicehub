import { Children, isValidElement, useEffect, useId, useMemo, useRef, useState, type ReactElement, type ReactNode } from 'react'
import { Check, ChevronDown } from 'lucide-react'

type Opt = { value: string; label: ReactNode; text: string; disabled: boolean }

function parse(children: ReactNode): Opt[] {
  const out: Opt[] = []
  const walk = (nodes: ReactNode) =>
    Children.forEach(nodes, (c) => {
      if (!isValidElement(c)) return
      const el = c as ReactElement<{ value?: string | number; children?: ReactNode; disabled?: boolean }>
      if (el.type === 'option') {
        const label = el.props.children
        const text = Children.toArray(label).join('')
        out.push({ value: String(el.props.value ?? text), label, text, disabled: !!el.props.disabled })
      } else if (el.props.children) walk(el.props.children)
    })
  walk(children)
  return out
}

/**
 * The one dropdown for the whole app — the same look as the namespace picker: a rounded control that opens a
 * floating list with the chosen row ticked. It takes plain `<option>` children so call sites read like a `<select>`,
 * but `onChange` receives the chosen value directly.
 *
 * `variant="card"` shows a small caption above the value (page-header filters); `variant="field"` is a full-width
 * form control; `variant="inline"` is the small one used inside toolbars and table footers.
 */
export function Select({
  value,
  onChange,
  children,
  label,
  ariaLabel,
  id,
  disabled,
  variant = 'field',
  className = '',
  icon,
}: {
  value: string
  onChange: (value: string) => void
  children: ReactNode
  /** Caption shown above the value on the card variant. */
  label?: string
  ariaLabel?: string
  id?: string
  disabled?: boolean
  variant?: 'card' | 'field' | 'inline'
  className?: string
  icon?: ReactNode
}) {
  const [open, setOpen] = useState(false)
  const root = useRef<HTMLDivElement>(null)
  const listId = useId()
  const options = useMemo(() => parse(children), [children])
  const selectedIndex = options.findIndex((o) => o.value === value)
  const [active, setActive] = useState(0)
  const current = options[selectedIndex]

  useEffect(() => {
    if (!open) return
    const away = (e: MouseEvent) => root.current && !root.current.contains(e.target as Node) && setOpen(false)
    document.addEventListener('mousedown', away)
    return () => document.removeEventListener('mousedown', away)
  }, [open])

  function toggle() {
    if (disabled) return
    setActive(Math.max(0, selectedIndex))
    setOpen((o) => !o)
  }
  function pick(o: Opt) {
    if (o.disabled) return
    setOpen(false)
    if (o.value !== value) onChange(o.value)
  }
  function onKeyDown(e: React.KeyboardEvent) {
    if (e.key === 'Escape') return setOpen(false)
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault()
      if (!open) return toggle()
      const step = e.key === 'ArrowDown' ? 1 : -1
      setActive((i) => (i + step + options.length) % options.length)
    } else if ((e.key === 'Enter' || e.key === ' ') && open) {
      e.preventDefault()
      const o = options[active]
      if (o) pick(o)
    }
  }

  const trigger = {
    card: 'min-w-[170px] gap-2.5 rounded-xl border border-[var(--color-primary-200)] bg-gradient-to-br from-[var(--color-primary-50)] to-[var(--color-surface)] px-3 py-1.5 shadow-[var(--shadow-card)] hover:border-[var(--color-primary-400)] hover:shadow-md',
    field: 'w-full gap-2 rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2 text-sm hover:border-[var(--color-primary-400)]',
    inline: 'gap-1.5 rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-2 py-1 text-[inherit] hover:border-[var(--color-primary-400)]',
  }[variant]

  return (
    <div ref={root} onKeyDown={onKeyDown} className={`relative ${variant === 'field' ? 'block' : 'inline-block'} ${className}`}>
      <button
        type="button"
        id={id}
        disabled={disabled}
        aria-label={ariaLabel}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={listId}
        onClick={toggle}
        className={`flex items-center justify-between text-left font-normal normal-case tracking-normal text-[var(--color-text)] transition disabled:cursor-not-allowed disabled:opacity-60 ${trigger}`}
      >
        {icon}
        <span className="min-w-0 flex-1">
          {variant === 'card' && label && <span className="block text-[9.5px] font-bold uppercase tracking-[0.6px] text-[var(--color-text-muted)]">{label}</span>}
          <span className={`block truncate ${variant === 'card' ? 'text-[13px] font-bold' : ''}`}>{current ? current.label : ' '}</span>
        </span>
        <ChevronDown className={`h-4 w-4 shrink-0 text-[var(--color-text-muted)] transition-transform ${open ? 'rotate-180' : ''}`} aria-hidden="true" />
      </button>
      {open && (
        <ul
          id={listId}
          role="listbox"
          aria-label={ariaLabel ?? label}
          className="absolute left-0 z-40 mt-1.5 max-h-[320px] w-max min-w-full max-w-[92vw] overflow-y-auto rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-1 shadow-xl"
        >
          {options.map((o, i) => (
            <li
              key={o.value}
              role="option"
              data-value={o.value}
              aria-selected={o.value === value}
              aria-disabled={o.disabled || undefined}
              onMouseEnter={() => setActive(i)}
              onClick={() => pick(o)}
              className={[
                'flex cursor-pointer items-center justify-between gap-3 rounded-lg px-3 py-1.5 text-[13px] text-[var(--color-text)]',
                o.disabled ? 'cursor-not-allowed opacity-50' : '',
                o.value === value ? 'bg-[var(--color-primary-50)] font-semibold' : i === active ? 'bg-[var(--color-surface-muted)]' : '',
              ].join(' ')}
            >
              <span className="truncate">{o.label}</span>
              {o.value === value && <Check className="h-4 w-4 shrink-0 text-[var(--color-primary-700)]" aria-hidden="true" />}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
