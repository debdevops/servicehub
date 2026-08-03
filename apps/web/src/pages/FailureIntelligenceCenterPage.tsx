import { useNavigate } from 'react-router-dom';
import { AlertCircle, CheckCircle, Clock, AlertTriangle, TrendingUp, Plus, RefreshCw, Zap, Eye, BookOpen, History } from 'lucide-react';
import {
  useInvestigationQueue,
  type CompactMetricsSummary,
  type InvestigationQueueItem,
  type KnowledgeReviewItem,
  type NewSignatureItem,
} from '@servicehub/ui-shared/hooks/useInvestigationQueue';

function getPriorityLevel(score: number): { level: string; color: { bg: string; text: string; dot: string; border: string } } {
  if (score >= 15) return {
    level: 'Critical',
    color: { bg: 'bg-red-50', text: 'text-red-700', dot: 'bg-red-500', border: 'border-red-200' }
  };
  if (score >= 10) return {
    level: 'High',
    color: { bg: 'bg-orange-50', text: 'text-orange-700', dot: 'bg-orange-500', border: 'border-orange-200' }
  };
  if (score >= 5) return {
    level: 'Medium',
    color: { bg: 'bg-yellow-50', text: 'text-yellow-700', dot: 'bg-yellow-500', border: 'border-yellow-200' }
  };
  return {
    level: 'Low',
    color: { bg: 'bg-blue-50', text: 'text-blue-700', dot: 'bg-blue-500', border: 'border-blue-200' }
  };
}

function PriorityBadge({ score }: { score: number }) {
  const { level, color } = getPriorityLevel(score);
  return (
    <span className={`inline-flex items-center gap-1.5 px-2 py-1 rounded-full text-xs font-semibold ${color.bg} ${color.text}`}>
      <span className={`w-1.5 h-1.5 rounded-full ${color.dot}`} />
      {level}
    </span>
  );
}

function StatusBadge({ status }: { status: string }) {
  const colors: Record<string, { bg: string; text: string; dot: string }> = {
    'Active': { bg: 'bg-red-100', text: 'text-red-800', dot: 'bg-red-500' },
    'Reopened': { bg: 'bg-amber-100', text: 'text-amber-800', dot: 'bg-amber-500' },
    'Suppressed': { bg: 'bg-slate-100', text: 'text-slate-800', dot: 'bg-slate-500' },
    'Resolved': { bg: 'bg-green-100', text: 'text-green-800', dot: 'bg-green-500' },
    'Archived': { bg: 'bg-gray-100', text: 'text-gray-700', dot: 'bg-gray-400' },
  };
  const color = colors[status] || colors['Active'];
  return (
    <span className={`inline-flex items-center gap-1.5 px-2 py-1 rounded-full text-xs font-medium ${color.bg} ${color.text}`}>
      <span className={`w-1.5 h-1.5 rounded-full ${color.dot}`} />
      {status}
    </span>
  );
}

function TrendBadge({ trend }: { trend: string }) {
  if (trend === 'Escalating') {
    return <span className="inline-flex items-center gap-1 px-2 py-1 rounded-full text-xs font-medium bg-red-50 text-red-700"><TrendingUp className="w-3 h-3" /> Escalating</span>;
  }
  return null;
}

function MetricsHeader({ metrics }: { metrics: CompactMetricsSummary | undefined }) {
  if (!metrics) return null;

  const metricItems = [
    { label: 'Total Signatures', value: metrics.totalSignatures, color: 'text-gray-600' },
    { label: 'Active', value: metrics.activeSignatures, color: 'text-red-600', highlight: true },
    { label: 'Resolved', value: metrics.resolvedSignatures, color: 'text-green-600' },
    { label: 'Suppressed', value: metrics.suppressedSignatures, color: 'text-amber-600' },
    { label: 'Archived', value: metrics.archivedSignatures, color: 'text-gray-500' },
    { label: 'Requires Action', value: metrics.requiresAction, color: 'text-red-700', highlight: true },
  ];

  return (
    <div className="grid grid-cols-2 lg:grid-cols-6 gap-3 mb-6">
      {metricItems.map((item) => (
        <div
          key={item.label}
          className={`bg-white border rounded-lg p-4 ${item.highlight ? 'border-gray-300 shadow-sm' : 'border-gray-200'}`}
        >
          <p className="text-xs font-medium text-gray-600 mb-1" title={item.label}>{item.label}</p>
          <p className={`text-2xl font-bold ${item.color}`}>{item.value}</p>
        </div>
      ))}
    </div>
  );
}

function HighestPriorityBanner({ item }: { item: InvestigationQueueItem }) {
  const navigate = useNavigate();
  const { color } = getPriorityLevel(item.priorityScore);

  return (
    <div className={`border-2 ${color.border} ${color.bg} rounded-lg p-4 mb-6`}>
      <div className="flex items-start justify-between gap-4 mb-3">
        <div className="flex-1">
          <div className="flex items-center gap-2 mb-2">
            <Zap className={`w-4 h-4 ${color.text}`} />
            <span className="text-xs font-semibold uppercase tracking-wide text-gray-600">Highest Priority Incident</span>
          </div>
          <h3 className={`text-lg font-semibold ${color.text} mb-1`}>{item.displayName}</h3>
          <div className="flex items-center gap-2 flex-wrap mb-2">
            <StatusBadge status={item.status} />
            {item.isEscalating && <TrendBadge trend="Escalating" />}
            <PriorityBadge score={item.priorityScore} />
            <span className="text-xs text-gray-600">{item.messageCount} messages</span>
          </div>
          {item.explanation && <p className="text-sm text-gray-700 mt-1">{item.explanation}</p>}
        </div>
        <div className="text-right flex-shrink-0">
          <p className="text-xs font-semibold text-gray-600 mb-1">Recommended Action</p>
          <p className="text-sm font-medium text-gray-900">{item.recommendedNextAction || 'Investigate'}</p>
        </div>
      </div>
      <div className="flex gap-2 flex-wrap">
        <button
          onClick={() => navigate(`/signatures/${item.signatureHash}?namespace=${item.namespaceId}`)}
          className={`inline-flex items-center gap-1 text-xs px-3 py-2 font-medium rounded-md transition-colors border ${color.border} ${color.bg} ${color.text} hover:opacity-75`}
          aria-label="Investigate incident"
        >
          <Eye className="w-3.5 h-3.5" />
          Investigate
        </button>
        {item.isEscalating && (
          <button
            onClick={() => navigate(`/signatures/${item.signatureHash}?namespace=${item.namespaceId}&tab=replay`)}
            className="inline-flex items-center gap-1 text-xs px-3 py-2 font-medium rounded-md bg-white text-gray-700 border border-gray-300 hover:bg-gray-50 transition-colors"
            aria-label="Preview replay"
          >
            <History className="w-3.5 h-3.5" />
            Replay Preview
          </button>
        )}
      </div>
    </div>
  );
}

function InvestigationQueueSection({ items }: { items: InvestigationQueueItem[] | undefined }) {
  const navigate = useNavigate();

  if (!items || items.length === 0) {
    return (
      <div className="bg-white border border-gray-200 rounded-lg p-8 text-center mb-6">
        <CheckCircle className="w-8 h-8 text-green-500 mx-auto mb-2" />
        <p className="text-gray-900 font-medium">No incidents require attention</p>
        <p className="text-sm text-gray-600 mt-1">Your systems are running smoothly. New incidents will appear here as they occur.</p>
      </div>
    );
  }

  const highestPriority = items[0];
  const remainingItems = items.slice(1);

  return (
    <>
      <HighestPriorityBanner item={highestPriority} />

      {remainingItems.length > 0 && (
        <div className="mb-6">
          <h2 className="text-sm font-semibold text-gray-900 mb-3 flex items-center gap-2">
            <AlertCircle className="w-4 h-4" /> Other Investigations
          </h2>
          <div className="space-y-2">
            {remainingItems.map((item) => (
              <div key={item.signatureHash} className="bg-white border border-gray-200 rounded-lg p-4 hover:shadow-sm transition-shadow">
                <div className="flex items-start justify-between gap-4 mb-3">
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2 flex-wrap mb-2">
                      <StatusBadge status={item.status} />
                      {item.isEscalating && <TrendBadge trend="Escalating" />}
                      <PriorityBadge score={item.priorityScore} />
                      <span className="text-xs text-gray-500">{item.messageCount} messages</span>
                    </div>
                    <p className="font-medium text-gray-900 break-words">{item.displayName}</p>
                    {item.explanation && <p className="text-sm text-gray-600 mt-1">{item.explanation}</p>}
                  </div>
                </div>
                <div className="flex gap-2 flex-wrap">
                  <button
                    onClick={() => navigate(`/signatures/${item.signatureHash}?namespace=${item.namespaceId}`)}
                    className="text-xs px-3 py-1.5 bg-blue-50 text-blue-700 rounded-md hover:bg-blue-100 transition-colors font-medium"
                    aria-label={`Investigate ${item.displayName}`}
                  >
                    <Eye className="w-3 h-3 inline mr-1" />
                    Investigate
                  </button>
                  <button
                    onClick={() => navigate(`/signatures/${item.signatureHash}?namespace=${item.namespaceId}&tab=knowledge`)}
                    className="text-xs px-3 py-1.5 bg-gray-100 text-gray-700 rounded-md hover:bg-gray-200 transition-colors font-medium"
                    aria-label={`Review knowledge for ${item.displayName}`}
                  >
                    <BookOpen className="w-3 h-3 inline mr-1" />
                    Knowledge
                  </button>
                  <button
                    onClick={() => navigate(`/signatures/${item.signatureHash}?namespace=${item.namespaceId}&tab=replay`)}
                    className="text-xs px-3 py-1.5 bg-amber-50 text-amber-700 rounded-md hover:bg-amber-100 transition-colors font-medium"
                    aria-label={`Preview replay for ${item.displayName}`}
                  >
                    <History className="w-3 h-3 inline mr-1" />
                    Replay
                  </button>
                </div>
                {item.recommendedNextAction && (
                  <p className="text-xs text-gray-600 mt-2 font-medium">Recommended: {item.recommendedNextAction}</p>
                )}
              </div>
            ))}
          </div>
        </div>
      )}
    </>
  );
}

function KnowledgeReviewSection({ items }: { items: KnowledgeReviewItem[] | undefined }) {
  const navigate = useNavigate();

  if (!items || items.length === 0) {
    return (
      <div className="bg-white border border-gray-200 rounded-lg p-8 text-center mb-6">
        <BookOpen className="w-8 h-8 text-blue-500 mx-auto mb-2" />
        <p className="text-gray-900 font-medium">Knowledge is current</p>
        <p className="text-sm text-gray-600 mt-1">All signatures have up-to-date knowledge documentation.</p>
      </div>
    );
  }

  return (
    <div className="mb-6">
      <h2 className="text-sm font-semibold text-gray-900 mb-3 flex items-center gap-2">
        <Clock className="w-4 h-4" /> Knowledge Review
      </h2>
      <div className="space-y-2">
        {items.map((item) => (
          <div key={item.signatureHash} className="bg-white border border-gray-200 rounded-lg p-4 hover:shadow-sm transition-shadow">
            <div className="flex items-start justify-between gap-4 mb-3">
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2 flex-wrap mb-2">
                  {item.isReviewOverdue && (
                    <span className="inline-flex items-center gap-1 px-2 py-1 rounded-full text-xs font-medium bg-red-50 text-red-700">
                      <AlertTriangle className="w-3 h-3" />
                      Review Overdue
                    </span>
                  )}
                  <StatusBadge status={item.status} />
                  {item.owner && <span className="text-xs bg-gray-100 text-gray-700 px-2 py-1 rounded">Owner: {item.owner}</span>}
                  <span className="text-xs text-gray-500">{item.messageCount} messages</span>
                </div>
                <p className="font-medium text-gray-900 break-words">{item.displayName}</p>
              </div>
            </div>
            <div className="flex gap-2 flex-wrap">
              <button
                onClick={() => navigate(`/signatures/${item.signatureHash}?namespace=${item.namespaceId}&tab=knowledge`)}
                className="text-xs px-3 py-1.5 bg-blue-50 text-blue-700 rounded-md hover:bg-blue-100 transition-colors font-medium"
                aria-label={`${item.hasKnowledge ? 'Update' : 'Add'} knowledge for ${item.displayName}`}
              >
                <BookOpen className="w-3 h-3 inline mr-1" />
                {item.hasKnowledge ? 'Update Knowledge' : 'Add Knowledge'}
              </button>
              <button
                onClick={() => navigate(`/signatures/${item.signatureHash}?namespace=${item.namespaceId}`)}
                className="text-xs px-3 py-1.5 bg-gray-100 text-gray-700 rounded-md hover:bg-gray-200 transition-colors font-medium"
                aria-label={`Investigate ${item.displayName}`}
              >
                <Eye className="w-3 h-3 inline mr-1" />
                View Details
              </button>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

function NewSignaturesSection({ items }: { items: NewSignatureItem[] | undefined }) {
  const navigate = useNavigate();

  if (!items || items.length === 0) {
    return (
      <div className="bg-white border border-gray-200 rounded-lg p-8 text-center mb-6">
        <Plus className="w-8 h-8 text-purple-500 mx-auto mb-2" />
        <p className="text-gray-900 font-medium">No new signatures</p>
        <p className="text-sm text-gray-600 mt-1">All failure signatures have been documented. New failures will appear here.</p>
      </div>
    );
  }

  return (
    <div className="mb-6">
      <h2 className="text-sm font-semibold text-gray-900 mb-3 flex items-center gap-2">
        <Plus className="w-4 h-4" /> New / Unknown Signatures
      </h2>
      <div className="space-y-2">
        {items.map((item) => (
          <div key={item.signatureHash} className="bg-white border border-gray-200 rounded-lg p-4 hover:shadow-sm transition-shadow">
            <div className="flex items-start justify-between gap-4 mb-3">
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2 flex-wrap mb-2">
                  <span className="text-xs bg-purple-50 text-purple-700 px-2 py-1 rounded font-medium">
                    First seen: {new Date(item.firstSeenAt).toLocaleDateString()}
                  </span>
                  <span className="text-xs text-gray-500">{item.messageCount} messages</span>
                </div>
                <p className="font-medium text-gray-900 break-words">{item.displayName}</p>
              </div>
            </div>
            <div className="flex gap-2 flex-wrap">
              <button
                onClick={() => navigate(`/signatures/${item.signatureHash}?namespace=${item.namespaceId}&tab=knowledge`)}
                className="text-xs px-3 py-1.5 bg-blue-50 text-blue-700 rounded-md hover:bg-blue-100 transition-colors font-medium"
                aria-label={`Add knowledge for ${item.displayName}`}
              >
                <BookOpen className="w-3 h-3 inline mr-1" />
                Add Knowledge
              </button>
              <button
                onClick={() => navigate(`/signatures/${item.signatureHash}?namespace=${item.namespaceId}`)}
                className="text-xs px-3 py-1.5 bg-gray-100 text-gray-700 rounded-md hover:bg-gray-200 transition-colors font-medium"
                aria-label={`Investigate ${item.displayName}`}
              >
                <Eye className="w-3 h-3 inline mr-1" />
                View Details
              </button>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

export function FailureIntelligenceCenterPage() {
  const { data, isLoading, error, refetch } = useInvestigationQueue();

  if (isLoading) {
    return (
      <div className="flex items-center justify-center min-h-screen bg-gray-50">
        <div className="text-center">
          <div className="animate-spin rounded-full h-10 w-10 border-b-2 border-primary-600 mb-4 mx-auto" />
          <p className="text-gray-600 font-medium">Loading Investigation Center...</p>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center min-h-screen bg-gray-50">
        <div className="text-center max-w-md">
          <AlertCircle className="w-12 h-12 text-red-500 mx-auto mb-4" />
          <h2 className="text-lg font-semibold text-gray-900 mb-1">Unable to load Investigation Center</h2>
          <p className="text-gray-600 mb-4">Please try again or contact support if the problem persists.</p>
          <button
            onClick={() => refetch()}
            className="inline-flex items-center gap-2 px-4 py-2 text-sm font-medium bg-primary-600 text-white rounded-md hover:bg-primary-700 transition-colors"
            aria-label="Retry loading Investigation Center"
          >
            <RefreshCw className="w-4 h-4" />
            Try Again
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-6 md:py-8 overflow-y-auto flex flex-col">
      <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4 mb-6">
        <div className="flex-1">
          <h1 className="text-2xl md:text-3xl font-bold text-gray-900">Incident Center</h1>
          <p className="text-sm text-gray-600 mt-1">Operational command center for failure investigation and remediation</p>
        </div>
        <button
          onClick={() => refetch()}
          className="inline-flex items-center gap-2 px-4 py-2 text-sm font-medium bg-white border border-gray-300 rounded-md hover:bg-gray-50 transition-colors whitespace-nowrap"
          aria-label="Refresh Investigation Center data"
        >
          <RefreshCw className="w-4 h-4" />
          Refresh
        </button>
      </div>

      {data && (
        <>
          <MetricsHeader metrics={data.metrics} />
          <InvestigationQueueSection items={data.investigationQueue} />
          <KnowledgeReviewSection items={data.knowledgeReview} />
          <NewSignaturesSection items={data.newSignatures} />
        </>
      )}
    </div>
  );
}
