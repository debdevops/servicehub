import { useState, useRef, useEffect } from 'react';
import { HelpCircle, X } from 'lucide-react';

interface HelpTooltipProps {
  /** Short one-liner shown on hover */
  text: string;
  /** Optional longer explanation shown on click */
  detail?: string;
  /** Optional "what to do next" action hint */
  action?: string;
  /** Position relative to the icon */
  position?: 'top' | 'bottom' | 'left' | 'right';
  /** Icon size in pixels */
  size?: number;
  /** Additional CSS class on the wrapper */
  className?: string;
}

export function HelpTooltip({
  text,
  detail,
  action,
  position = 'top',
  size = 14,
  className = '',
}: HelpTooltipProps) {
  const [isHovered, setIsHovered] = useState(false);
  const [isOpen, setIsOpen] = useState(false);
  const popoverRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLSpanElement>(null);

  // Close popover on outside click
  useEffect(() => {
    if (!isOpen) return;
    function handleClick(e: MouseEvent) {
      if (
        popoverRef.current &&
        !popoverRef.current.contains(e.target as Node) &&
        triggerRef.current &&
        !triggerRef.current.contains(e.target as Node)
      ) {
        setIsOpen(false);
      }
    }
    document.addEventListener('mousedown', handleClick);
    return () => document.removeEventListener('mousedown', handleClick);
  }, [isOpen]);

  // Close on Escape
  useEffect(() => {
    if (!isOpen) return;
    function handleKey(e: KeyboardEvent) {
      if (e.key === 'Escape') setIsOpen(false);
    }
    document.addEventListener('keydown', handleKey);
    return () => document.removeEventListener('keydown', handleKey);
  }, [isOpen]);

  const hasDetail = !!(detail || action);
  const showTooltip = isHovered && !isOpen;

  const positionClasses: Record<string, string> = {
    top: 'bottom-full left-1/2 -translate-x-1/2 mb-2',
    bottom: 'top-full left-1/2 -translate-x-1/2 mt-2',
    left: 'right-full top-1/2 -translate-y-1/2 mr-2',
    right: 'left-full top-1/2 -translate-y-1/2 ml-2',
  };

  return (
    <span className={`relative inline-flex items-center ${className}`}>
      <span
        ref={triggerRef}
        role="button"
        tabIndex={0}
        className="inline-flex items-center justify-center text-gray-400 hover:text-primary-500 transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-primary-500 rounded-full cursor-pointer"
        aria-label="Help"
        aria-expanded={isOpen}
        onMouseEnter={() => setIsHovered(true)}
        onMouseLeave={() => setIsHovered(false)}
        onClick={() => hasDetail ? setIsOpen(!isOpen) : undefined}
        onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); if (hasDetail) setIsOpen(!isOpen); } }}
      >
        <HelpCircle style={{ width: size, height: size }} />
      </span>

      {/* Hover tooltip — short text */}
      {showTooltip && (
        <div
          role="tooltip"
          className={`absolute z-50 px-2.5 py-1.5 text-xs font-medium text-white bg-gray-800 rounded-lg shadow-lg whitespace-nowrap pointer-events-none ${positionClasses[position]}`}
        >
          {text}
          {/* Arrow */}
          {position === 'top' && (
            <div className="absolute top-full left-1/2 -translate-x-1/2 w-0 h-0 border-l-4 border-r-4 border-t-4 border-transparent border-t-gray-800" />
          )}
          {position === 'bottom' && (
            <div className="absolute bottom-full left-1/2 -translate-x-1/2 w-0 h-0 border-l-4 border-r-4 border-b-4 border-transparent border-b-gray-800" />
          )}
        </div>
      )}

      {/* Click popover — detail + action */}
      {isOpen && hasDetail && (
        <div
          ref={popoverRef}
          className={`absolute z-50 w-72 bg-white border border-gray-200 rounded-xl shadow-xl ${positionClasses[position]}`}
        >
          <div className="p-3">
            <div className="flex items-start justify-between gap-2">
              <p className="text-sm font-medium text-gray-900">{text}</p>
              <button
                type="button"
                onClick={() => setIsOpen(false)}
                className="text-gray-400 hover:text-gray-600 -mt-0.5"
                aria-label="Close"
              >
                <X className="w-3.5 h-3.5" />
              </button>
            </div>
            {detail && (
              <p className="mt-1.5 text-xs text-gray-600 leading-relaxed">{detail}</p>
            )}
            {action && (
              <div className="mt-2 flex items-center gap-1.5 text-xs text-primary-600 font-medium">
                <span className="w-1 h-1 bg-primary-500 rounded-full" />
                {action}
              </div>
            )}
          </div>
        </div>
      )}
    </span>
  );
}
