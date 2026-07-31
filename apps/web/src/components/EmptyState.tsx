import type { LucideIcon } from 'lucide-react';

interface EmptyStateAction {
  label: string;
  onClick: () => void;
  icon?: LucideIcon;
}

interface EmptyStateProps {
  icon: LucideIcon;
  heading: string;
  subtext?: string;
  action?: EmptyStateAction;
  /** Fill the parent's height and center within it (the common "whole pane is empty" case). */
  fillHeight?: boolean;
}

/** Shared "nothing here yet" panel — icon + heading + subtext + optional CTA. */
export function EmptyState({ icon: Icon, heading, subtext, action, fillHeight = true }: EmptyStateProps) {
  return (
    <div
      className={`flex flex-col items-center justify-center text-center px-6 ${fillHeight ? 'h-full' : 'py-16'}`}
    >
      <Icon className="w-14 h-14 text-gray-300 mb-4" />
      <p className="text-gray-600 font-semibold text-lg mb-1">{heading}</p>
      {subtext && <p className="text-gray-400 text-sm mb-5 max-w-sm">{subtext}</p>}
      {action && (
        <button
          onClick={action.onClick}
          className="flex items-center gap-2 px-5 py-2.5 bg-primary-600 hover:bg-primary-700 text-white rounded-lg text-sm font-medium transition-colors focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-1"
        >
          {action.icon && <action.icon className="w-4 h-4" />}
          {action.label}
        </button>
      )}
    </div>
  );
}
