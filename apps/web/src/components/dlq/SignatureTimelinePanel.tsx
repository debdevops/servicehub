import { Clock, AlertCircle, CheckCircle, XCircle, ArrowRight, FileText, Sparkles } from 'lucide-react';
import type { DlqTimelineEvent } from '@servicehub/ui-shared/lib/api/dlqHistory';

interface SignatureTimelinePanelProps {
  events: DlqTimelineEvent[];
}

const eventIcons: Record<string, { icon: typeof Clock; color: string }> = {
  SignatureFirstObserved: { icon: FileText, color: 'text-amber-500' },
  SignatureRecurred: { icon: ArrowRight, color: 'text-blue-500' },
  KnowledgeRecorded: { icon: Sparkles, color: 'text-primary-500' },
  StatusChanged: { icon: ArrowRight, color: 'text-purple-500' },
  ReplayedSuccess: { icon: CheckCircle, color: 'text-green-500' },
  ReplayedFailed: { icon: XCircle, color: 'text-red-500' },
  DeadLettered: { icon: AlertCircle, color: 'text-red-500' },
};

function formatTimestamp(ts: string): string {
  const date = new Date(ts);
  return date.toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });
}

function formatRelativeTime(ts: string): string {
  const now = Date.now();
  const then = new Date(ts).getTime();
  const diff = now - then;
  const minutes = Math.floor(diff / 60000);
  if (minutes < 1) return 'just now';
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}

export function SignatureTimelinePanel({ events }: SignatureTimelinePanelProps) {
  if (events.length === 0) {
    return <p className="text-sm text-gray-500">No timeline events available.</p>;
  }

  return (
    <div className="relative">
      <div className="absolute left-4 top-2 bottom-2 w-0.5 bg-gray-200" />

      <div className="space-y-4">
        {events.map((event, idx) => {
          const config = eventIcons[event.eventType] || eventIcons.SignatureFirstObserved;
          const Icon = config.icon;

          return (
            <div key={idx} className="relative flex gap-3 pl-1">
              <div className="relative z-10 w-7 h-7 rounded-full bg-white border-2 border-gray-200 flex items-center justify-center shrink-0">
                <Icon className={`w-3.5 h-3.5 ${config.color}`} />
              </div>
              <div className="flex-1 min-w-0 pb-1">
                <div className="flex items-center justify-between gap-2">
                  <span className="font-medium text-sm text-gray-900">{event.eventType}</span>
                  <span className="text-xs text-gray-400 shrink-0" title={formatTimestamp(event.timestamp)}>
                    {formatRelativeTime(event.timestamp)}
                  </span>
                </div>
                <p className="text-sm text-gray-600 mt-0.5">{event.description}</p>
                {event.details && Object.keys(event.details).length > 0 && (
                  <div className="mt-1.5 flex flex-wrap gap-1.5">
                    {Object.entries(event.details).map(([key, value]) => (
                      <span
                        key={key}
                        className="inline-flex items-center gap-1 px-2 py-0.5 bg-gray-100 rounded text-xs text-gray-600"
                      >
                        <span className="font-medium">{key}:</span> {value}
                      </span>
                    ))}
                  </div>
                )}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
