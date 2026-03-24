import { useState, useEffect, useCallback, useRef } from 'react';
import { X, Key, Eye, EyeOff, CheckCircle, Trash2, ShieldCheck } from 'lucide-react';

const STORAGE_KEY = 'servicehub:api-key';

interface SettingsDialogProps {
  isOpen: boolean;
  onClose: () => void;
}

export function SettingsDialog({ isOpen, onClose }: SettingsDialogProps) {
  const [apiKey, setApiKey] = useState('');
  const [showKey, setShowKey] = useState(false);
  const [saved, setSaved] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  // Load current key on open
  useEffect(() => {
    if (isOpen) {
      const stored = sessionStorage.getItem(STORAGE_KEY) ?? '';
      setApiKey(stored);
      setSaved(false);
      setShowKey(false);
      setTimeout(() => inputRef.current?.focus(), 50);
    }
  }, [isOpen]);

  // Close on Escape
  useEffect(() => {
    if (!isOpen) return;
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', handleKey);
    return () => window.removeEventListener('keydown', handleKey);
  }, [isOpen, onClose]);

  const handleSave = useCallback(() => {
    const trimmed = apiKey.trim();
    if (trimmed) {
      sessionStorage.setItem(STORAGE_KEY, trimmed);
    } else {
      sessionStorage.removeItem(STORAGE_KEY);
    }
    setSaved(true);
    setTimeout(onClose, 900);
  }, [apiKey, onClose]);

  const handleClear = useCallback(() => {
    setApiKey('');
    sessionStorage.removeItem(STORAGE_KEY);
    setSaved(false);
  }, []);

  const hasStoredKey = !!sessionStorage.getItem(STORAGE_KEY);

  if (!isOpen) return null;

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center"
      role="dialog"
      aria-modal="true"
      aria-labelledby="settings-dialog-title"
    >
      {/* Backdrop */}
      <div
        className="absolute inset-0 bg-black/50 backdrop-blur-sm"
        onClick={onClose}
        aria-hidden="true"
      />

      {/* Panel */}
      <div className="relative z-10 w-full max-w-md mx-4 bg-white dark:bg-gray-900 rounded-2xl shadow-2xl border border-gray-200 dark:border-gray-700">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-100 dark:border-gray-800">
          <div className="flex items-center gap-2">
            <ShieldCheck className="w-5 h-5 text-primary-500" />
            <h2 id="settings-dialog-title" className="font-semibold text-gray-900 dark:text-gray-100 text-base">
              Security Settings
            </h2>
          </div>
          <button
            onClick={onClose}
            className="p-1.5 rounded-lg hover:bg-gray-100 dark:hover:bg-gray-800 transition-colors"
            aria-label="Close settings"
          >
            <X className="w-4 h-4 text-gray-500" />
          </button>
        </div>

        {/* Body */}
        <div className="px-6 py-5 space-y-5">
          {/* API Key section */}
          <div>
            <label
              htmlFor="api-key-input"
              className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5"
            >
              <span className="flex items-center gap-1.5">
                <Key className="w-4 h-4" />
                API Key
              </span>
            </label>
            <p className="text-xs text-gray-500 dark:text-gray-400 mb-2">
              Your API key is stored only in this browser tab (
              <code className="bg-gray-100 dark:bg-gray-800 px-1 rounded">sessionStorage</code>
              ) and sent as the{' '}
              <code className="bg-gray-100 dark:bg-gray-800 px-1 rounded">X-API-Key</code>{' '}
              header on every request. It is cleared automatically when you close the tab.
            </p>
            <div className="relative">
              <input
                id="api-key-input"
                ref={inputRef}
                type={showKey ? 'text' : 'password'}
                value={apiKey}
                onChange={(e) => { setApiKey(e.target.value); setSaved(false); }}
                onKeyDown={(e) => e.key === 'Enter' && handleSave()}
                placeholder="Paste your API key here…"
                autoComplete="off"
                spellCheck={false}
                className="w-full pr-10 pl-3 py-2.5 text-sm border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100 placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-primary-400 focus:border-transparent transition-shadow"
              />
              <button
                type="button"
                onClick={() => setShowKey((v) => !v)}
                className="absolute right-2.5 top-1/2 -translate-y-1/2 p-1 text-gray-400 hover:text-gray-600 dark:hover:text-gray-300"
                aria-label={showKey ? 'Hide API key' : 'Show API key'}
              >
                {showKey ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
              </button>
            </div>

            {/* Status indicator */}
            {hasStoredKey && !saved && (
              <p className="mt-1.5 text-xs text-green-600 dark:text-green-400 flex items-center gap-1">
                <CheckCircle className="w-3.5 h-3.5" />
                API key is active for this session
              </p>
            )}
          </div>

          {/* Info box */}
          <div className="bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800 rounded-lg p-3 text-xs text-amber-800 dark:text-amber-300 space-y-1">
            <p className="font-medium">Where do I get an API key?</p>
            <p>
              Ask your system administrator for a{' '}
              <code className="bg-amber-100 dark:bg-amber-900/40 px-1 rounded">ScopedApiKey</code>{' '}
              configured on the ServiceHub backend. Keys are provisioned via the{' '}
              <code className="bg-amber-100 dark:bg-amber-900/40 px-1 rounded">
                Security:Authentication:ScopedApiKeys
              </code>{' '}
              environment variable.
            </p>
          </div>
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between px-6 py-4 border-t border-gray-100 dark:border-gray-800">
          <button
            onClick={handleClear}
            disabled={!apiKey}
            className="flex items-center gap-1.5 text-sm text-red-500 hover:text-red-700 disabled:opacity-30 disabled:cursor-not-allowed transition-colors"
          >
            <Trash2 className="w-4 h-4" />
            Clear key
          </button>

          <div className="flex items-center gap-2">
            <button
              onClick={onClose}
              className="px-4 py-2 text-sm text-gray-600 dark:text-gray-400 hover:text-gray-800 dark:hover:text-gray-200 transition-colors"
            >
              Cancel
            </button>
            <button
              onClick={handleSave}
              disabled={saved}
              className="flex items-center gap-1.5 px-4 py-2 text-sm font-medium bg-primary-500 hover:bg-primary-600 text-white rounded-lg transition-colors disabled:opacity-60"
            >
              {saved ? (
                <>
                  <CheckCircle className="w-4 h-4" />
                  Saved!
                </>
              ) : (
                'Save'
              )}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
