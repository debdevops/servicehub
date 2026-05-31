/**
 * AwsDemoPage — Dedicated AWS SQS / SNS Demo Experience
 *
 * Completely separate from the Azure demo. Uses SQS/SNS terminology,
 * orange branding, and AcmeRetail E-Commerce scenario mock data.
 * Route: /demo/aws
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
  Layers,
  Tag,
  ChevronRight,
  Zap,
} from 'lucide-react';
import { generateAwsMockMessages } from '@/lib/awsMockData';
import type { Message } from '@/lib/mockData';

// ── AWS-specific terminology map ──────────────────────────────────────────────
const AWS_QUEUE_NAMES = [
  { name: 'order-processing', dlq: 'order-processing-dlq', type: 'Standard' },
  { name: 'payment-gateway-events', dlq: 'payment-gateway-events-dlq', type: 'FIFO' },
  { name: 'notification-service', dlq: 'notification-service-dlq', type: 'Standard' },
  { name: 'fraud-detection', dlq: 'fraud-detection-dlq', type: 'Standard' },
  { name: 'inventory-sync', dlq: 'inventory-sync-dlq', type: 'FIFO' },
  { name: 'cart-abandonment', dlq: null, type: 'Standard' },
];

const SNS_TOPICS = [
  'order-events-topic',
  'payment-alerts-topic',
  'customer-notifications-topic',
];

type TabType = 'active' | 'dlq';

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
  const receiveCount = msg.deliveryCount;
  return (
    <button
      onClick={onClick}
      className={`w-full text-left px-4 py-3.5 border-b border-gray-100 flex items-start gap-3 transition-colors ${
        selected ? 'bg-orange-50 border-l-4 border-l-orange-500' : 'hover:bg-gray-50'
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
          {receiveCount > 1 && (
            <span className="text-[10px] font-semibold bg-amber-100 text-amber-700 px-1.5 py-0.5 rounded">
              ReceiveCount: {receiveCount}
            </span>
          )}
          {msg.deadLetterReason && (
            <span className="text-[10px] font-semibold bg-red-100 text-red-600 px-1.5 py-0.5 rounded truncate max-w-[200px]">
              DLR: {msg.deadLetterReason}
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
    'MessageId': msg.id,
    'ApproximateReceiveCount': String(msg.deliveryCount),
    'SentTimestamp': msg.enqueuedTime.toISOString(),
    'ContentType': msg.contentType,
    ...(msg.deadLetterReason ? { 'DeadLetterReason': msg.deadLetterReason } : {}),
    ...(msg.deadLetterSource ? { 'DeadLetterSource': msg.deadLetterSource } : {}),
  };

  return (
    <div className="flex flex-col h-full">
      {/* Header */}
      <div className="px-4 py-3 border-b border-gray-200 bg-orange-50">
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
                ? 'border-b-2 border-orange-500 text-orange-700'
                : 'text-gray-500 hover:text-gray-700'
            }`}
          >
            {t === 'attributes' ? 'Message Attributes' : t === 'metadata' ? 'SQS Metadata' : 'Body'}
          </button>
        ))}
      </div>

      {/* Content */}
      <div className="flex-1 overflow-auto p-4 bg-gray-50 font-mono text-xs leading-relaxed">
        {bodyTab === 'body' && (
          <pre className="whitespace-pre-wrap text-gray-800">{prettyBody || '[Empty body]'}</pre>
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
              { k: 'Queue', v: 'acmeretail-prod/order-processing' },
              { k: 'Region', v: 'us-east-1' },
              { k: 'SequenceNumber', v: String(msg.sequenceNumber) },
              { k: 'MessageGroupId', v: msg.properties?.['MessageGroupId'] as string ?? '—' },
              { k: 'ContentType', v: msg.contentType },
              { k: 'VisibilityTimeout', v: '30s' },
              { k: 'MaxReceiveCount', v: '3' },
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
        <div className="shrink-0 border-t border-gray-200 p-4 bg-amber-50">
          <div className="flex items-start gap-2">
            <Zap className="w-4 h-4 text-amber-600 mt-0.5 shrink-0" />
            <div>
              <p className="text-xs font-bold text-amber-800 mb-1">AI Root Cause</p>
              <p className="text-xs text-amber-700 leading-relaxed">{msg.aiAnalysis.issue}</p>
              {msg.aiAnalysis.recommendations[0] && (
                <p className="text-xs text-amber-600 mt-1.5 font-medium">
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

export function AwsDemoPage() {
  const navigate = useNavigate();
  const [selectedQueue, setSelectedQueue] = useState(AWS_QUEUE_NAMES[0].name);
  const [tab, setTab] = useState<TabType>('active');
  const [selectedMsgId, setSelectedMsgId] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [sidebarSection, setSidebarSection] = useState<'queues' | 'topics'>('queues');

  const allMessages = useMemo(() => generateAwsMockMessages(60), []);

  const activeMessages = useMemo(
    () => allMessages.filter((m) => m.queueType === 'active'),
    [allMessages]
  );
  const dlqMessages = useMemo(
    () => allMessages.filter((m) => m.queueType === 'deadletter'),
    [allMessages]
  );

  const tabMessages = tab === 'dlq' ? dlqMessages : activeMessages;

  const filtered = useMemo(() => {
    if (!search.trim()) return tabMessages;
    const q = search.toLowerCase();
    return tabMessages.filter(
      (m) =>
        m.id.toLowerCase().includes(q) ||
        m.preview.toLowerCase().includes(q) ||
        m.body.toLowerCase().includes(q)
    );
  }, [tabMessages, search]);

  const selectedMsg = useMemo(
    () => filtered.find((m) => m.id === selectedMsgId) ?? null,
    [filtered, selectedMsgId]
  );

  return (
    <div className="flex h-screen flex-col bg-white overflow-hidden">
      {/* ── Top bar ── */}
      <header className="h-12 bg-orange-500 text-white flex items-center px-4 gap-3 shrink-0 shadow">
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
            AWS
          </span>
          <span className="font-semibold text-sm">SQS / SNS Demo</span>
          <span className="text-[10px] bg-amber-600/50 border border-amber-300/30 px-2 py-0.5 rounded font-medium">
            AcmeRetail E-Commerce · us-east-1
          </span>
        </div>
        <div className="flex-1" />
        <span className="text-xs text-white/60">
          {activeMessages.length} active · {dlqMessages.length} in DLQ
        </span>
        <button
          onClick={() => navigate('/connect')}
          className="flex items-center gap-1.5 px-3 py-1.5 bg-white text-orange-700 text-xs font-bold rounded-lg hover:bg-orange-50 transition-colors"
        >
          Connect Real AWS →
        </button>
      </header>

      {/* ── Body ── */}
      <div className="flex flex-1 overflow-hidden">
        {/* ── Sidebar ── */}
        <aside className="w-56 bg-slate-50 border-r border-gray-200 flex flex-col shrink-0 overflow-hidden">
          {/* Namespace header */}
          <div className="px-3 py-3 border-b border-gray-200">
            <div className="flex items-center gap-2">
              <div className="w-7 h-7 bg-orange-500 rounded flex items-center justify-center shrink-0">
                <span className="text-white text-[10px] font-black">AWS</span>
              </div>
              <div>
                <p className="text-xs font-bold text-gray-800 leading-none">acmeretail-prod</p>
                <p className="text-[10px] text-gray-500 mt-0.5">us-east-1 · Demo</p>
              </div>
            </div>
          </div>

          {/* Section tabs */}
          <div className="flex border-b border-gray-200">
            <button
              onClick={() => setSidebarSection('queues')}
              className={`flex-1 py-2 text-xs font-semibold flex items-center justify-center gap-1 transition-colors ${
                sidebarSection === 'queues' ? 'text-orange-700 border-b-2 border-orange-500 bg-white' : 'text-gray-500 hover:text-gray-700'
              }`}
            >
              <Inbox className="w-3.5 h-3.5" />
              Queues
            </button>
            <button
              onClick={() => setSidebarSection('topics')}
              className={`flex-1 py-2 text-xs font-semibold flex items-center justify-center gap-1 transition-colors ${
                sidebarSection === 'topics' ? 'text-orange-700 border-b-2 border-orange-500 bg-white' : 'text-gray-500 hover:text-gray-700'
              }`}
            >
              <Layers className="w-3.5 h-3.5" />
              Topics
            </button>
          </div>

          <div className="flex-1 overflow-y-auto py-1">
            {sidebarSection === 'queues' && (
              <>
                <p className="px-3 pt-2 pb-1 text-[10px] font-bold uppercase text-gray-400 tracking-wider">SQS Queues</p>
                {AWS_QUEUE_NAMES.map((q) => (
                  <button
                    key={q.name}
                    onClick={() => {
                      setSelectedQueue(q.name);
                      setSelectedMsgId(null);
                    }}
                    className={`w-full flex items-center justify-between px-3 py-2 text-xs transition-colors ${
                      selectedQueue === q.name
                        ? 'bg-orange-50 text-orange-800 font-semibold border-r-2 border-orange-500'
                        : 'text-gray-700 hover:bg-gray-100'
                    }`}
                  >
                    <span className="truncate flex items-center gap-1.5">
                      <Inbox className="w-3 h-3 text-orange-400 shrink-0" />
                      {q.name}
                    </span>
                    <span className={`text-[10px] px-1.5 py-0.5 rounded font-medium ${
                      q.type === 'FIFO' ? 'bg-blue-100 text-blue-700' : 'bg-gray-100 text-gray-500'
                    }`}>
                      {q.type}
                    </span>
                  </button>
                ))}
              </>
            )}
            {sidebarSection === 'topics' && (
              <>
                <p className="px-3 pt-2 pb-1 text-[10px] font-bold uppercase text-gray-400 tracking-wider">SNS Topics</p>
                {SNS_TOPICS.map((t) => (
                  <div
                    key={t}
                    className="flex items-center gap-2 px-3 py-2 text-xs text-gray-700 hover:bg-gray-100 cursor-default"
                  >
                    <Tag className="w-3 h-3 text-orange-400 shrink-0" />
                    <span className="truncate">{t}</span>
                    <ChevronRight className="w-3 h-3 text-gray-400 shrink-0 ml-auto" />
                  </div>
                ))}
                <p className="px-3 py-2 text-[10px] text-gray-400 italic">
                  SNS → SQS subscriptions active
                </p>
              </>
            )}
          </div>
        </aside>

        {/* ── Messages list ── */}
        <div className="flex-1 flex flex-col overflow-hidden">
          {/* Tab bar */}
          <div className="flex items-center gap-0 border-b border-gray-200 bg-white shrink-0 px-4 pt-2">
            {([
              { id: 'active', label: 'Active Messages', count: activeMessages.length, color: 'text-green-700 border-orange-500' },
              { id: 'dlq', label: 'Dead-Letter Queue (DLQ)', count: dlqMessages.length, color: 'text-red-700 border-red-500' },
            ] as const).map((t) => (
              <button
                key={t.id}
                onClick={() => { setTab(t.id); setSelectedMsgId(null); }}
                className={`flex items-center gap-2 px-4 py-2.5 text-sm font-semibold border-b-2 transition-colors mr-1 ${
                  tab === t.id
                    ? t.color
                    : 'text-gray-500 border-transparent hover:text-gray-700'
                }`}
              >
                {t.id === 'dlq'
                  ? <AlertCircle className="w-4 h-4" />
                  : <CheckCircle className="w-4 h-4" />}
                {t.label}
                <span className={`text-[11px] px-2 py-0.5 rounded-full font-bold ${
                  t.id === 'dlq' ? 'bg-red-100 text-red-700' : 'bg-green-100 text-green-700'
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
                placeholder="Search SQS messages…"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                className="pl-8 pr-3 py-1.5 text-xs bg-gray-50 border border-gray-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-orange-400 w-52"
              />
              {search && (
                <button onClick={() => setSearch('')} className="absolute right-2.5 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600">
                  <X className="w-3 h-3" />
                </button>
              )}
            </div>
            <button className="pb-2 ml-2 p-1.5 text-gray-500 hover:text-orange-600 hover:bg-orange-50 rounded transition-colors">
              <Filter className="w-4 h-4" />
            </button>
            <button className="pb-2 p-1.5 text-gray-500 hover:text-orange-600 hover:bg-orange-50 rounded transition-colors">
              <RefreshCw className="w-4 h-4" />
            </button>
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
            <span>Queue: <strong className="text-gray-700">{selectedQueue}</strong></span>
            <span>Showing {filtered.length} / {tabMessages.length} messages</span>
            <span className="ml-auto text-green-600 font-medium">● Demo Mode — AcmeRetail us-east-1</span>
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
                Click any row to see the full SQS message body, MessageAttributes, ApproximateReceiveCount, and AI root-cause analysis.
              </p>
              {tab === 'active' && (
                <button
                  onClick={() => { setTab('dlq'); setSelectedMsgId(null); }}
                  className="mt-2 flex items-center gap-1.5 px-4 py-2 bg-red-50 border border-red-200 text-red-700 text-xs font-semibold rounded-lg hover:bg-red-100 transition-colors"
                >
                  <AlertCircle className="w-3.5 h-3.5" />
                  View Dead-Letter Queue ({dlqMessages.length} messages)
                </button>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
