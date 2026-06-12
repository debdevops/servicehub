/**
 * GcpDemoPage — Dedicated GCP Pub/Sub Demo Experience
 *
 * Completely separate from the Azure and AWS demos. Uses Pub/Sub terminology
 * (topics, subscriptions, ack deadline, DLT), green branding, and
 * MedStream Healthcare Analytics scenario mock data.
 * Route: /demo/gcp
 */

import { useState, useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  Search,
  X,
  Filter,
  RefreshCw,
  ArrowLeft,
  AlertCircle,
  CheckCircle,
  Clock,
  Inbox,
  Hash,
  Layers,
  ChevronRight,
  Zap,
} from 'lucide-react';
import { generateGcpMockMessages } from '@/lib/gcpMockData';
import type { Message } from '@/lib/mockData';

// ── GCP-specific structure ────────────────────────────────────────────────────
const GCP_TOPICS = [
  {
    name: 'patient-intake',
    subscriptions: ['intake-processor-sub', 'ehr-sync-sub'],
    dlt: 'patient-intake-dlq',
  },
  {
    name: 'lab-results',
    subscriptions: ['results-router-sub', 'physician-notify-sub', 'hl7-export-sub'],
    dlt: 'lab-results-dlq',
  },
  {
    name: 'billing-events',
    subscriptions: ['insurance-claims-sub', 'patient-billing-sub'],
    dlt: 'billing-events-dlq',
  },
  {
    name: 'appointment-reminders',
    subscriptions: ['sms-gateway-sub'],
    dlt: null,
  },
  {
    name: 'medication-orders',
    subscriptions: ['pharmacy-sub', 'dea-audit-sub'],
    dlt: 'medication-orders-dlq',
  },
  {
    name: 'clinical-alerts',
    subscriptions: ['oncall-pager-sub', 'dashboard-sub'],
    dlt: 'clinical-alerts-dlq',
  },
];

type TabType = 'active' | 'dlt';

function StatusDot({ status }: { status: Message['status'] }) {
  if (status === 'error') return <span className="w-2.5 h-2.5 rounded-full bg-red-500 shrink-0" />;
  if (status === 'warning') return <span className="w-2.5 h-2.5 rounded-full bg-amber-400 shrink-0" />;
  return <span className="w-2.5 h-2.5 rounded-full bg-green-500 shrink-0" />;
}

function MessageCard({
  msg,
  selected,
  onClick,
}: {
  msg: Message;
  selected: boolean;
  onClick: () => void;
}) {
  return (
    <button
      onClick={onClick}
      className={`w-full text-left px-4 py-3.5 border-b border-gray-100 flex items-start gap-3 transition-colors ${
        selected ? 'bg-green-50 border-l-4 border-l-green-600' : 'hover:bg-gray-50'
      }`}
    >
      <StatusDot status={msg.status} />
      <div className="flex-1 min-w-0">
        <div className="flex items-center justify-between gap-2 mb-1">
          <span className="text-xs font-mono text-gray-500 truncate">{msg.id.substring(0, 24)}…</span>
          <span className="text-[10px] text-gray-400 shrink-0">
            {msg.enqueuedTime.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
          </span>
        </div>
        <p className="text-sm text-gray-800 truncate leading-snug">{msg.preview}</p>
        <div className="flex items-center gap-2 mt-1.5">
          {msg.deliveryCount > 0 && (
            <span className="text-[10px] font-semibold bg-amber-100 text-amber-700 px-1.5 py-0.5 rounded">
              DeliveryAttempt: {msg.deliveryCount}
            </span>
          )}
          {msg.deadLetterReason && (
            <span className="text-[10px] font-semibold bg-red-100 text-red-600 px-1.5 py-0.5 rounded truncate max-w-[200px]">
              DLT: {msg.deadLetterReason}
            </span>
          )}
        </div>
      </div>
    </button>
  );
}

function MessageDetail({ msg }: { msg: Message }) {
  const [bodyTab, setBodyTab] = useState<'body' | 'attributes' | 'metadata'>('body');

  let prettyBody = msg.body;
  try {
    prettyBody = JSON.stringify(JSON.parse(msg.body), null, 2);
  } catch {
    /* not JSON */
  }

  const attributes: Record<string, string> = {
    'message_id': msg.id,
    'delivery_attempt': String(msg.deliveryCount),
    'publish_time': msg.enqueuedTime.toISOString(),
    'content_type': msg.contentType,
    ...(msg.deadLetterReason ? { 'dead_letter_reason': msg.deadLetterReason } : {}),
    ...(msg.deadLetterSource ? { 'dead_letter_source_subscription': msg.deadLetterSource } : {}),
  };

  return (
    <div className="flex flex-col h-full">
      {/* Header */}
      <div className="px-4 py-3 border-b border-gray-200 bg-green-50">
        <div className="flex items-center gap-2 mb-1">
          <StatusDot status={msg.status} />
          <span className="text-xs font-mono text-gray-500">{msg.id}</span>
        </div>
        {msg.deadLetterReason && (
          <div className="flex items-center gap-1.5 mt-1 text-xs text-red-700 bg-red-50 border border-red-200 rounded px-2 py-1">
            <AlertCircle className="w-3.5 h-3.5 shrink-0" />
            <span className="font-semibold">{msg.deadLetterReason}</span>
          </div>
        )}
      </div>

      {/* Tabs */}
      <div className="flex border-b border-gray-200 bg-white shrink-0">
        {(['body', 'attributes', 'metadata'] as const).map((t) => (
          <button
            key={t}
            onClick={() => setBodyTab(t)}
            className={`px-4 py-2.5 text-xs font-semibold capitalize transition-colors ${
              bodyTab === t
                ? 'border-b-2 border-green-600 text-green-700'
                : 'text-gray-500 hover:text-gray-700'
            }`}
          >
            {t === 'attributes' ? 'Pub/Sub Attributes' : t === 'metadata' ? 'Subscription Metadata' : 'Data'}
          </button>
        ))}
      </div>

      {/* Content */}
      <div className="flex-1 overflow-auto p-4 bg-gray-50 font-mono text-xs leading-relaxed">
        {bodyTab === 'body' && (
          <pre className="whitespace-pre-wrap text-gray-800">{prettyBody || '[Empty data]'}</pre>
        )}
        {bodyTab === 'attributes' && (
          <div className="space-y-2 font-sans">
            {Object.entries(attributes).map(([k, v]) => (
              <div key={k} className="flex gap-3">
                <span className="font-semibold text-gray-600 w-52 shrink-0">{k}</span>
                <span className="text-gray-800 break-all">{v}</span>
              </div>
            ))}
          </div>
        )}
        {bodyTab === 'metadata' && (
          <div className="space-y-2 font-sans">
            {[
              { k: 'Project', v: 'medstream-prod-gcp' },
              { k: 'Topic', v: 'projects/medstream-prod-gcp/topics/lab-results' },
              { k: 'Subscription', v: 'results-router-sub' },
              { k: 'AckDeadline', v: '600s' },
              { k: 'MaxDeliveryAttempts', v: '5' },
              { k: 'RetainAckedMessages', v: 'true (7 days)' },
              { k: 'SequenceNumber', v: String(msg.sequenceNumber) },
            ].map(({ k, v }) => (
              <div key={k} className="flex gap-3">
                <span className="font-semibold text-gray-600 w-48 shrink-0">{k}</span>
                <span className="text-gray-800">{v}</span>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* AI Root Cause */}
      {msg.aiAnalysis && (
        <div className="shrink-0 border-t border-gray-200 p-4 bg-emerald-50">
          <div className="flex items-start gap-2">
            <Zap className="w-4 h-4 text-emerald-600 mt-0.5 shrink-0" />
            <div>
              <p className="text-xs font-bold text-emerald-800 mb-1">AI Root Cause</p>
              <p className="text-xs text-emerald-700 leading-relaxed">{msg.aiAnalysis.issue}</p>
              {msg.aiAnalysis.recommendations[0] && (
                <p className="text-xs text-emerald-600 mt-1.5 font-medium">
                  → {msg.aiAnalysis.recommendations[0]}
                </p>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export function GcpDemoPage() {
  const navigate = useNavigate();
  const [selectedTopic, setSelectedTopic] = useState(GCP_TOPICS[0].name);
  const [selectedSub, setSelectedSub] = useState(GCP_TOPICS[0].subscriptions[0]);
  const [tab, setTab] = useState<TabType>('active');
  const [selectedMsgId, setSelectedMsgId] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [expandedTopics, setExpandedTopics] = useState<Set<string>>(new Set(['patient-intake', 'lab-results']));

  const allMessages = useMemo(() => generateGcpMockMessages(60), []);

  const activeMessages = useMemo(() => allMessages.filter((m) => m.queueType === 'active'), [allMessages]);
  const dltMessages = useMemo(() => allMessages.filter((m) => m.queueType === 'deadletter'), [allMessages]);

  const tabMessages = tab === 'dlt' ? dltMessages : activeMessages;

  const filtered = useMemo(() => {
    if (!search.trim()) return tabMessages;
    const q = search.toLowerCase();
    return tabMessages.filter(
      (m) => m.id.toLowerCase().includes(q) || m.preview.toLowerCase().includes(q) || m.body.toLowerCase().includes(q)
    );
  }, [tabMessages, search]);

  const selectedMsg = useMemo(() => filtered.find((m) => m.id === selectedMsgId) ?? null, [filtered, selectedMsgId]);

  const toggleTopic = (topic: string) => {
    setExpandedTopics((prev) => {
      const next = new Set(prev);
      if (next.has(topic)) {
        next.delete(topic);
      } else {
        next.add(topic);
      }
      return next;
    });
  };

  return (
    <div className="flex h-screen flex-col bg-white overflow-hidden">
      {/* ── Top bar ── */}
      <header className="h-12 bg-green-700 text-white flex items-center px-4 gap-3 shrink-0 shadow">
        <button
          onClick={() => navigate('/')}
          className="flex items-center gap-1.5 text-white/80 hover:text-white text-sm transition-colors"
        >
          <ArrowLeft className="w-4 h-4" />
          Home
        </button>
        <div className="w-px h-5 bg-white/30" />
        <div className="flex items-center gap-2">
          <span className="w-6 h-6 bg-white/20 rounded flex items-center justify-center text-[11px] font-black">
            GCP
          </span>
          <span className="font-semibold text-sm">Pub/Sub Demo</span>
          <span className="text-[10px] bg-green-800/50 border border-green-400/30 px-2 py-0.5 rounded font-medium">
            MedStream Healthcare · medstream-prod-gcp
          </span>
        </div>
        <div className="flex-1" />
        <span className="text-xs text-white/60">
          {activeMessages.length} active · {dltMessages.length} in DLT
        </span>
        <button
          onClick={() => navigate('/connect')}
          className="flex items-center gap-1.5 px-3 py-1.5 bg-white text-green-800 text-xs font-bold rounded-lg hover:bg-green-50 transition-colors"
        >
          Connect Real GCP →
        </button>
      </header>

      {/* ── Body ── */}
      <div className="flex flex-1 overflow-hidden">
        {/* ── Sidebar ── */}
        <aside className="w-60 bg-slate-50 border-r border-gray-200 flex flex-col shrink-0 overflow-hidden">
          {/* Namespace */}
          <div className="px-3 py-3 border-b border-gray-200">
            <div className="flex items-center gap-2">
              <div className="w-7 h-7 bg-green-700 rounded flex items-center justify-center shrink-0">
                <span className="text-white text-[10px] font-black">GCP</span>
              </div>
              <div>
                <p className="text-xs font-bold text-gray-800 leading-none">medstream-prod-gcp</p>
                <p className="text-[10px] text-gray-500 mt-0.5">Google Cloud · Demo</p>
              </div>
            </div>
          </div>

          <p className="px-3 pt-2 pb-1 text-[10px] font-bold uppercase text-gray-400 tracking-wider">Topics & Subscriptions</p>

          <div className="flex-1 overflow-y-auto pb-2">
            {GCP_TOPICS.map((topic) => {
              const expanded = expandedTopics.has(topic.name);
              return (
                <div key={topic.name}>
                  <button
                    onClick={() => toggleTopic(topic.name)}
                    className="w-full flex items-center gap-2 px-3 py-2 text-xs font-semibold text-gray-700 hover:bg-gray-100 transition-colors"
                  >
                    <Layers className="w-3.5 h-3.5 text-green-600 shrink-0" />
                    <span className="flex-1 truncate text-left">{topic.name}</span>
                    <ChevronRight className={`w-3.5 h-3.5 text-gray-400 transition-transform ${expanded ? 'rotate-90' : ''}`} />
                  </button>
                  {expanded && (
                    <div className="pl-6">
                      {topic.subscriptions.map((sub) => (
                        <button
                          key={sub}
                          onClick={() => {
                            setSelectedTopic(topic.name);
                            setSelectedSub(sub);
                            setSelectedMsgId(null);
                          }}
                          className={`w-full flex items-center gap-2 px-3 py-1.5 text-xs transition-colors ${
                            selectedTopic === topic.name && selectedSub === sub
                              ? 'bg-green-50 text-green-800 font-semibold border-r-2 border-green-600'
                              : 'text-gray-600 hover:bg-gray-100'
                          }`}
                        >
                          <Hash className="w-3 h-3 text-green-500 shrink-0" />
                          <span className="truncate">{sub}</span>
                        </button>
                      ))}
                      {topic.dlt && (
                        <button
                          onClick={() => { setSelectedTopic(topic.name); setSelectedSub(topic.dlt!); setTab('dlt'); setSelectedMsgId(null); }}
                          className="w-full flex items-center gap-2 px-3 py-1.5 text-xs text-red-600 hover:bg-red-50 transition-colors"
                        >
                          <AlertCircle className="w-3 h-3 shrink-0" />
                          <span className="truncate">{topic.dlt}</span>
                          <span className="ml-auto text-[9px] bg-red-100 px-1 rounded font-bold">DLT</span>
                        </button>
                      )}
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        </aside>

        {/* ── Messages list ── */}
        <div className="flex-1 flex flex-col overflow-hidden">
          {/* Tab bar */}
          <div className="flex items-center border-b border-gray-200 bg-white shrink-0 px-4 pt-2">
            {([
              { id: 'active', label: 'Subscription Messages', count: activeMessages.length },
              { id: 'dlt', label: 'Dead-Letter Topic (DLT)', count: dltMessages.length },
            ] as const).map((t) => (
              <button
                key={t.id}
                onClick={() => { setTab(t.id); setSelectedMsgId(null); }}
                className={`flex items-center gap-2 px-4 py-2.5 text-sm font-semibold border-b-2 transition-colors mr-1 ${
                  tab === t.id
                    ? (t.id === 'dlt' ? 'text-red-700 border-red-500' : 'text-green-700 border-green-600')
                    : 'text-gray-500 border-transparent hover:text-gray-700'
                }`}
              >
                {t.id === 'dlt' ? <AlertCircle className="w-4 h-4" /> : <CheckCircle className="w-4 h-4" />}
                {t.label}
                <span className={`text-[11px] px-2 py-0.5 rounded-full font-bold ${
                  t.id === 'dlt' ? 'bg-red-100 text-red-700' : 'bg-green-100 text-green-700'
                }`}>
                  {t.count}
                </span>
              </button>
            ))}
            <div className="flex-1" />
            <div className="relative pb-2">
              <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-gray-400" />
              <input
                type="text"
                placeholder="Search Pub/Sub messages…"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                className="pl-8 pr-3 py-1.5 text-xs bg-gray-50 border border-gray-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-green-500 w-52"
              />
              {search && (
                <button onClick={() => setSearch('')} className="absolute right-2.5 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600">
                  <X className="w-3 h-3" />
                </button>
              )}
            </div>
            <button className="pb-2 ml-2 p-1.5 text-gray-500 hover:text-green-700 hover:bg-green-50 rounded transition-colors">
              <Filter className="w-4 h-4" />
            </button>
            <button className="pb-2 p-1.5 text-gray-500 hover:text-green-700 hover:bg-green-50 rounded transition-colors">
              <RefreshCw className="w-4 h-4" />
            </button>
          </div>

          {/* Context line */}
          <div className="px-4 py-1.5 bg-green-50 border-b border-green-100 text-xs text-green-700 flex items-center gap-2">
            <Layers className="w-3.5 h-3.5" />
            <span>Topic: <strong>{selectedTopic}</strong></span>
            <span className="text-green-400">›</span>
            <span>Subscription: <strong>{selectedSub}</strong></span>
          </div>

          {/* Message list */}
          <div className="flex-1 overflow-y-auto bg-white">
            {filtered.length === 0 ? (
              <div className="flex flex-col items-center justify-center h-full text-gray-400 gap-3">
                <Inbox className="w-10 h-10" />
                <p className="text-sm">No messages found</p>
              </div>
            ) : (
              filtered.map((msg) => (
                <MessageCard
                  key={msg.id}
                  msg={msg}
                  selected={selectedMsgId === msg.id}
                  onClick={() => setSelectedMsgId(msg.id)}
                />
              ))
            )}
          </div>

          {/* Status bar */}
          <div className="h-7 bg-gray-50 border-t border-gray-200 px-4 flex items-center gap-4 text-[11px] text-gray-500 shrink-0">
            <Clock className="w-3 h-3" />
            <span>Subscription: <strong className="text-gray-700">{selectedSub}</strong></span>
            <span>Showing {filtered.length} / {tabMessages.length} messages</span>
            <span className="ml-auto text-green-600 font-medium">● Demo Mode — MedStream Healthcare GCP</span>
          </div>
        </div>

        {/* ── Detail panel ── */}
        <div className="w-[420px] border-l border-gray-200 bg-white flex flex-col shrink-0 overflow-hidden">
          {selectedMsg ? (
            <MessageDetail msg={selectedMsg} />
          ) : (
            <div className="flex flex-col items-center justify-center h-full text-gray-400 gap-3 px-8 text-center">
              <Inbox className="w-12 h-12" />
              <p className="text-sm font-medium text-gray-600">Select a message to inspect</p>
              <p className="text-xs leading-relaxed">
                Click any row to see the full Pub/Sub message data, attributes, AckDeadline metadata, and AI root-cause analysis.
              </p>
              {tab === 'active' && (
                <button
                  onClick={() => { setTab('dlt'); setSelectedMsgId(null); }}
                  className="mt-2 flex items-center gap-1.5 px-4 py-2 bg-red-50 border border-red-200 text-red-700 text-xs font-semibold rounded-lg hover:bg-red-100 transition-colors"
                >
                  <AlertCircle className="w-3.5 h-3.5" />
                  View Dead-Letter Topic ({dltMessages.length} messages)
                </button>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
