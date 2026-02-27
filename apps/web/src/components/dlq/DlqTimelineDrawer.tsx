import { useMemo } from 'react';
import { X, Clock, AlertCircle, CheckCircle, XCircle, ArrowRight, FileText } from 'lucide-react';
import { useDlqTimeline, useDlqMessageDetail } from '@/hooks/useDlqHistory';
import { StatusBadge, CategoryBadge } from './StatusBadge';

interface DlqTimelineDrawerProps {
  messageId: number | null;
  onClose: () => void;
}

const eventIcons: Record<string, { icon: typeof Clock; color: string }> = {
  Enqueued: { icon: ArrowRight, color: 'text-blue-500' },
  DeadLettered: { icon: AlertCircle, color: 'text-red-500' },
  Detected: { icon: FileText, color: 'text-amber-500' },
  ReplayedSuccess: { icon: CheckCircle, color: 'text-green-500' },
  ReplayedFailed: { icon: XCircle, color: 'text-red-500' },
  StatusChanged: { icon: ArrowRight, color: 'text-purple-500' },
  Archived: { icon: FileText, color: 'text-gray-500' },
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

export function DlqTimelineDrawer({ messageId, onClose }: DlqTimelineDrawerProps) {
  const { data: timeline, isLoading: timelineLoading } = useDlqTimeline(messageId);
  const { data: detail, isLoading: detailLoading } = useDlqMessageDetail(messageId);

  const isOpen = messageId !== null;

  const events = useMemo(() => timeline?.events ?? [], [timeline]);

  if (!isOpen) return null;

  return (
    <>
      {/* Backdrop */}
      <div
        className="fixed inset-0 bg-black/30 z-40"
        onClick={onClose}
      />

      {/* Drawer */}
      <div className="fixed right-0 top-0 h-full w-[520px] max-w-[90vw] bg-white shadow-2xl z-50 flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between p-4 border-b border-gray-200 bg-gradient-to-r from-red-50 to-white">
          <div>
            <h2 className="font-semibold text-gray-900">Message Timeline</h2>
            {detail && (
              <p className="text-xs text-gray-500 mt-0.5 truncate max-w-[300px]">
                {detail.messageId}
              </p>
            )}
          </div>
          <button
            onClick={onClose}
            className="p-1.5 hover:bg-gray-100 rounded-lg transition-colors"
            aria-label="Close drawer"
          >
            <X className="w-5 h-5 text-gray-500" />
          </button>
        </div>

        {/* Content */}
        <div className="flex-1 overflow-y-auto">
          {(timelineLoading || detailLoading) ? (
            <div className="flex items-center justify-center h-40">
              <div className="flex items-center gap-2 text-gray-500">
                <div className="w-5 h-5 border-2 border-gray-300 border-t-primary-500 rounded-full animate-spin" />
                Loading...
              </div>
            </div>
          ) : (
            <>
              {/* Summary */}
              {detail && (
                <div className="p-4 border-b border-gray-100 space-y-3">
                  <div className="flex items-center gap-2 flex-wrap">
                    <StatusBadge status={detail.status} size="md" />
                    <CategoryBadge
                      category={detail.failureCategory}
                      confidence={detail.categoryConfidence}
                      size="md"
                    />
                  </div>

                  <div className="grid grid-cols-2 gap-3 text-sm">
                    <div>
                      <span className="text-gray-500">Entity</span>
                      <p className="font-medium text-gray-900 truncate">{detail.entityName}</p>
                    </div>
                    <div>
                      <span className="text-gray-500">Delivery Count</span>
                      <p className="font-medium text-gray-900">{detail.deliveryCount}</p>
                    </div>
                    <div>
                      <span className="text-gray-500">Size</span>
                      <p className="font-medium text-gray-900">{formatBytes(detail.messageSize)}</p>
                    </div>
                    <div>
                      <span className="text-gray-500">Content Type</span>
                      <p className="font-medium text-gray-900 truncate">{detail.contentType || 'N/A'}</p>
                    </div>
                  </div>

                  {detail.deadLetterReason && (
                    <div className="bg-red-50 rounded-lg p-3 text-sm">
                      <span className="font-medium text-red-800">DLQ Reason: </span>
                      <span className="text-red-700">{detail.deadLetterReason}</span>
                      {detail.deadLetterErrorDescription && (
                        <p className="text-red-600 mt-1 text-xs">{detail.deadLetterErrorDescription}</p>
                      )}
                    </div>
                  )}

                  {detail.bodyPreview && (
                    <div className="bg-gray-50 rounded-lg p-3">
                      <span className="text-xs font-semibold text-gray-500 uppercase">Body Preview</span>
                      <pre className="text-xs text-gray-700 mt-1 whitespace-pre-wrap break-all max-h-32 overflow-y-auto">
                        {detail.bodyPreview}
                      </pre>
                    </div>
                  )}

                  {detail.userNotes && (
                    <div className="bg-yellow-50 rounded-lg p-3 border border-yellow-200">
                      <span className="text-xs font-semibold text-yellow-700 uppercase">Notes</span>
                      <p className="text-sm text-yellow-800 mt-1">{detail.userNotes}</p>
                    </div>
                  )}
                </div>
              )}

              {/* Timeline */}
              <div className="p-4">
                <h3 className="text-sm font-semibold text-gray-700 uppercase tracking-wider mb-4">
                  Journey Timeline
                </h3>

                {events.length === 0 ? (
                  <p className="text-sm text-gray-500">No timeline events available.</p>
                ) : (
                  <div className="relative">
                    {/* Vertical line */}
                    <div className="absolute left-4 top-2 bottom-2 w-0.5 bg-gray-200" />

                    <div className="space-y-4">
                      {events.map((event, idx) => {
                        const config = eventIcons[event.eventType] || eventIcons.Detected;
                        const Icon = config.icon;

                        return (
                          <div key={idx} className="relative flex gap-3 pl-1">
                            <div className={`relative z-10 w-7 h-7 rounded-full bg-white border-2 border-gray-200 flex items-center justify-center shrink-0`}>
                              <Icon className={`w-3.5 h-3.5 ${config.color}`} />
                            </div>
                            <div className="flex-1 min-w-0 pb-1">
                              <div className="flex items-center justify-between gap-2">
                                <span className="font-medium text-sm text-gray-900">
                                  {event.eventType}
                                </span>
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
                )}
              </div>
            </>
          )}
        </div>
      </div>
    </>
  );
}

function formatBytes(bytes: number): string {
  if (bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(1))} ${sizes[i]}`;
}
