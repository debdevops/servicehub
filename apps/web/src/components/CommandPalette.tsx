import { useState, useEffect, useRef, useCallback, useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  Search, LayoutDashboard, MessageSquare, Clock, GitMerge,
  AlertCircle, RefreshCw, BarChart2, HelpCircle, Plug,
  Database, ChevronRight, X
} from 'lucide-react';
import { useNamespaces } from '@/hooks/useNamespaces';

// ── Types ─────────────────────────────────────────────────────────────────────

interface PaletteItem {
  id: string;
  label: string;
  description?: string;
  group: 'Pages' | 'Namespaces' | 'Actions';
  icon: React.ReactNode;
  action: () => void;
  keywords?: string;
}

// ── Static Page Items ─────────────────────────────────────────────────────────

const PAGE_ITEMS: Omit<PaletteItem, 'action'>[] = [
  {
    id: 'page-dashboard',
    label: 'Dashboard',
    description: 'Multi-namespace overview',
    group: 'Pages',
    icon: <LayoutDashboard className="w-4 h-4" />,
    keywords: 'home overview',
  },
  {
    id: 'page-messages',
    label: 'Messages',
    description: 'Browse and send messages',
    group: 'Pages',
    icon: <MessageSquare className="w-4 h-4" />,
    keywords: 'queue browse send',
  },
  {
    id: 'page-scheduled',
    label: 'Scheduled Messages',
    description: 'View and cancel scheduled deliveries',
    group: 'Pages',
    icon: <Clock className="w-4 h-4" />,
    keywords: 'future timed deliver',
  },
  {
    id: 'page-correlation',
    label: 'Correlation Explorer',
    description: 'Trace messages by correlation ID',
    group: 'Pages',
    icon: <GitMerge className="w-4 h-4" />,
    keywords: 'trace journey timeline correlation',
  },
  {
    id: 'page-dlq',
    label: 'DLQ History',
    description: 'Dead-letter queue audit trail',
    group: 'Pages',
    icon: <AlertCircle className="w-4 h-4" />,
    keywords: 'dead letter poisoned failed',
  },
  {
    id: 'page-rules',
    label: 'Auto-Replay Rules',
    description: 'Manage auto-replay configuration',
    group: 'Pages',
    icon: <RefreshCw className="w-4 h-4" />,
    keywords: 'replay retry automation',
  },
  {
    id: 'page-health',
    label: 'Health',
    description: 'API and service health status',
    group: 'Pages',
    icon: <BarChart2 className="w-4 h-4" />,
    keywords: 'status ping uptime',
  },
  {
    id: 'page-connect',
    label: 'Connect',
    description: 'Add or manage Service Bus namespaces',
    group: 'Pages',
    icon: <Plug className="w-4 h-4" />,
    keywords: 'namespace add connection string',
  },
  {
    id: 'page-help',
    label: 'Help',
    description: 'Quick reference and shortcuts',
    group: 'Pages',
    icon: <HelpCircle className="w-4 h-4" />,
    keywords: 'docs guide keyboard',
  },
];

const PAGE_ROUTES: Record<string, string> = {
  'page-dashboard': '/dashboard',
  'page-messages': '/messages',
  'page-scheduled': '/scheduled',
  'page-correlation': '/correlation',
  'page-dlq': '/dlq-history',
  'page-rules': '/rules',
  'page-health': '/health',
  'page-connect': '/connect',
  'page-help': '/help',
};

// ── Fuzzy Match ───────────────────────────────────────────────────────────────

function fuzzyMatch(query: string, text: string): boolean {
  if (!query) return true;
  const q = query.toLowerCase();
  const t = text.toLowerCase();
  let qi = 0;
  for (let ti = 0; ti < t.length && qi < q.length; ti++) {
    if (t[ti] === q[qi]) qi++;
  }
  return qi === q.length;
}

function scoreMatch(query: string, item: PaletteItem): number {
  if (!query) return 0;
  const q = query.toLowerCase();
  const label = item.label.toLowerCase();
  const desc = (item.description || '').toLowerCase();
  const kw = (item.keywords || '').toLowerCase();
  if (label.startsWith(q)) return 100;
  if (label.includes(q)) return 80;
  if (desc.includes(q) || kw.includes(q)) return 50;
  if (fuzzyMatch(q, label)) return 30;
  if (fuzzyMatch(q, desc + ' ' + kw)) return 10;
  return 0;
}

// ── Highlight matching chars ──────────────────────────────────────────────────

function HighlightMatch({ text, query }: { text: string; query: string }) {
  if (!query) return <>{text}</>;
  const q = query.toLowerCase();
  const t = text;
  const result: React.ReactNode[] = [];
  let qi = 0;
  for (let ti = 0; ti < t.length; ti++) {
    if (qi < q.length && t[ti].toLowerCase() === q[qi]) {
      result.push(<mark key={ti} className="bg-primary-200 text-primary-900 rounded-[2px]">{t[ti]}</mark>);
      qi++;
    } else {
      result.push(t[ti]);
    }
  }
  return <>{result}</>;
}

// ── Group label ───────────────────────────────────────────────────────────────

const GROUP_COLORS: Record<string, string> = {
  Pages: 'text-gray-500',
  Namespaces: 'text-blue-600',
  Actions: 'text-purple-600',
};

// ── Main Component ────────────────────────────────────────────────────────────

interface CommandPaletteProps {
  open: boolean;
  onClose: () => void;
}

export function CommandPalette({ open, onClose }: CommandPaletteProps) {
  const navigate = useNavigate();
  const { data: namespaces = [] } = useNamespaces();
  const [query, setQuery] = useState('');
  const [activeIdx, setActiveIdx] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLUListElement>(null);

  // Reset on open
  useEffect(() => {
    if (open) {
      setQuery('');
      setActiveIdx(0);
      setTimeout(() => inputRef.current?.focus(), 30);
    }
  }, [open]);

  // Build full item list
  const allItems = useMemo<PaletteItem[]>(() => {
    const pageItems: PaletteItem[] = PAGE_ITEMS.map(p => ({
      ...p,
      action: () => { navigate(PAGE_ROUTES[p.id]); onClose(); },
    }));

    const nsItems: PaletteItem[] = namespaces.map(ns => ({
      id: `ns-${ns.id}`,
      label: ns.displayName || ns.name,
      description: ns.environment ? `${ns.environment} namespace` : 'Namespace',
      group: 'Namespaces' as const,
      icon: <Database className="w-4 h-4 text-blue-500" />,
      keywords: ns.name + ' ' + (ns.environment || ''),
      action: () => {
        navigate(`/?namespace=${ns.id}`);
        onClose();
      },
    }));

    const actionItems: PaletteItem[] = namespaces.flatMap(ns => [
      {
        id: `action-dlq-${ns.id}`,
        label: `Browse DLQ — ${ns.displayName || ns.name}`,
        description: 'View dead-letter messages',
        group: 'Actions' as const,
        icon: <AlertCircle className="w-4 h-4 text-red-500" />,
        keywords: 'dead letter queue',
        action: () => {
          navigate(`/app/messages?namespace=${ns.id}&tab=dlq`);
          onClose();
        },
      },
      {
        id: `action-scheduled-${ns.id}`,
        label: `Scheduled — ${ns.displayName || ns.name}`,
        description: 'View scheduled messages',
        group: 'Actions' as const,
        icon: <Clock className="w-4 h-4 text-purple-500" />,
        keywords: 'future timed',
        action: () => {
          navigate(`/app/scheduled?namespace=${ns.id}`);
          onClose();
        },
      },
    ]);

    return [...pageItems, ...nsItems, ...actionItems];
  }, [namespaces, navigate, onClose]);

  // Filter + score
  const filtered = useMemo(() => {
    if (!query) return allItems;
    return allItems
      .map(item => ({ item, score: scoreMatch(query, item) }))
      .filter(({ score }) => score > 0)
      .sort((a, b) => b.score - a.score)
      .map(({ item }) => item);
  }, [allItems, query]);

  // Group the filtered items
  const grouped = useMemo(() => {
    const groups: { label: string; items: PaletteItem[] }[] = [];
    const seen = new Set<string>();
    for (const item of filtered) {
      if (!seen.has(item.group)) {
        seen.add(item.group);
        groups.push({ label: item.group, items: [] });
      }
      groups[groups.length - 1].items.push(item);
    }
    return groups;
  }, [filtered]);

  // Flat index → item
  const flatItems = useMemo(() => filtered, [filtered]);

  // Clamp activeIdx when list changes
  useEffect(() => {
    setActiveIdx(prev => Math.max(0, Math.min(prev, flatItems.length - 1)));
  }, [flatItems.length]);

  // Scroll active item into view
  useEffect(() => {
    if (!listRef.current) return;
    const el = listRef.current.querySelector('[data-active="true"]') as HTMLElement | null;
    el?.scrollIntoView({ block: 'nearest' });
  }, [activeIdx]);

  const handleKeyDown = useCallback((e: React.KeyboardEvent) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setActiveIdx(i => Math.min(i + 1, flatItems.length - 1));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setActiveIdx(i => Math.max(i - 1, 0));
    } else if (e.key === 'Enter') {
      e.preventDefault();
      flatItems[activeIdx]?.action();
    } else if (e.key === 'Escape') {
      onClose();
    }
  }, [flatItems, activeIdx, onClose]);

  if (!open) return null;

  let flatIdx = 0;

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center pt-[15vh]"
      role="dialog"
      aria-modal="true"
      aria-label="Command palette"
    >
      {/* Backdrop */}
      <div
        className="absolute inset-0 bg-black/40 backdrop-blur-sm"
        onClick={onClose}
      />

      {/* Panel */}
      <div className="relative w-full max-w-xl mx-4 bg-white rounded-2xl shadow-2xl overflow-hidden border border-gray-200 flex flex-col max-h-[60vh]">
        {/* Search bar */}
        <div className="flex items-center gap-3 px-4 py-3 border-b border-gray-100">
          <Search className="w-4 h-4 text-gray-400 shrink-0" />
          <input
            ref={inputRef}
            type="text"
            value={query}
            onChange={e => { setQuery(e.target.value); setActiveIdx(0); }}
            onKeyDown={handleKeyDown}
            placeholder="Search pages, namespaces, actions…"
            className="flex-1 bg-transparent text-sm text-gray-900 placeholder-gray-400 outline-none"
            autoComplete="off"
            spellCheck={false}
          />
          <span className="text-xs text-gray-400 shrink-0 hidden sm:block">ESC to close</span>
          <button
            onClick={onClose}
            className="p-1 hover:bg-gray-100 rounded-lg transition-colors sm:hidden"
            aria-label="Close"
          >
            <X className="w-3.5 h-3.5 text-gray-400" />
          </button>
        </div>

        {/* Results */}
        <ul
          ref={listRef}
          className="overflow-y-auto flex-1 py-2"
          role="listbox"
        >
          {grouped.length === 0 && (
            <li className="px-4 py-8 text-center text-sm text-gray-400">
              No results for &ldquo;{query}&rdquo;
            </li>
          )}
          {grouped.map(group => (
            <li key={group.label}>
              {/* Group header */}
              <div className={`px-4 py-1.5 text-[10px] font-semibold uppercase tracking-widest ${GROUP_COLORS[group.label]}`}>
                {group.label}
              </div>
              <ul>
                {group.items.map(item => {
                  const idx = flatIdx++;
                  const isActive = idx === activeIdx;
                  return (
                    <li
                      key={item.id}
                      data-active={isActive}
                      role="option"
                      aria-selected={isActive}
                      className={`flex items-center gap-3 px-4 py-2.5 cursor-pointer transition-colors ${
                        isActive ? 'bg-primary-50' : 'hover:bg-gray-50'
                      }`}
                      onClick={item.action}
                      onMouseEnter={() => setActiveIdx(idx)}
                    >
                      <span className={`shrink-0 ${isActive ? 'text-primary-600' : 'text-gray-400'}`}>
                        {item.icon}
                      </span>
                      <span className="flex-1 min-w-0">
                        <span className={`block text-sm font-medium truncate ${isActive ? 'text-primary-700' : 'text-gray-800'}`}>
                          <HighlightMatch text={item.label} query={query} />
                        </span>
                        {item.description && (
                          <span className="block text-xs text-gray-400 truncate">{item.description}</span>
                        )}
                      </span>
                      {isActive && <ChevronRight className="w-3.5 h-3.5 text-primary-400 shrink-0" />}
                    </li>
                  );
                })}
              </ul>
            </li>
          ))}
        </ul>

        {/* Footer hint */}
        <div className="border-t border-gray-100 px-4 py-2 flex items-center gap-4 text-[11px] text-gray-400">
          <span><kbd className="font-mono bg-gray-100 px-1 rounded">↑↓</kbd> navigate</span>
          <span><kbd className="font-mono bg-gray-100 px-1 rounded">↵</kbd> select</span>
          <span><kbd className="font-mono bg-gray-100 px-1 rounded">Esc</kbd> close</span>
        </div>
      </div>
    </div>
  );
}
