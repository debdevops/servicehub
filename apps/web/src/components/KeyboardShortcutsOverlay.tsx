import { useEffect } from 'react';
import { X, Keyboard } from 'lucide-react';

interface ShortcutRow {
  keys: string[];
  label: string;
}

const SHORTCUTS: { group: string; items: ShortcutRow[] }[] = [
  {
    group: 'Navigation',
    items: [
      { keys: ['⌘', 'K'], label: 'Open command palette' },
      { keys: ['?'], label: 'Show this shortcuts list' },
    ],
  },
  {
    group: 'Message List',
    items: [
      { keys: ['J'], label: 'Next message' },
      { keys: ['K'], label: 'Previous message' },
      { keys: ['Enter'], label: 'Open message detail' },
      { keys: ['Esc'], label: 'Close detail panel' },
    ],
  },
  {
    group: 'Global',
    items: [
      { keys: ['Esc'], label: 'Close any open dialog or panel' },
    ],
  },
];

interface KeyboardShortcutsOverlayProps {
  open: boolean;
  onClose: () => void;
}

export function KeyboardShortcutsOverlay({ open, onClose }: KeyboardShortcutsOverlayProps) {
  useEffect(() => {
    if (!open) return;
    const handler = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [open, onClose]);

  if (!open) return null;

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center"
      role="dialog"
      aria-modal="true"
      aria-label="Keyboard shortcuts"
    >
      {/* Backdrop */}
      <div className="absolute inset-0 bg-black/30 backdrop-blur-[2px]" onClick={onClose} />

      {/* Panel */}
      <div className="relative bg-white rounded-2xl shadow-2xl w-full max-w-sm mx-4 overflow-hidden border border-gray-200">
        {/* Header */}
        <div className="flex items-center justify-between px-5 py-4 border-b border-gray-100">
          <div className="flex items-center gap-2 text-gray-800 font-semibold text-sm">
            <Keyboard className="w-4 h-4 text-gray-500" />
            Keyboard Shortcuts
          </div>
          <button
            onClick={onClose}
            className="p-1.5 hover:bg-gray-100 rounded-lg transition-colors"
            aria-label="Close"
          >
            <X className="w-3.5 h-3.5 text-gray-500" />
          </button>
        </div>

        {/* Shortcut groups */}
        <div className="px-5 py-4 space-y-4">
          {SHORTCUTS.map(group => (
            <div key={group.group}>
              <p className="text-[10px] font-semibold uppercase tracking-widest text-gray-400 mb-2">
                {group.group}
              </p>
              <ul className="space-y-1.5">
                {group.items.map(item => (
                  <li key={item.label} className="flex items-center justify-between text-sm">
                    <span className="text-gray-600">{item.label}</span>
                    <span className="flex items-center gap-1">
                      {item.keys.map(k => (
                        <kbd
                          key={k}
                          className="px-1.5 py-0.5 text-xs font-mono bg-gray-100 border border-gray-200 rounded text-gray-700 leading-none"
                        >
                          {k}
                        </kbd>
                      ))}
                    </span>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>

        <div className="px-5 py-3 border-t border-gray-100 text-center text-xs text-gray-400">
          Press <kbd className="font-mono bg-gray-100 px-1 rounded">?</kbd> anywhere to toggle
        </div>
      </div>
    </div>
  );
}
