import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { ChevronsLeft, ChevronsRight } from 'lucide-react';

interface ResizablePanelProps {
  /** Unique key used to persist width/collapsed state across reloads. */
  panelId: string;
  title: string;
  icon?: ReactNode;
  /** Extra controls rendered in the header, before the collapse button (e.g. refresh, add). */
  headerActions?: ReactNode;
  defaultWidth?: number;
  minWidth?: number;
  maxWidth?: number;
  collapsedByDefault?: boolean;
  children: ReactNode;
  dataTour?: string;
}

const STORAGE_PREFIX = 'servicehub_panel_';

function readStoredWidth(panelId: string, fallback: number): number {
  const raw = localStorage.getItem(`${STORAGE_PREFIX}${panelId}_width`);
  const parsed = raw ? Number(raw) : NaN;
  return Number.isFinite(parsed) ? parsed : fallback;
}

function readStoredCollapsed(panelId: string, fallback: boolean): boolean {
  const raw = localStorage.getItem(`${STORAGE_PREFIX}${panelId}_collapsed`);
  return raw === null ? fallback : raw === 'true';
}

/**
 * A draggable, resizable, collapsible side panel — the building block for the Quick Access
 * and Namespaces/Connections columns. Width and collapsed state persist per-panel in
 * localStorage so the layout survives reloads, matching the enterprise-dashboard reference.
 */
export function ResizablePanel({
  panelId,
  title,
  icon,
  headerActions,
  defaultWidth = 280,
  minWidth = 220,
  maxWidth = 480,
  collapsedByDefault = false,
  children,
  dataTour,
}: ResizablePanelProps) {
  const [width, setWidth] = useState(() => readStoredWidth(panelId, defaultWidth));
  const [collapsed, setCollapsed] = useState(() => readStoredCollapsed(panelId, collapsedByDefault));
  const [isDragging, setIsDragging] = useState(false);
  const dragState = useRef<{ startX: number; startWidth: number } | null>(null);

  useEffect(() => {
    localStorage.setItem(`${STORAGE_PREFIX}${panelId}_width`, String(width));
  }, [panelId, width]);

  useEffect(() => {
    localStorage.setItem(`${STORAGE_PREFIX}${panelId}_collapsed`, String(collapsed));
  }, [panelId, collapsed]);

  const handleDragMove = useCallback(
    (e: MouseEvent) => {
      if (!dragState.current) return;
      const delta = e.clientX - dragState.current.startX;
      const next = Math.min(maxWidth, Math.max(minWidth, dragState.current.startWidth + delta));
      setWidth(next);
    },
    [minWidth, maxWidth],
  );

  const handleDragEnd = useCallback(() => {
    dragState.current = null;
    setIsDragging(false);
    window.removeEventListener('mousemove', handleDragMove);
    window.removeEventListener('mouseup', handleDragEnd);
  }, [handleDragMove]);

  const handleDragStart = useCallback(
    (e: React.MouseEvent) => {
      e.preventDefault();
      dragState.current = { startX: e.clientX, startWidth: width };
      setIsDragging(true);
      window.addEventListener('mousemove', handleDragMove);
      window.addEventListener('mouseup', handleDragEnd);
    },
    [width, handleDragMove, handleDragEnd],
  );

  useEffect(() => {
    return () => {
      window.removeEventListener('mousemove', handleDragMove);
      window.removeEventListener('mouseup', handleDragEnd);
    };
  }, [handleDragMove, handleDragEnd]);

  if (collapsed) {
    return (
      <div
        className="w-10 shrink-0 bg-white border-r border-gray-200 flex flex-col items-center py-3 gap-3"
        data-tour={dataTour}
      >
        <button
          onClick={() => setCollapsed(false)}
          className="p-1.5 rounded-lg text-gray-400 hover:text-primary-600 hover:bg-primary-50 transition-colors"
          title={`Expand ${title}`}
          aria-label={`Expand ${title}`}
        >
          <ChevronsRight className="w-4 h-4" />
        </button>
        <span className="text-[10px] font-semibold text-gray-400 uppercase tracking-wider [writing-mode:vertical-rl] rotate-180">
          {title}
        </span>
      </div>
    );
  }

  return (
    <div
      className="relative shrink-0 bg-white border-r border-gray-200 flex flex-col overflow-hidden"
      style={{ width }}
      data-tour={dataTour}
    >
      {/* Header */}
      <div className="flex items-center justify-between gap-1.5 px-3 py-2.5 border-b border-gray-100 shrink-0">
        <div className="flex items-center gap-1.5 min-w-0">
          {icon}
          <h2 className="text-xs font-bold text-gray-600 uppercase tracking-wider truncate">{title}</h2>
        </div>
        <div className="flex items-center gap-0.5 shrink-0">
          {headerActions}
          <button
            onClick={() => setCollapsed(true)}
            className="p-1 rounded hover:bg-gray-100 text-gray-400 hover:text-gray-600 transition-colors"
            title={`Collapse ${title}`}
            aria-label={`Collapse ${title}`}
          >
            <ChevronsLeft className="w-3.5 h-3.5" />
          </button>
        </div>
      </div>

      {/* Content */}
      <div className="flex-1 overflow-y-auto min-h-0">{children}</div>

      {/* Resize footer — draggable, matches the reference's "drag to resize" affordance */}
      <div
        onMouseDown={handleDragStart}
        className={`shrink-0 flex items-center justify-center gap-1.5 px-3 py-1.5 border-t border-gray-100 text-[10px] font-medium text-gray-400 cursor-col-resize select-none transition-colors ${
          isDragging ? 'bg-primary-100 text-primary-600' : 'hover:bg-gray-50 hover:text-gray-500'
        }`}
        title="Drag to resize"
      >
        <svg width="12" height="12" viewBox="0 0 12 12" fill="none" className="shrink-0">
          <circle cx="3" cy="3" r="1" fill="currentColor" />
          <circle cx="3" cy="6" r="1" fill="currentColor" />
          <circle cx="3" cy="9" r="1" fill="currentColor" />
          <circle cx="6" cy="3" r="1" fill="currentColor" />
          <circle cx="6" cy="6" r="1" fill="currentColor" />
          <circle cx="6" cy="9" r="1" fill="currentColor" />
        </svg>
        Drag to resize
        <span aria-hidden="true">↔</span>
      </div>

      {/* Invisible edge handle — standard splitter affordance alongside the footer bar */}
      <div
        onMouseDown={handleDragStart}
        className="absolute top-0 right-0 h-full w-1.5 cursor-col-resize z-10 hover:bg-primary-300/50"
      />
    </div>
  );
}
