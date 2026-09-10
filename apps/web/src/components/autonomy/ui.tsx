import { useEffect, useRef, type ComponentType, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { X, ChevronLeft, ChevronRight, Search, AlertCircle, Info } from 'lucide-react';
import { ProviderBadge } from '@servicehub/ui-shared/lib/providerStyles';
import type { CloudProviderType } from '@servicehub/ui-shared/lib/api/types';

/**
 * Shared building blocks for the four Autonomous ServiceHub pages (Autonomy Control Center,
 * Recovery Evidence, Playbook Ledger, Governance) so they read as one product: the same header,
 * stat tiles, cards, callouts, filters, tabs and detail panel everywhere. Presentation only — no
 * data fetching, no business rules.
 */

export type Tone = 'blue' | 'green' | 'amber' | 'red' | 'violet' | 'teal' | 'indigo' | 'gray';

// Literal, fully-spelled Tailwind classes — Tailwind's JIT scanner only picks up class names that
// appear verbatim in source, so these cannot be built by string interpolation.
const TONE_TILE: Record<Tone, string> = {
  blue: 'bg-blue-50 text-blue-600 ring-blue-100',
  green: 'bg-green-50 text-green-600 ring-green-100',
  amber: 'bg-amber-50 text-amber-600 ring-amber-100',
  red: 'bg-red-50 text-red-600 ring-red-100',
  violet: 'bg-violet-50 text-violet-600 ring-violet-100',
  teal: 'bg-teal-50 text-teal-600 ring-teal-100',
  indigo: 'bg-indigo-50 text-indigo-600 ring-indigo-100',
  gray: 'bg-gray-100 text-gray-500 ring-gray-200',
};

const TONE_PILL: Record<Tone, string> = {
  blue: 'bg-blue-50 text-blue-700 ring-blue-200',
  green: 'bg-green-50 text-green-700 ring-green-200',
  amber: 'bg-amber-50 text-amber-800 ring-amber-200',
  red: 'bg-red-50 text-red-700 ring-red-200',
  violet: 'bg-violet-50 text-violet-700 ring-violet-200',
  teal: 'bg-teal-50 text-teal-700 ring-teal-200',
  indigo: 'bg-indigo-50 text-indigo-700 ring-indigo-200',
  gray: 'bg-gray-100 text-gray-600 ring-gray-200',
};

const TONE_CALLOUT: Record<Tone, string> = {
  blue: 'bg-blue-50 border-blue-200 text-blue-900',
  green: 'bg-green-50 border-green-200 text-green-900',
  amber: 'bg-amber-50 border-amber-200 text-amber-900',
  red: 'bg-red-50 border-red-200 text-red-900',
  violet: 'bg-violet-50 border-violet-200 text-violet-900',
  teal: 'bg-teal-50 border-teal-200 text-teal-900',
  indigo: 'bg-indigo-50 border-indigo-200 text-indigo-900',
  gray: 'bg-gray-50 border-gray-200 text-gray-700',
};

type IconType = ComponentType<{ className?: string }>;

export function IconTile({ icon: Icon, tone, size = 'md' }: { icon: IconType; tone: Tone; size?: 'sm' | 'md' | 'lg' }) {
  const box = size === 'lg' ? 'w-12 h-12 rounded-xl' : size === 'sm' ? 'w-8 h-8 rounded-lg' : 'w-10 h-10 rounded-lg';
  const glyph = size === 'lg' ? 'w-6 h-6' : size === 'sm' ? 'w-4 h-4' : 'w-5 h-5';
  return (
    <div className={`${box} ${TONE_TILE[tone]} ring-1 flex items-center justify-center shrink-0`} aria-hidden="true">
      <Icon className={glyph} />
    </div>
  );
}

/** Page title block: icon, title, one-line purpose, and right-aligned actions. */
export function PageHeader({
  icon, tone, title, subtitle, actions, children,
}: {
  icon: IconType;
  tone: Tone;
  title: string;
  subtitle: string;
  actions?: ReactNode;
  /** Banners (demo mode, emergency stop) rendered under the title row. */
  children?: ReactNode;
}) {
  return (
    <header className="bg-white border-b border-gray-200 px-4 sm:px-6 py-4 shrink-0">
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div className="flex items-start gap-3 min-w-0">
          <IconTile icon={icon} tone={tone} size="lg" />
          <div className="min-w-0">
            <h1 className="text-xl font-bold text-gray-900 leading-tight">{title}</h1>
            <p className="text-sm text-gray-500 mt-0.5 max-w-3xl">{subtitle}</p>
          </div>
        </div>
        {actions && <div className="flex items-center gap-2 flex-wrap">{actions}</div>}
      </div>
      {children}
    </header>
  );
}

/** A headline number. `value` is rendered as-is — pass "—" (with a `hint`) rather than a fabricated 0. */
export function StatCard({
  icon, tone, label, value, hint, to, title,
}: {
  icon: IconType;
  tone: Tone;
  label: string;
  value: ReactNode;
  hint?: ReactNode;
  to?: string;
  title?: string;
}) {
  const body = (
    <div
      title={title}
      className={`h-full bg-white border border-gray-200 rounded-xl p-4 shadow-sm flex items-start gap-3 ${
        to ? 'hover:border-primary-300 hover:shadow transition-all' : ''
      }`}
    >
      <IconTile icon={icon} tone={tone} />
      <div className="min-w-0">
        <div className="text-xs font-medium text-gray-500">{label}</div>
        <div className="text-2xl font-bold text-gray-900 leading-tight mt-0.5">{value}</div>
        {hint && <div className="text-xs text-gray-500 mt-0.5">{hint}</div>}
      </div>
    </div>
  );
  return to ? <Link to={to} className="block h-full">{body}</Link> : body;
}

export function Card({
  title, icon, tone = 'gray', action, children, className = '', bodyClassName = 'p-4', id,
}: {
  title?: ReactNode;
  icon?: IconType;
  tone?: Tone;
  action?: ReactNode;
  children: ReactNode;
  className?: string;
  bodyClassName?: string;
  id?: string;
}) {
  return (
    <section id={id} className={`bg-white border border-gray-200 rounded-xl shadow-sm ${className}`}>
      {title && (
        <div className="px-4 py-3 border-b border-gray-100 flex items-center justify-between gap-3">
          <h2 className="text-sm font-semibold text-gray-900 flex items-center gap-2 min-w-0">
            {icon && <IconTile icon={icon} tone={tone} size="sm" />}
            <span className="truncate">{title}</span>
          </h2>
          {action && <div className="shrink-0">{action}</div>}
        </div>
      )}
      <div className={bodyClassName}>{children}</div>
    </section>
  );
}

export function Callout({
  tone = 'blue', icon: Icon = Info, title, children, className = '',
}: {
  tone?: Tone;
  icon?: IconType;
  title?: ReactNode;
  children?: ReactNode;
  className?: string;
}) {
  return (
    <div className={`flex items-start gap-2.5 px-3.5 py-3 rounded-lg border text-sm ${TONE_CALLOUT[tone]} ${className}`}>
      <Icon className="w-4 h-4 shrink-0 mt-0.5" />
      <div className="min-w-0">
        {title && <div className="font-semibold">{title}</div>}
        {children && <div className={title ? 'mt-0.5 text-xs leading-relaxed opacity-90' : 'text-xs leading-relaxed'}>{children}</div>}
      </div>
    </div>
  );
}

export function Pill({ tone, children, icon: Icon, title }: { tone: Tone; children: ReactNode; icon?: IconType; title?: string }) {
  return (
    <span title={title} className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium ring-1 ring-inset whitespace-nowrap ${TONE_PILL[tone]}`}>
      {Icon && <Icon className="w-3 h-3" />}
      {children}
    </span>
  );
}

const PRIMARY_BUTTON =
  'inline-flex items-center justify-center gap-1.5 px-3 py-2 text-sm font-medium text-white bg-primary-600 hover:bg-primary-700 rounded-lg shadow-sm transition-colors disabled:opacity-50 disabled:cursor-not-allowed focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-1';
const SECONDARY_BUTTON =
  'inline-flex items-center justify-center gap-1.5 px-3 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-200 hover:bg-gray-50 rounded-lg shadow-sm transition-colors disabled:opacity-50 disabled:cursor-not-allowed focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-1';

export const buttonClass = { primary: PRIMARY_BUTTON, secondary: SECONDARY_BUTTON };

export function SearchInput({
  value, onChange, placeholder, label,
}: { value: string; onChange: (value: string) => void; placeholder: string; label: string }) {
  return (
    <div className="relative flex-1 min-w-[12rem]">
      <Search className="w-4 h-4 text-gray-400 absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none" />
      <input
        type="search"
        value={value}
        onChange={e => onChange(e.target.value)}
        placeholder={placeholder}
        aria-label={label}
        className="w-full text-sm border border-gray-200 rounded-lg pl-9 pr-3 py-2 bg-white text-gray-800 placeholder:text-gray-400 focus:outline-none focus:ring-2 focus:ring-primary-500"
      />
    </div>
  );
}

export function FilterSelect<T extends string>({
  value, onChange, options, label,
}: { value: T; onChange: (value: T) => void; options: ReadonlyArray<{ value: T; label: string }>; label: string }) {
  return (
    <select
      value={value}
      onChange={e => onChange(e.target.value as T)}
      aria-label={label}
      className="text-sm border border-gray-200 rounded-lg px-3 py-2 bg-white text-gray-700 focus:outline-none focus:ring-2 focus:ring-primary-500"
    >
      {options.map(o => (
        <option key={o.value} value={o.value}>{o.label}</option>
      ))}
    </select>
  );
}

export function Tabs<T extends string>({
  tabs, active, onChange, label,
}: {
  tabs: ReadonlyArray<{ id: T; label: string; count?: number; hint?: string }>;
  active: T;
  onChange: (id: T) => void;
  label: string;
}) {
  return (
    <div role="tablist" aria-label={label} className="flex items-center gap-1 border-b border-gray-200 overflow-x-auto">
      {tabs.map(tab => {
        const selected = tab.id === active;
        return (
          <button
            key={tab.id}
            type="button"
            role="tab"
            aria-selected={selected}
            title={tab.hint}
            onClick={() => onChange(tab.id)}
            className={`px-3 py-2 text-sm whitespace-nowrap border-b-2 -mb-px transition-colors ${
              selected ? 'border-primary-600 text-primary-700 font-semibold' : 'border-transparent text-gray-500 hover:text-gray-800'
            }`}
          >
            {tab.label}
            {tab.count !== undefined && (
              <span className={`ml-1.5 text-xs px-1.5 py-0.5 rounded-full ${selected ? 'bg-primary-100 text-primary-700' : 'bg-gray-100 text-gray-500'}`}>
                {tab.count}
              </span>
            )}
          </button>
        );
      })}
    </div>
  );
}

export function Pagination({
  page, pageCount, total, pageSize, onPage, noun,
}: { page: number; pageCount: number; total: number; pageSize: number; onPage: (page: number) => void; noun: string }) {
  if (total === 0) return null;
  const from = page * pageSize + 1;
  const to = Math.min(total, (page + 1) * pageSize);
  return (
    <div className="flex items-center justify-between gap-3 px-4 py-2.5 border-t border-gray-100 text-xs text-gray-500">
      <span>Showing {from}–{to} of {total} {noun}</span>
      {pageCount > 1 && (
        <div className="flex items-center gap-1">
          <button
            type="button"
            onClick={() => onPage(page - 1)}
            disabled={page === 0}
            aria-label="Previous page"
            className="p-1.5 rounded-md border border-gray-200 hover:bg-gray-50 disabled:opacity-40"
          >
            <ChevronLeft className="w-3.5 h-3.5" />
          </button>
          <span className="px-2 tabular-nums">{page + 1} / {pageCount}</span>
          <button
            type="button"
            onClick={() => onPage(page + 1)}
            disabled={page >= pageCount - 1}
            aria-label="Next page"
            className="p-1.5 rounded-md border border-gray-200 hover:bg-gray-50 disabled:opacity-40"
          >
            <ChevronRight className="w-3.5 h-3.5" />
          </button>
        </div>
      )}
    </div>
  );
}

/**
 * Selected-item detail. When the page itself is wide enough (a `@6xl` container query on the
 * page's `@container` wrapper — deliberately not the viewport, since Quick Access and the
 * Namespaces panel can take half a wide screen) it sits as a column beside the list; otherwise it
 * becomes a slide-over with a backdrop. Escape closes it either way, and
 * focus moves to its heading on open so keyboard and screen-reader users land in it.
 */
export function DetailPanel({
  title, subtitle, onClose, children, footer, badge,
}: {
  title: ReactNode;
  subtitle?: ReactNode;
  badge?: ReactNode;
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
}) {
  const headingRef = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    headingRef.current?.focus();
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <>
      <div className="fixed inset-0 bg-black/30 z-40 @6xl:hidden" onClick={onClose} aria-hidden="true" />
      <aside
        aria-label="Details"
        className="fixed inset-y-0 right-0 z-50 w-full max-w-md bg-white shadow-2xl flex flex-col @6xl:sticky @6xl:top-0 @6xl:z-auto @6xl:max-w-none @6xl:w-auto @6xl:shadow-sm @6xl:border @6xl:border-gray-200 @6xl:rounded-xl @6xl:max-h-[calc(100vh-12rem)]"
      >
        <div className="px-4 py-3 border-b border-gray-100 flex items-start justify-between gap-3 shrink-0">
          <div className="min-w-0">
            <div className="flex items-center gap-2 flex-wrap">
              {/* Focused programmatically on open so keyboard/screen-reader users land here; the global
                *:focus-visible ring (styles/index.css) is suppressed inline because a heading that was
                never tabbed to shouldn't look like an interactive control. */}
              <h2 ref={headingRef} tabIndex={-1} style={{ boxShadow: 'none' }} className="text-sm font-semibold text-gray-900 outline-none">
                {title}
              </h2>
              {badge}
            </div>
            {subtitle && <div className="text-xs text-gray-500 mt-0.5">{subtitle}</div>}
          </div>
          <button type="button" onClick={onClose} aria-label="Close details" className="p-1 rounded-md hover:bg-gray-100 shrink-0">
            <X className="w-4 h-4 text-gray-500" />
          </button>
        </div>
        <div className="flex-1 overflow-y-auto p-4 space-y-4">{children}</div>
        {footer && <div className="px-4 py-3 border-t border-gray-100 shrink-0">{footer}</div>}
      </aside>
    </>
  );
}

export function LoadingBlock({ label = 'Loading…' }: { label?: string }) {
  return (
    <div className="flex flex-col items-center justify-center py-16 gap-3" role="status" aria-live="polite">
      <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600" />
      <span className="text-xs text-gray-500">{label}</span>
    </div>
  );
}

export function ErrorBlock({ title, detail, onRetry }: { title: string; detail?: ReactNode; onRetry?: () => void }) {
  return (
    <div className="flex flex-col items-center justify-center text-center py-14 px-6">
      <AlertCircle className="w-10 h-10 text-red-400 mb-3" />
      <p className="text-gray-700 font-medium">{title}</p>
      {detail && <p className="text-xs text-gray-500 mt-1 max-w-md">{detail}</p>}
      {onRetry && (
        <button type="button" onClick={onRetry} className={`${SECONDARY_BUTTON} mt-4`}>
          Try again
        </button>
      )}
    </div>
  );
}

export function EmptyBlock({ icon: Icon, title, children, action }: { icon: IconType; title: string; children?: ReactNode; action?: ReactNode }) {
  return (
    <div className="flex flex-col items-center justify-center text-center py-14 px-6">
      <div className="w-14 h-14 rounded-2xl bg-gray-100 flex items-center justify-center mb-4">
        <Icon className="w-7 h-7 text-gray-400" />
      </div>
      <p className="text-gray-800 font-semibold">{title}</p>
      {children && <div className="text-sm text-gray-500 mt-1 max-w-md">{children}</div>}
      {action && <div className="mt-4">{action}</div>}
    </div>
  );
}

const KNOWN_PROVIDERS: readonly CloudProviderType[] = ['azure', 'aws', 'gcp'];

export function normalizeProvider(provider: string | null | undefined): CloudProviderType | null {
  const normalized = provider?.toLowerCase() as CloudProviderType | undefined;
  return normalized && KNOWN_PROVIDERS.includes(normalized) ? normalized : null;
}

export const PROVIDER_LABELS: Record<CloudProviderType, string> = { azure: 'Azure', aws: 'AWS', gcp: 'GCP' };

export function CloudBadge({ provider }: { provider: string | null | undefined }) {
  const normalized = normalizeProvider(provider);
  return normalized ? <ProviderBadge provider={normalized} /> : null;
}

export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return '—';
  return new Date(iso).toLocaleString(undefined, { month: 'short', day: '2-digit', hour: '2-digit', minute: '2-digit' });
}

export function formatFullDateTime(iso: string | null | undefined): string {
  if (!iso) return '—';
  return new Date(iso).toLocaleString();
}

export function truncateMiddle(value: string, keep = 10): string {
  return value.length > keep * 2 + 1 ? `${value.slice(0, keep)}…${value.slice(-4)}` : value;
}

/** "3 proposals" / "1 proposal". */
export function plural(count: number, singular: string, pluralForm = `${singular}s`): string {
  return `${count} ${count === 1 ? singular : pluralForm}`;
}

/** The `/demo/{provider}` link prefix in Demo Mode, `''` otherwise. */
export function navPrefixFor(isDemoMode: boolean, cloudProvider: string | null | undefined): string {
  return isDemoMode && cloudProvider ? `/demo/${cloudProvider}` : '';
}
