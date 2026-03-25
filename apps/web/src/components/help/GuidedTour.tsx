import { useState, useEffect, useCallback, useRef } from 'react';
import { X, ChevronLeft, ChevronRight } from 'lucide-react';
import { tourSteps, type TourStep } from '@/lib/helpContent';

const TOUR_COMPLETED_KEY = 'servicehub_tour_completed';

export function isTourCompleted(): boolean {
  return localStorage.getItem(TOUR_COMPLETED_KEY) === 'true';
}

export function resetTour(): void {
  localStorage.removeItem(TOUR_COMPLETED_KEY);
}

interface GuidedTourProps {
  /** Whether the tour is currently active */
  isActive: boolean;
  /** Called when the tour ends (completed or dismissed) */
  onComplete: () => void;
}

export function GuidedTour({ isActive, onComplete }: GuidedTourProps) {
  const [currentStep, setCurrentStep] = useState(0);
  const [targetRect, setTargetRect] = useState<DOMRect | null>(null);
  const popoverRef = useRef<HTMLDivElement>(null);

  const step: TourStep | undefined = tourSteps[currentStep];
  const isLast = currentStep === tourSteps.length - 1;
  const isFirst = currentStep === 0;

  // Locate the target element and track its position
  const updateTargetRect = useCallback(() => {
    if (!step) return;
    const el = document.querySelector(step.target);
    if (el) {
      setTargetRect(el.getBoundingClientRect());
    } else {
      setTargetRect(null);
    }
  }, [step]);

  useEffect(() => {
    if (!isActive) return;
    updateTargetRect();
    window.addEventListener('resize', updateTargetRect);
    window.addEventListener('scroll', updateTargetRect, true);
    return () => {
      window.removeEventListener('resize', updateTargetRect);
      window.removeEventListener('scroll', updateTargetRect, true);
    };
  }, [isActive, updateTargetRect]);

  // Close on Escape
  useEffect(() => {
    if (!isActive) return;
    function handleKey(e: KeyboardEvent) {
      if (e.key === 'Escape') finish();
    }
    document.addEventListener('keydown', handleKey);
    return () => document.removeEventListener('keydown', handleKey);
  });

  const finish = useCallback(() => {
    localStorage.setItem(TOUR_COMPLETED_KEY, 'true');
    setCurrentStep(0);
    onComplete();
  }, [onComplete]);

  const next = () => {
    if (isLast) {
      finish();
    } else {
      setCurrentStep((s) => s + 1);
    }
  };

  const prev = () => {
    if (!isFirst) setCurrentStep((s) => s - 1);
  };

  if (!isActive || !step) return null;

  // Default to center of screen if target not found
  const padding = 8;
  const spotlight = targetRect
    ? {
        top: targetRect.top - padding,
        left: targetRect.left - padding,
        width: targetRect.width + padding * 2,
        height: targetRect.height + padding * 2,
      }
    : null;

  // Calculate popover position
  const popoverStyle = getPopoverPosition(step.placement, targetRect, spotlight);

  return (
    <div className="fixed inset-0 z-[9999]" aria-modal="true" role="dialog">
      {/* Overlay with spotlight cutout */}
      <svg className="absolute inset-0 w-full h-full" style={{ pointerEvents: 'none' }}>
        <defs>
          <mask id="tour-mask">
            <rect width="100%" height="100%" fill="white" />
            {spotlight && (
              <rect
                x={spotlight.left}
                y={spotlight.top}
                width={spotlight.width}
                height={spotlight.height}
                rx={12}
                fill="black"
              />
            )}
          </mask>
        </defs>
        <rect
          width="100%"
          height="100%"
          fill="rgba(0,0,0,0.55)"
          mask="url(#tour-mask)"
          style={{ pointerEvents: 'auto' }}
          onClick={finish}
        />
      </svg>

      {/* Spotlight ring */}
      {spotlight && (
        <div
          className="absolute rounded-xl ring-2 ring-primary-400 ring-offset-2 pointer-events-none transition-all duration-300"
          style={{
            top: spotlight.top,
            left: spotlight.left,
            width: spotlight.width,
            height: spotlight.height,
          }}
        />
      )}

      {/* Popover card */}
      <div
        ref={popoverRef}
        className="absolute z-[10000] w-80 bg-white rounded-xl shadow-2xl border border-gray-200 transition-all duration-300"
        style={popoverStyle}
      >
        {/* Header */}
        <div className="flex items-center justify-between px-4 pt-4">
          <span className="text-xs font-medium text-primary-600 bg-primary-50 px-2 py-0.5 rounded-full">
            {currentStep + 1} / {tourSteps.length}
          </span>
          <button
            onClick={finish}
            className="text-gray-400 hover:text-gray-600 transition-colors"
            aria-label="Close tour"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Content */}
        <div className="px-4 py-3">
          <h3 className="text-sm font-semibold text-gray-900">{step.title}</h3>
          <p className="mt-1.5 text-xs text-gray-600 leading-relaxed">{step.content}</p>
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between px-4 pb-4">
          <button
            onClick={finish}
            className="text-xs text-gray-500 hover:text-gray-700 transition-colors"
          >
            Skip tour
          </button>
          <div className="flex items-center gap-2">
            {!isFirst && (
              <button
                onClick={prev}
                className="flex items-center gap-1 text-xs text-gray-600 hover:text-gray-800 px-2 py-1.5 rounded-lg hover:bg-gray-100 transition-colors"
              >
                <ChevronLeft className="w-3 h-3" />
                Back
              </button>
            )}
            <button
              onClick={next}
              className="flex items-center gap-1 text-xs font-medium text-white bg-primary-500 hover:bg-primary-600 px-3 py-1.5 rounded-lg transition-colors"
            >
              {isLast ? 'Finish' : 'Next'}
              {!isLast && <ChevronRight className="w-3 h-3" />}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

function getPopoverPosition(
  placement: TourStep['placement'],
  targetRect: DOMRect | null,
  spotlight: { top: number; left: number; width: number; height: number } | null,
): React.CSSProperties {
  if (!targetRect || !spotlight) {
    // Center on screen
    return { top: '50%', left: '50%', transform: 'translate(-50%, -50%)' };
  }

  const gap = 16;

  switch (placement) {
    case 'right':
      return {
        top: spotlight.top,
        left: spotlight.left + spotlight.width + gap,
      };
    case 'left':
      return {
        top: spotlight.top,
        right: window.innerWidth - spotlight.left + gap,
      };
    case 'bottom':
      return {
        top: spotlight.top + spotlight.height + gap,
        left: Math.max(16, spotlight.left + spotlight.width / 2 - 160),
      };
    case 'top':
      return {
        bottom: window.innerHeight - spotlight.top + gap,
        left: Math.max(16, spotlight.left + spotlight.width / 2 - 160),
      };
    default:
      return {
        top: spotlight.top + spotlight.height + gap,
        left: spotlight.left,
      };
  }
}
