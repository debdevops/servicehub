import { Link, useNavigate } from 'react-router-dom';
import {
  Search,
  Zap,
  Lock,
  BarChart3,
  Github,
  ArrowRight,
  Check,
  Activity,
  Clock,
  GitBranch,
  Shield,
  Layers,
  RefreshCw,
  Brain,
  ExternalLink,
  Play,
} from 'lucide-react';

const GITHUB_URL = 'https://github.com/debdevops/servicehub';

const CLOUD_PROVIDERS = [
  {
    id: 'azure',
    name: 'Microsoft Azure',
    shortName: 'Azure',
    product: 'Service Bus',
    tagline: 'Enterprise message broker for .NET, Java & cloud-native apps',
    badge: 'Full Support',
    badgeColor: 'bg-blue-100 text-blue-700 border-blue-200',
    borderColor: 'border-blue-200 hover:border-blue-400',
    bgGradient: 'from-blue-50 to-white',
    iconBg: 'bg-blue-600',
    iconText: 'Az',
    accentColor: 'text-blue-700',
    features: [
      'Queues, Topics & Subscriptions',
      'Dead-Letter Queue forensics',
      'Auto-replay rules engine',
      'AI-powered error clustering',
      'Correlation ID tracing',
      '30-day DLQ history',
    ],
    demoUrl: '/messages?demo=azure',
    demoLabel: '▶ Open Azure Demo (50 messages)',
    demoColor: 'bg-blue-600 hover:bg-blue-700',
    status: 'production',
  },
  {
    id: 'aws',
    name: 'Amazon Web Services',
    shortName: 'AWS',
    product: 'SQS / SNS',
    tagline: 'Highly available queuing and notification for AWS workloads',
    badge: 'Phase 2',
    badgeColor: 'bg-orange-100 text-orange-700 border-orange-200',
    borderColor: 'border-orange-200 hover:border-orange-400',
    bgGradient: 'from-orange-50 to-white',
    iconBg: 'bg-orange-500',
    iconText: 'AWS',
    accentColor: 'text-orange-700',
    features: [
      'Standard & FIFO queues',
      'Dead-Letter Queue browser',
      'ApproximateReceiveCount tracking',
      'SNS topic message tracing',
      'Message attribute filtering',
      'IAM & STS auth support',
    ],
    demoUrl: '/messages?demo=aws',
    demoLabel: '▶ Open AWS Demo (50 messages)',
    demoColor: 'bg-orange-500 hover:bg-orange-600',
    status: 'preview',
  },
  {
    id: 'gcp',
    name: 'Google Cloud Platform',
    shortName: 'GCP',
    product: 'Pub/Sub',
    tagline: 'Global, scalable messaging for event-driven GCP architectures',
    badge: 'Phase 2',
    badgeColor: 'bg-green-100 text-green-700 border-green-200',
    borderColor: 'border-green-200 hover:border-green-400',
    bgGradient: 'from-green-50 to-white',
    iconBg: 'bg-green-600',
    iconText: 'GCP',
    accentColor: 'text-green-700',
    features: [
      'Topics & Subscriptions browser',
      'Dead-Letter Topic drill-down',
      'Ack deadline monitoring',
      'Pub/Sub Seek for replay',
      'FHIR & HL7 schema analysis',
      'Workload Identity support',
    ],
    demoUrl: '/messages?demo=gcp',
    demoLabel: '▶ Open GCP Demo (50 messages)',
    demoColor: 'bg-green-600 hover:bg-green-700',
    status: 'preview',
  },
];

const CORE_FEATURES = [
  {
    icon: Search,
    title: 'Forensic Message Browser',
    description:
      'Browse Active and Dead-Letter queues with full message bodies, headers, and properties. Syntax-highlighted JSON/XML. Virtualized grid handles thousands of messages without lag.',
    badge: 'Core',
  },
  {
    icon: Zap,
    title: 'Auto-Replay Rules Engine',
    description:
      'Define smart rules to detect and replay failed DLQ messages automatically. Built-in templates for timeouts, throttle errors, and TTL expiry — with rate limiting and circuit-breaker safety.',
    badge: 'Automation',
  },
  {
    icon: Brain,
    title: 'Client-Side AI Analysis',
    description:
      'Pattern-detection engine groups DLQ failures by error type, calculates confidence scores, and surfaces the highest-impact clusters — entirely in your browser. Zero data sent anywhere.',
    badge: 'AI',
  },
  {
    icon: BarChart3,
    title: 'DLQ Intelligence & 30-Day History',
    description:
      'Persistent SQLite-backed history of every DLQ event. Trend charts, auto-categorisation into 5 failure types, replay-safety ratings, and CSV/JSON export for post-mortem reports.',
    badge: 'Analytics',
  },
  {
    icon: GitBranch,
    title: 'Correlation Explorer',
    description:
      "Paste any Correlation ID and instantly trace a message's full journey across every queue, topic, subscription, and namespace — invaluable for debugging distributed workflows.",
    badge: 'Tracing',
  },
  {
    icon: Clock,
    title: 'Scheduled Message Manager',
    description:
      'View all future-scheduled messages across your namespaces. Reschedule or cancel individual messages from the UI — no SDK scripts required.',
    badge: 'Management',
  },
  {
    icon: Layers,
    title: 'Multi-Cloud Namespaces',
    description:
      'Connect to Azure Service Bus, AWS SQS, and GCP Pub/Sub simultaneously. DEV, UAT, PROD across all three clouds — visible in the sidebar with live message counts.',
    badge: 'Multi-Cloud',
  },
  {
    icon: Activity,
    title: 'Live System Health Monitor',
    description:
      'Real-time runtime metrics: uptime, memory usage, thread count, GC generations, and full .NET environment information. Know your deployment is healthy at a glance.',
    badge: 'Ops',
  },
  {
    icon: Lock,
    title: 'Enterprise Security',
    description:
      'AES-GCM encrypted credentials at rest. HMAC SPA token auth. Read-only PeekMessagesAsync — messages are never consumed or removed. OWASP-compliant.',
    badge: 'Security',
  },
  {
    icon: RefreshCw,
    title: 'Real-Time Auto-Refresh',
    description:
      'Message lists refresh every 7 seconds automatically. Live incident visibility without manual page reloads — just leave it open during production incidents.',
    badge: 'Live',
  },
];

const COMPARISON_ROWS = [
  { feature: 'View Full Message Body', azure: '\u2717 Count only', aws: '\u2717 Count only', gcp: '\u2717 Metadata only', hub: '\u2713 Full body + syntax highlighting' },
  { feature: 'Real-Time Full-Text Search', azure: '\u2717', aws: '\u2717', gcp: '\u2717', hub: '\u2713 Instant, cross-field' },
  { feature: 'DLQ / Dead-Letter Investigation', azure: '\u2717 One at a time', aws: '\u2717 Console only', gcp: '\u2717 Basic', hub: '\u2713 Batch analysis + AI patterns' },
  { feature: 'AI Pattern Detection', azure: '\u2717', aws: '\u2717', gcp: '\u2717', hub: '\u2713 Client-side, zero data sent' },
  { feature: 'Replay Messages from DLQ', azure: '\u2717 Not available', aws: '\u2717 Manual only', gcp: '\u2717 Not available', hub: '\u2713 1-click or automated rules' },
  { feature: '30-Day DLQ History & Trends', azure: '\u2717', aws: '\u2717', gcp: '\u2717', hub: '\u2713 Full history + charts' },
  { feature: 'Correlation ID Tracing', azure: '\u2717', aws: '\u2717', gcp: '\u2717', hub: '\u2713 Cross-queue journey' },
  { feature: 'Multi-Cloud Management', azure: '\u2014', aws: '\u2014', gcp: '\u2014', hub: '\u2713 Azure + AWS + GCP in one tab' },
];

export function WelcomePage() {
  const navigate = useNavigate();

  return (
    <div className="flex flex-col min-h-screen bg-gradient-to-b from-slate-950 via-slate-900 to-slate-950 font-sans">

      {/* Header */}
      <header className="fixed top-0 w-full bg-slate-900/90 backdrop-blur-md border-b border-white/10 z-50">
        <div className="max-w-7xl mx-auto px-6 py-3 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="w-9 h-9 bg-gradient-to-br from-blue-500 to-purple-600 rounded-xl flex items-center justify-center shadow-lg">
              <Search className="w-5 h-5 text-white" />
            </div>
            <div>
              <span className="font-bold text-white text-lg">ServiceHub</span>
              <span className="ml-2 text-xs text-blue-400 font-medium bg-blue-500/10 border border-blue-500/20 px-2 py-0.5 rounded-full">v3.1.0</span>
            </div>
          </div>
          <nav className="hidden md:flex items-center gap-6 text-sm">
            <a href="#providers" className="text-white/60 hover:text-white transition-colors font-medium">Clouds</a>
            <a href="#features" className="text-white/60 hover:text-white transition-colors font-medium">Features</a>
            <a href="#compare" className="text-white/60 hover:text-white transition-colors font-medium">Compare</a>
            <a href={GITHUB_URL} target="_blank" rel="noopener noreferrer" className="flex items-center gap-1.5 text-white/60 hover:text-white transition-colors font-medium">
              <Github className="w-4 h-4" />
              GitHub
            </a>
          </nav>
          <Link
            to="/connect"
            className="inline-flex items-center gap-2 px-5 py-2 bg-gradient-to-r from-blue-500 to-purple-600 text-white text-sm font-semibold rounded-lg hover:from-blue-600 hover:to-purple-700 transition-all shadow-lg"
            aria-label="Open ServiceHub application"
          >
            Open App
            <ArrowRight className="w-3.5 h-3.5" />
          </Link>
        </div>
      </header>

      {/* Hero */}
      <section className="pt-36 pb-20 px-6 relative overflow-hidden">
        <div className="absolute top-20 left-1/4 w-96 h-96 bg-blue-600/20 rounded-full blur-3xl pointer-events-none" />
        <div className="absolute top-40 right-1/4 w-96 h-96 bg-purple-600/20 rounded-full blur-3xl pointer-events-none" />
        <div className="max-w-5xl mx-auto text-center relative">
          <div className="inline-flex items-center gap-2 mb-6 px-4 py-2 bg-white/5 border border-white/20 text-white/80 rounded-full text-sm font-semibold">
            <span className="w-2 h-2 bg-green-400 rounded-full animate-pulse" />
            Multi-Cloud · v3.1.0 Live · Azure + AWS + GCP
          </div>
          <h1 className="text-5xl md:text-6xl lg:text-7xl font-extrabold text-white mb-6 leading-[1.08] tracking-tight">
            <span className="text-transparent bg-clip-text bg-gradient-to-r from-blue-400 via-purple-400 to-emerald-400">One Platform.</span>
            <br />
            <span>Three Clouds.</span>
            <br />
            <span className="text-white/70 text-4xl md:text-5xl">Zero Compromise.</span>
          </h1>
          <p className="text-xl md:text-2xl text-white/70 mb-4 max-w-3xl mx-auto leading-relaxed font-light">
            The forensic debugger for message queues — Azure Service Bus, AWS SQS, and GCP Pub/Sub.
            Full message bodies, DLQ patterns, AI insights, and automated replay in one tab.
          </p>
          <p className="text-base text-white/50 mb-12 max-w-2xl mx-auto">
            Built for DevOps, Platform, and SRE engineers who need real answers during production incidents.
          </p>
          <div className="flex flex-col sm:flex-row items-center justify-center gap-4 mb-14">
            <Link
              to="/connect"
              className="inline-flex items-center gap-2.5 px-8 py-4 bg-gradient-to-r from-blue-500 to-purple-600 text-white text-base font-bold rounded-xl hover:from-blue-600 hover:to-purple-700 transition-all shadow-xl hover:-translate-y-0.5"
            >
              Open ServiceHub
              <ArrowRight className="w-5 h-5" />
            </Link>
            <button
              onClick={() => navigate('/messages?demo=azure')}
              className="inline-flex items-center gap-2.5 px-8 py-4 bg-white/10 text-white text-base font-bold rounded-xl border border-white/20 hover:bg-white/20 transition-all"
            >
              <Play className="w-5 h-5 text-amber-300 fill-current" />
              Live Demo
            </button>
            <a
              href={GITHUB_URL}
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-2.5 px-8 py-4 bg-white/5 text-white/80 text-base font-bold rounded-xl border border-white/15 hover:bg-white/10 transition-all"
            >
              <Github className="w-5 h-5" />
              GitHub
            </a>
          </div>
          <div className="flex flex-wrap items-center justify-center gap-x-8 gap-y-3 text-sm text-white/50">
            {[
              { icon: '\u2705', label: '100% Open Source (MIT)' },
              { icon: '\uD83D\uDD10', label: 'AES-GCM Encrypted Credentials' },
              { icon: '\uD83D\uDC41\uFE0F', label: 'Read-Only by Default' },
              { icon: '\uD83E\uDDE0', label: 'AI Runs Entirely In-Browser' },
              { icon: '\u2601\uFE0F', label: 'Self-Hostable on Any Cloud' },
            ].map(({ icon, label }) => (
              <span key={label} className="flex items-center gap-1.5 font-medium">
                <span>{icon}</span> {label}
              </span>
            ))}
          </div>
        </div>
      </section>

      {/* Cloud Provider Cards */}
      <section id="providers" className="py-20 px-6 bg-white">
        <div className="max-w-6xl mx-auto">
          <div className="text-center mb-14">
            <h2 className="text-3xl font-bold text-gray-900 mb-4">Choose Your Cloud. Keep Your Workflow.</h2>
            <p className="text-gray-500 max-w-2xl mx-auto">
              ServiceHub speaks Azure Service Bus natively and extends to AWS SQS and GCP Pub/Sub.
              Same interface, same debugging power — regardless of where your messages live.
            </p>
          </div>
          <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
            {CLOUD_PROVIDERS.map((provider) => (
              <div
                key={provider.id}
                className={`rounded-2xl border-2 bg-gradient-to-b ${provider.bgGradient} ${provider.borderColor} p-6 flex flex-col shadow-sm hover:shadow-lg transition-all duration-200`}
              >
                <div className="flex items-start justify-between mb-5">
                  <div className="flex items-center gap-3">
                    <div className={`w-12 h-12 ${provider.iconBg} rounded-xl flex items-center justify-center shadow-md`}>
                      <span className="text-white text-xs font-black tracking-tight">{provider.iconText}</span>
                    </div>
                    <div>
                      <div className="font-bold text-gray-900">{provider.shortName}</div>
                      <div className="text-xs text-gray-500">{provider.product}</div>
                    </div>
                  </div>
                  <span className={`text-[10px] font-bold px-2 py-1 rounded-full border ${provider.badgeColor}`}>
                    {provider.badge}
                  </span>
                </div>
                <p className="text-sm text-gray-600 mb-5 leading-relaxed">{provider.tagline}</p>
                <ul className="space-y-2 mb-6 flex-1">
                  {provider.features.map((f) => (
                    <li key={f} className="flex items-start gap-2 text-sm text-gray-700">
                      <span className={`mt-0.5 shrink-0 ${provider.accentColor}`}>\u2713</span>
                      {f}
                    </li>
                  ))}
                </ul>
                {provider.status === 'preview' && (
                  <div className="mb-4 p-2.5 rounded-lg bg-white/70 border border-dashed border-gray-300 text-xs text-gray-500">
                    <strong className={provider.accentColor}>Phase 2 provider.</strong> Demo mode available now. Full live browsing ships in Phase 2.
                  </div>
                )}
                <button
                  onClick={() => navigate(provider.demoUrl)}
                  className={`w-full flex items-center justify-center gap-2 px-4 py-2.5 ${provider.demoColor} text-white text-sm font-semibold rounded-xl transition-colors shadow-sm`}
                >
                  <Play className="w-4 h-4 fill-current" />
                  {provider.demoLabel}
                </button>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* Stats Bar */}
      <section className="py-12 px-6 bg-gradient-to-r from-blue-600 via-purple-600 to-emerald-600">
        <div className="max-w-5xl mx-auto grid grid-cols-2 md:grid-cols-4 gap-6 text-center text-white">
          {[
            { value: '3', label: 'Cloud Providers' },
            { value: '30-day', label: 'DLQ History' },
            { value: '100%', label: 'Client-Side AI' },
            { value: '< 60s', label: 'Setup Time' },
          ].map(({ value, label }) => (
            <div key={label}>
              <div className="text-3xl md:text-4xl font-extrabold mb-1">{value}</div>
              <div className="text-sm text-white/80 font-medium">{label}</div>
            </div>
          ))}
        </div>
      </section>

      {/* How It Works */}
      <section className="py-20 px-6 bg-white">
        <div className="max-w-5xl mx-auto">
          <h2 className="text-3xl font-bold text-gray-900 text-center mb-4">Up and Running in Under 60 Seconds</h2>
          <p className="text-gray-500 text-center mb-14 max-w-xl mx-auto">
            No Docker. No cloud provisioning. No credit card. Connect any cloud provider and start debugging.
          </p>
          <div className="grid grid-cols-1 md:grid-cols-3 gap-8">
            {[
              { step: '01', title: 'Choose Your Cloud', desc: 'Select Azure Service Bus, AWS SQS, or GCP Pub/Sub on the Connect page. Each provider has a dedicated, secure credential form.', icon: '\u2601\uFE0F' },
              { step: '02', title: 'Browse & Analyse', desc: 'Instantly see all queues/topics, full message bodies, DLQ messages, AI pattern clusters, and 30-day failure trends.', icon: '\uD83D\uDD0D' },
              { step: '03', title: 'Replay & Recover', desc: 'Fix the root cause, then replay failed messages manually or set auto-replay rules to do it automatically.', icon: '\u26A1' },
            ].map(({ step, title, desc, icon }) => (
              <div key={step} className="relative p-6 bg-white rounded-xl border border-gray-200 shadow-sm hover:shadow-md hover:border-blue-200 transition-all group">
                <div className="absolute -top-3 -left-3 w-8 h-8 bg-gradient-to-br from-blue-500 to-purple-600 text-white text-xs font-bold rounded-full flex items-center justify-center shadow">
                  {step}
                </div>
                <div className="text-4xl mb-4 group-hover:scale-110 transition-transform">{icon}</div>
                <h3 className="text-lg font-bold text-gray-900 mb-2">{title}</h3>
                <p className="text-gray-600 text-sm leading-relaxed">{desc}</p>
              </div>
            ))}
          </div>
          <div className="mt-10 p-4 bg-gray-900 rounded-xl text-center">
            <code className="text-green-400 text-sm font-mono">
              {'git clone ' + GITHUB_URL + '.git && cd servicehub && ./run.sh'}
            </code>
          </div>
        </div>
      </section>

      {/* All Features */}
      <section id="features" className="py-20 px-6 bg-gray-50/60">
        <div className="max-w-6xl mx-auto">
          <h2 className="text-3xl font-bold text-gray-900 text-center mb-4">Every Feature You Need — Across All Three Clouds</h2>
          <p className="text-gray-500 text-center mb-14 max-w-2xl mx-auto">
            ServiceHub covers the full message lifecycle — from real-time browsing to automated recovery and deep forensic analysis.
          </p>
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
            {CORE_FEATURES.map(({ icon: Icon, title, description, badge }) => (
              <div key={title} className="p-6 bg-white rounded-xl border border-gray-200 shadow-sm hover:shadow-md hover:border-blue-300 transition-all">
                <div className="flex items-start justify-between mb-4">
                  <div className="w-11 h-11 bg-gradient-to-br from-blue-50 to-purple-50 rounded-lg flex items-center justify-center">
                    <Icon className="w-6 h-6 text-blue-600" />
                  </div>
                  <span className="text-xs font-semibold text-blue-600 bg-blue-50 border border-blue-100 px-2 py-0.5 rounded-full">{badge}</span>
                </div>
                <h3 className="text-base font-bold text-gray-900 mb-2">{title}</h3>
                <p className="text-sm text-gray-600 leading-relaxed">{description}</p>
              </div>
            ))}
          </div>
          <div className="mt-14 pt-10 border-t border-gray-200">
            <p className="text-center text-sm font-semibold text-gray-500 mb-6 uppercase tracking-wider">All Included in v3.1.0</p>
            <div className="flex flex-wrap justify-center gap-3">
              {[
                '\u2601\uFE0F Azure Service Bus', '\uD83D\uDFE0 AWS SQS / SNS', '\uD83D\uDFE2 GCP Pub/Sub',
                '\uD83D\uDD0D DLQ Intelligence', '\u26A1 Auto-Replay Rules', '\uD83E\uDD16 AI Pattern Analysis',
                '\uD83D\uDD17 Correlation Explorer', '\u23F1\uFE0F Scheduled Messages', '\uD83D\uDC9A Health Monitor',
                '\uD83D\uDCCA 30-Day Trends', '\uD83D\uDD10 AES-GCM Security', '\uD83D\uDCE4 CSV/JSON Export',
                '\uD83D\uDD0E Full-Text Search', '\u267B\uFE0F Auto-Refresh Live',
              ].map((chip) => (
                <span key={chip} className="px-4 py-2 bg-white border border-gray-200 rounded-full text-sm text-gray-700 font-medium shadow-sm hover:border-blue-300 hover:text-blue-700 transition-colors">
                  {chip}
                </span>
              ))}
            </div>
          </div>
        </div>
      </section>

      {/* Comparison Table */}
      <section id="compare" className="py-20 px-6 bg-white">
        <div className="max-w-6xl mx-auto">
          <h2 className="text-3xl font-bold text-gray-900 text-center mb-4">ServiceHub vs. Native Cloud Portals</h2>
          <p className="text-gray-500 text-center mb-12 max-w-xl mx-auto">
            Cloud portals are built for provisioning — not debugging. Here's why ServiceHub exists.
          </p>
          <div className="overflow-x-auto rounded-xl border border-gray-200 shadow-sm">
            <table className="w-full text-sm">
              <thead>
                <tr className="bg-gray-50 border-b border-gray-200">
                  <th className="px-4 py-4 text-left font-bold text-gray-800">Capability</th>
                  <th className="px-3 py-4 text-center font-bold text-blue-500">
                    <span className="inline-flex items-center gap-1.5"><span className="w-3 h-3 rounded-sm bg-blue-600 inline-block" />Azure Portal</span>
                  </th>
                  <th className="px-3 py-4 text-center font-bold text-orange-500">
                    <span className="inline-flex items-center gap-1.5"><span className="w-3 h-3 rounded-sm bg-orange-500 inline-block" />AWS Console</span>
                  </th>
                  <th className="px-3 py-4 text-center font-bold text-green-600">
                    <span className="inline-flex items-center gap-1.5"><span className="w-3 h-3 rounded-sm bg-green-600 inline-block" />GCP Console</span>
                  </th>
                  <th className="px-3 py-4 text-center font-bold text-purple-700 bg-purple-50/50">ServiceHub</th>
                </tr>
              </thead>
              <tbody>
                {COMPARISON_ROWS.map(({ feature, azure, aws, gcp, hub }, i) => (
                  <tr key={feature} className={`${i % 2 === 0 ? 'bg-white' : 'bg-gray-50/40'} border-b border-gray-100 hover:bg-blue-50/20 transition-colors`}>
                    <td className="px-4 py-3.5 text-gray-800 font-medium">{feature}</td>
                    <td className="px-3 py-3.5 text-center text-xs text-red-500 font-medium">{azure}</td>
                    <td className="px-3 py-3.5 text-center text-xs text-red-500 font-medium">{aws}</td>
                    <td className="px-3 py-3.5 text-center text-xs text-red-500 font-medium">{gcp}</td>
                    <td className="px-3 py-3.5 text-center text-xs text-green-700 font-semibold bg-purple-50/30">{hub}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </section>

      {/* Security */}
      <section className="py-20 px-6 bg-gray-50/60">
        <div className="max-w-5xl mx-auto">
          <h2 className="text-3xl font-bold text-gray-900 text-center mb-4">Enterprise-Grade Security. Developer-Friendly.</h2>
          <p className="text-gray-500 text-center mb-14 max-w-2xl mx-auto">
            Your cloud credentials are sensitive. ServiceHub was built so a debugging tool never becomes a security liability.
          </p>
          <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
            {[
              { icon: '\uD83D\uDD10', title: 'AES-GCM Encryption', body: 'Connection strings, access keys, and service account JSONs are encrypted at rest using AES-256-GCM. The key lives only in your server config.' },
              { icon: '\uD83D\uDC41\uFE0F', title: 'Read-Only by Default', body: 'All message reading uses PeekMessagesAsync / GetMessages. ServiceHub never consumes, removes, or alters any messages.' },
              { icon: '\uD83E\uDDE0', title: 'Zero-Data-Exfiltration AI', body: 'AI pattern analysis is pure TypeScript running in your browser tab. Message content never leaves your environment.' },
              { icon: '\uD83D\uDEE1\uFE0F', title: 'OWASP Compliant', body: 'Built against OWASP Top 10 guidelines. Input validation, secure headers, EF Core parameterised queries.' },
              { icon: '\uD83D\uDD11', title: 'Minimum-Privilege Design', body: 'Azure: Listen-only SAS. AWS: sqs:ReceiveMessage + GetQueueAttributes. GCP: roles/pubsub.subscriber.' },
              { icon: '\uD83C\uDFE0', title: 'Self-Host for Sovereignty', body: 'Deploy on Azure, AWS VPC, GCP project, on-premises VM, Docker, or Kubernetes. You own the infrastructure.' },
            ].map(({ icon, title, body }) => (
              <div key={title} className="p-5 bg-white rounded-xl border border-gray-200 shadow-sm hover:shadow-md transition-all">
                <div className="text-2xl mb-3">{icon}</div>
                <h4 className="font-bold text-gray-900 mb-2">{title}</h4>
                <p className="text-sm text-gray-600 leading-relaxed">{body}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* Why Choose */}
      <section className="py-20 px-6 bg-white">
        <div className="max-w-5xl mx-auto">
          <h2 className="text-3xl font-bold text-gray-900 text-center mb-12">Why Teams Choose ServiceHub</h2>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-12">
            <div>
              <h3 className="text-xl font-bold text-gray-900 mb-6 flex items-center gap-2">
                <span className="text-2xl">\u2601\uFE0F</span> Multi-Cloud Without the Complexity
              </h3>
              <ul className="space-y-4">
                {[
                  'Azure, AWS, GCP \u2014 one UI, one workflow',
                  'See full message body \u2014 not just counts or offsets',
                  'Search 10,000 messages in under a second',
                  'Auto-replay failed DLQ messages with configurable rules',
                  'AI clusters messages by error type automatically',
                  'Trace any Correlation ID across all queues & topics',
                ].map((item) => (
                  <li key={item} className="flex items-start gap-3">
                    <Check className="w-5 h-5 text-green-500 shrink-0 mt-0.5" />
                    <span className="text-gray-700">{item}</span>
                  </li>
                ))}
              </ul>
            </div>
            <div>
              <h3 className="text-xl font-bold text-gray-900 mb-6 flex items-center gap-2">
                <span className="text-2xl">\uD83E\uDDD1\u200D\uD83D\uDCBB</span> Built for DevOps &amp; SREs
              </h3>
              <ul className="space-y-4">
                {[
                  'Works on any OS \u2014 macOS, Linux, Windows',
                  'One-command setup: clone \u2192 run \u2192 connect',
                  'Multi-environment: DEV, UAT, PROD in sidebar',
                  'Enterprise AES-GCM encryption out of the box',
                  'Deploy anywhere: Azure, AWS, GCP, Docker, VMs',
                  'Open source, MIT licensed \u2014 no vendor lock-in',
                ].map((item) => (
                  <li key={item} className="flex items-start gap-3">
                    <Check className="w-5 h-5 text-green-500 shrink-0 mt-0.5" />
                    <span className="text-gray-700">{item}</span>
                  </li>
                ))}
              </ul>
            </div>
          </div>
        </div>
      </section>

      {/* Demo Trio */}
      <section className="py-20 px-6 bg-gray-50/60">
        <div className="max-w-5xl mx-auto text-center">
          <h2 className="text-3xl font-bold text-gray-900 mb-4">Try a Live Demo — No Credentials Needed</h2>
          <p className="text-gray-500 mb-10 max-w-xl mx-auto">
            Each demo has 50 production-realistic messages, DLQ scenarios, and AI root-cause analysis. Pick your cloud.
          </p>
          <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
            {CLOUD_PROVIDERS.map((p) => (
              <button
                key={p.id}
                onClick={() => navigate(p.demoUrl)}
                className={`group p-6 rounded-2xl border-2 bg-gradient-to-b ${p.bgGradient} ${p.borderColor} text-left hover:shadow-lg transition-all duration-200`}
              >
                <div className={`w-12 h-12 ${p.iconBg} rounded-xl flex items-center justify-center mb-4 shadow-md group-hover:scale-110 transition-transform`}>
                  <span className="text-white text-xs font-black">{p.iconText}</span>
                </div>
                <div className="font-bold text-gray-900 mb-1">{p.shortName} Demo</div>
                <div className="text-xs text-gray-500 mb-3">{p.product}</div>
                <div className="text-sm text-gray-600 mb-4">50 realistic messages \u00b7 DLQ scenarios \u00b7 AI analysis</div>
                <span className={`inline-flex items-center gap-1.5 text-xs font-semibold ${p.demoColor} text-white px-3 py-1.5 rounded-lg`}>
                  <Play className="w-3 h-3 fill-current" />
                  {p.demoLabel}
                </span>
              </button>
            ))}
          </div>
        </div>
      </section>

      {/* Auth Note */}
      <section className="px-6 py-12 bg-white">
        <div className="max-w-4xl mx-auto">
          <div className="flex items-start gap-4 p-5 bg-blue-50 border border-blue-200 rounded-xl shadow-sm">
            <div className="flex-shrink-0 w-10 h-10 bg-blue-100 rounded-lg flex items-center justify-center mt-0.5">
              <Shield className="w-5 h-5 text-blue-600" />
            </div>
            <div>
              <p className="text-sm font-bold text-blue-900 mb-1">
                \uD83D\uDD12 Hosted App Authentication via Microsoft Entra ID (Azure AD)
              </p>
              <p className="text-sm text-blue-800 leading-relaxed">
                The hosted application uses <strong>Microsoft's own login page</strong> for access control only.
                ServiceHub does <strong>not store your personal information, credentials, or any user data</strong>.
                We comply with GDPR. For full data sovereignty, self-host on your own infrastructure.
              </p>
            </div>
          </div>
        </div>
      </section>

      {/* Final CTA */}
      <section className="py-24 px-6 bg-gradient-to-b from-slate-900 to-slate-950">
        <div className="max-w-3xl mx-auto text-center">
          <div className="bg-gradient-to-br from-blue-600 via-purple-600 to-emerald-600 rounded-2xl p-12 text-white shadow-2xl relative overflow-hidden">
            <div className="absolute top-0 right-0 w-48 h-48 bg-white/5 rounded-full -translate-y-1/2 translate-x-1/2" />
            <div className="absolute bottom-0 left-0 w-32 h-32 bg-white/5 rounded-full translate-y-1/2 -translate-x-1/2" />
            <div className="relative">
              <h2 className="text-3xl md:text-4xl font-extrabold mb-4">Stop Guessing. Start Debugging.</h2>
              <p className="text-lg mb-2 text-white/90 leading-relaxed">
                Your dead-letter messages are telling a story. ServiceHub helps you read it \u2014 on any cloud.
              </p>
              <p className="text-sm text-white/70 mb-10">No credit card. No install required. Connect in under 60 seconds.</p>
              <div className="flex flex-col sm:flex-row items-center justify-center gap-4">
                <Link
                  to="/connect"
                  className="inline-flex items-center gap-2.5 px-8 py-4 bg-white text-purple-700 font-bold text-base rounded-xl hover:bg-gray-100 transition-all shadow-lg hover:-translate-y-0.5"
                >
                  Open ServiceHub
                  <ArrowRight className="w-5 h-5" />
                </Link>
                <a
                  href={GITHUB_URL}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="inline-flex items-center gap-2.5 px-8 py-4 bg-white/15 text-white font-bold text-base rounded-xl border-2 border-white/40 hover:bg-white/25 transition-all"
                >
                  <Github className="w-5 h-5" />
                  Star on GitHub
                </a>
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* Footer */}
      <footer className="border-t border-white/10 py-12 px-6 bg-slate-950">
        <div className="max-w-7xl mx-auto">
          <div className="grid grid-cols-1 md:grid-cols-4 gap-8 mb-10">
            <div>
              <div className="flex items-center gap-2 mb-4">
                <div className="w-8 h-8 bg-gradient-to-br from-blue-500 to-purple-600 rounded-lg flex items-center justify-center">
                  <Search className="w-4 h-4 text-white" />
                </div>
                <span className="font-bold text-white">ServiceHub</span>
              </div>
              <p className="text-xs text-white/40 leading-relaxed">
                The multi-cloud message queue debugger. Azure Service Bus \u00b7 AWS SQS \u00b7 GCP Pub/Sub.
              </p>
            </div>
            <div>
              <h4 className="font-semibold text-white/80 mb-4 text-sm">Product</h4>
              <ul className="space-y-2.5 text-sm text-white/50">
                <li><a href={`${GITHUB_URL}/blob/main/README.md`} target="_blank" rel="noopener noreferrer" className="hover:text-white flex items-center gap-1.5 transition-colors">Documentation <ExternalLink className="w-3 h-3" /></a></li>
                <li><a href={`${GITHUB_URL}/tree/main/self-hosting`} target="_blank" rel="noopener noreferrer" className="hover:text-white flex items-center gap-1.5 transition-colors">Self-Hosting Guide <ExternalLink className="w-3 h-3" /></a></li>
                <li><Link to="/security" className="hover:text-white transition-colors">Security &amp; Privacy</Link></li>
                <li><a href={`${GITHUB_URL}/blob/main/CHANGELOG.md`} target="_blank" rel="noopener noreferrer" className="hover:text-white flex items-center gap-1.5 transition-colors">Changelog <ExternalLink className="w-3 h-3" /></a></li>
              </ul>
            </div>
            <div>
              <h4 className="font-semibold text-white/80 mb-4 text-sm">Community</h4>
              <ul className="space-y-2.5 text-sm text-white/50">
                <li><a href={GITHUB_URL} target="_blank" rel="noopener noreferrer" className="hover:text-white flex items-center gap-1.5 transition-colors"><Github className="w-4 h-4" /> GitHub Repository</a></li>
                <li><a href={`${GITHUB_URL}/issues`} target="_blank" rel="noopener noreferrer" className="hover:text-white transition-colors">Report an Issue</a></li>
                <li><a href={`${GITHUB_URL}/discussions`} target="_blank" rel="noopener noreferrer" className="hover:text-white transition-colors">Discussions</a></li>
              </ul>
            </div>
            <div>
              <h4 className="font-semibold text-white/80 mb-4 text-sm">Legal</h4>
              <ul className="space-y-2.5 text-sm text-white/50">
                <li><a href={`${GITHUB_URL}/blob/main/SECURITY.md`} target="_blank" rel="noopener noreferrer" className="hover:text-white transition-colors">Security Policy</a></li>
                <li><a href={`${GITHUB_URL}/blob/main/LICENSE`} target="_blank" rel="noopener noreferrer" className="hover:text-white transition-colors">MIT License</a></li>
                <li><Link to="/security" className="hover:text-white transition-colors">Privacy Notice</Link></li>
              </ul>
            </div>
          </div>
          <div className="border-t border-white/10 pt-8 flex flex-col md:flex-row items-center justify-between gap-4 text-sm text-white/40">
            <p>
              ServiceHub is open source, free to use, and MIT licensed. Made with \u2764\uFE0F by{' '}
              <a href="https://github.com/debdevops" className="text-blue-400 hover:underline font-medium">Debasis</a>
            </p>
            <p>© 2026 ServiceHub · All rights reserved</p>
          </div>
        </div>
      </footer>
    </div>
  );
}
