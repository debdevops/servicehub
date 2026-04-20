import { useState, useCallback } from 'react';
import { Copy, Check } from 'lucide-react';
import { copyToClipboard } from '@/lib/clipboard';

interface CopyButtonProps {
  text: string;
  label?: string;
  /** Extra Tailwind classes for the button wrapper */
  className?: string;
  /** Icon size class, e.g. 'w-3 h-3'. Defaults to 'w-3.5 h-3.5' */
  iconSize?: string;
}

/**
 * Small inline button that copies `text` to the clipboard.
 * Shows a 2-second success state (✓) after a successful copy.
 */
export function CopyButton({ text, label, className = '', iconSize = 'w-3.5 h-3.5' }: CopyButtonProps) {
  const [copied, setCopied] = useState(false);

  const handleCopy = useCallback(async (e: React.MouseEvent) => {
    e.stopPropagation();
    const ok = await copyToClipboard(text);
    if (ok) {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    }
  }, [text]);

  return (
    <button
      type="button"
      onClick={handleCopy}
      title={label ? `Copy ${label}` : 'Copy to clipboard'}
      aria-label={label ? `Copy ${label}` : 'Copy to clipboard'}
      className={`inline-flex items-center gap-1 p-1 rounded transition-colors ${
        copied
          ? 'text-green-600 bg-green-50'
          : 'text-gray-400 hover:text-gray-600 hover:bg-gray-100'
      } ${className}`}
    >
      {copied ? (
        <Check className={iconSize} />
      ) : (
        <Copy className={iconSize} />
      )}
      {label && (
        <span className="text-xs">{copied ? 'Copied!' : label}</span>
      )}
    </button>
  );
}
