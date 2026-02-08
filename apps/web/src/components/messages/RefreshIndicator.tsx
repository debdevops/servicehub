import { RefreshCw } from 'lucide-react';

interface RefreshIndicatorProps {
  isRefreshing: boolean;
  autoRefreshEnabled: boolean;
  lastUpdated?: number;
}

export function RefreshIndicator({ isRefreshing, autoRefreshEnabled, lastUpdated }: RefreshIndicatorProps) {
  const getLastUpdatedText = () => {
    if (!lastUpdated) return '';
    const seconds = Math.floor((Date.now() - lastUpdated) / 1000);
    if (seconds < 5) return 'just now';
    if (seconds < 60) return `${seconds}s ago`;
    return `${Math.floor(seconds / 60)}m ago`;
  };

  if (!autoRefreshEnabled) return null;

  return (
    <div className="fixed bottom-4 right-4 z-50">
      <div className={`
        flex items-center gap-2 px-3 py-2 rounded-lg shadow-lg border transition-all
        ${isRefreshing 
          ? 'bg-blue-50 border-blue-200 text-blue-700' 
          : 'bg-green-50 border-green-200 text-green-700'}
      `}>
        <RefreshCw className={`w-4 h-4 ${isRefreshing ? 'animate-spin' : ''}`} />
        <span className="text-sm font-medium">
          {isRefreshing ? 'Refreshing...' : 'Live'}
        </span>
        {!isRefreshing && lastUpdated && (
          <span className="text-xs opacity-75">
            • {getLastUpdatedText()}
          </span>
        )}
      </div>
    </div>
  );
}
