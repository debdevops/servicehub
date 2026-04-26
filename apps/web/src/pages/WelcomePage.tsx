import { Link } from 'react-router-dom';
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
} from 'lucide-react';

const LIVE_APP_URL = 'https://app-servicehub-prod.azurewebsites.net/';
const GITHUB_URL = 'https://github.com/debdevops/servicehub';

export function WelcomePage() {
  const coreFeatures = [
    {
      icon: Search,
      title: 'Forensic Message Browser',
      description:
        'Browse Active and Dead-Letter queues with full message bodies, headers, and properties. Syntax-highlighted JSON/XML. Virtualized grid handles thousands of messages without a lag.',
      badge: 'Core',
    },
    {
      icon: Zap,
      title: 'Auto-Replay Rules Engine',
      description:
        'Define smart rules to detect and replay failed DLQ messages automatically. Built-in templates for timeouts, throttle errors, and TTL expiry — with rate limiting and circuit-breaker safety controls.',
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
        'Paste any Correlation ID and instantly trace a message\'s full journey across every queue, topic, subscription, and namespace — invaluable for debugging distributed workflows.',
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
      title: 'Multi-Namespace Support',
      description:
        'Connect to multiple Azure Service Bus namespaces simultaneously — DEV, UAT, PROD, all visible in the sidebar with live colour-coded message counts.',
      badge: 'Enterprise',
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
        'AES-GCM encrypted connection strings at rest. HMAC SPA token auth. Read-only PeekMessagesAsync — messages are never consumed or removed. OWASP-compliant.',
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

  const comparisonRows = [
    { feature: 'View Full Message Body & Content', portal: 'Count only', hub: 'Full body + syntax highlighting' },
    { feature: 'Real-Time Full-Text Search', portal: 'Not available', hub: 'Instant, cross-field search' },
    { feature: 'Dead-Letter Queue Investigation', portal: 'One message at a time', hub: 'Batch analysis + AI patterns' },
    { feature: 'AI Pattern Detection', portal: 'Not available', hub: 'Client-side clustering, zero data sent' },
    { feature: 'Replay Messages from DLQ', portal: 'Not available', hub: 'One-click or automated rules' },
    { feature: '30-Day DLQ Trend History', portal: 'Not available', hub: 'Full history + trend charts' },
    { feature: 'Correlation ID Tracing', portal: 'Not available', hub: 'Cross-queue journey explorer' },
    { feature: 'Multi-Namespace Management', portal: 'Portal-per-namespace', hub: 'All namespaces simultaneously' },
    { feature: 'Scheduled Message Management', portal: 'Not available', hub: 'View, reschedule & cancel' },
    { feature: 'Auto-Replay Rules Engine', portal: 'Not available', hub: 'Smart matching + safety controls' },
    { feature: 'Live Health & Metrics Dashboard', portal: 'Basic only', hub: 'Full runtime + GC metrics' },
  ];

  const useCases = [
    {
      emoji: '🚨',
      title: 'Production Incident — 2 AM',
      story:
        '10,000 orders stuck in DLQ. Azure Portal shows counts, nothing else. With ServiceHub: search all messages in seconds, AI detects 3 error clusters, auto-replay rule recovers 8,000 in minutes.',
      saving: '~6 hours saved',
      color: 'amber',
    },
    {
      emoji: '🔍',
      title: 'Post-Mortem Root-Cause Analysis',
      story:
        'DLQ Intelligence stores 30 days of failure history. Graph trends, categorise failures (Transient, MaxDelivery, Expired, DataQuality), extract patterns, and export CSV for stakeholder reports.',
      saving: 'Data-driven insights',
      color: 'blue',
    },
    {
      emoji: '🎯',
      title: 'Hands-Free Automated Recovery',
      story:
        'Deploy auto-replay rules that watch for transient errors and replay automatically. Templates cover throttling, timeout, and TTL expiry. Set once — forget it.',
      saving: 'Zero manual intervention',
      color: 'green',
    },
    {
      emoji: '🔗',
      title: 'Distributed Message Tracing',
      story:
        'Trace a Correlation ID across all queues, topics, and subscriptions to find exactly where a payment, order, or notification broke in a multi-hop workflow.',
      saving: '30 min → 30 seconds',
      color: 'sky',
    },
  ];

  const colorMap: Record<string, string> = {
    amber: 'from-amber-50 to-white border-amber-200',
    blue: 'from-blue-50 to-white border-blue-200',
    green: 'from-green-50 to-white border-green-200',
    sky: 'from-sky-50 to-white border-sky-200',
  };
  const savingColorMap: Record<string, string> = {
    amber: 'text-amber-700',
    blue: 'text-blue-700',
    green: 'text-green-700',
    sky: 'text-sky-700',
  };

  return (
    <div className="flex flex-col min-h-screen bg-gradient-to-b from-white via-primary-50/20 to-white font-sans">

      {/* ─── Fixed Header ────────────────────────────────────────────────── */}
      <header className="fixed top-0 w-full bg-white/90 backdrop-blur-md border-b border-gray-200 z-50 shadow-sm">
        <div className="max-w-7xl mx-auto px-6 py-3 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="w-9 h-9 bg-gradient-to-br from-primary-600 to-primary-700 rounded-xl flex items-center justify-center shadow-sm">
              <Search className="w-5 h-5 text-white" />
            </div>
            <div>
              <span className="font-bold text-gray-900 text-lg">ServiceHub</span>
              <span className="ml-2 text-xs text-primary-600 font-medium bg-primary-50 px-2 py-0.5 rounded-full">v3.1.0</span>
            </div>
          </div>
          <nav className="hidden md:flex items-center gap-6 text-sm">
            <a href="#features" className="text-gray-600 hover:text-primary-600 transition-colors font-medium">Features</a>
            <a href="#compare" className="text-gray-600 hover:text-primary-600 transition-colors font-medium">Compare</a>
            <a href="#usecases" className="text-gray-600 hover:text-primary-600 transition-colors font-medium">Use Cases</a>
            <a
              href={GITHUB_URL}
              target="_blank"
              rel="noopener noreferrer"
              className="flex items-center gap-1.5 text-gray-600 hover:text-gray-900 transition-colors font-medium"
            >
              <Github className="w-4 h-4" />
              GitHub
            </a>
          </nav>
          <Link
            to="/connect"
            className="inline-flex items-center gap-2 px-5 py-2 bg-primary-600 text-white text-sm font-semibold rounded-lg hover:bg-primary-700 transition-colors shadow-sm"
            aria-label="Open ServiceHub application"
          >
            Open App
            <ArrowRight className="w-3.5 h-3.5" />
          </Link>
        </div>
      </header>

      {/* ─── Hero Section ────────────────────────────────────────────────── */}
      <section className="pt-36 pb-24 px-6">
        <div className="max-w-5xl mx-auto text-center">
          <div className="inline-flex items-center gap-2 mb-6 px-4 py-2 bg-primary-50 border border-primary-200 text-primary-700 rounded-full text-sm font-semibold shadow-sm">
            <span className="w-2 h-2 bg-green-500 rounded-full animate-pulse" />
            Production-Ready · Hosted on Azure · v3.1.0 Live
          </div>

          <h1 className="text-5xl md:text-6xl lg:text-7xl font-extrabold text-gray-900 mb-6 leading-[1.08] tracking-tight">
            <span className="text-transparent bg-clip-text bg-gradient-to-r from-primary-600 via-sky-500 to-blue-600">
              Azure Service Bus
            </span>
            <br />
            <span>Forensic Debugger</span>
          </h1>

          <p className="text-xl md:text-2xl text-gray-600 mb-4 max-w-3xl mx-auto leading-relaxed font-light">
            Everything the Azure Portal can't show you — full message bodies, DLQ patterns,
            auto-replay rules, correlation tracing, and AI-powered insights.
          </p>
          <p className="text-base text-gray-500 mb-10 max-w-2xl mx-auto">
            Built for DevOps, Platform, and SRE engineers who need real answers during production incidents — not just message counts.
          </p>

          {/* CTAs */}
          <div className="flex flex-col sm:flex-row items-center justify-center gap-4 mb-14">
            <Link
              to="/connect"
              className="inline-flex items-center gap-2.5 px-8 py-4 bg-primary-600 text-white text-base font-bold rounded-xl hover:bg-primary-700 transition-all shadow-lg hover:shadow-xl hover:-translate-y-0.5"
              aria-label="Open the ServiceHub application"
            >
              🚀 Open ServiceHub
              <ArrowRight className="w-5 h-5" />
            </Link>
            <a
              href={GITHUB_URL}
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-2.5 px-8 py-4 bg-white text-gray-800 text-base font-bold rounded-xl border-2 border-gray-300 hover:border-gray-400 hover:bg-gray-50 transition-all shadow-sm"
            >
              <Github className="w-5 h-5" />
              View on GitHub
            </a>
            <Link
              to="/connect"
              className="inline-flex items-center gap-2.5 px-8 py-4 bg-white text-primary-600 text-base font-bold rounded-xl border-2 border-primary-300 hover:border-primary-400 hover:bg-primary-50 transition-all shadow-sm"
            >
              💻 Self-Host Locally
            </Link>
          </div>

          {/* Trust bar */}
          <div className="flex flex-wrap items-center justify-center gap-x-8 gap-y-3 text-sm text-gray-500">
            {[
              { icon: '✅', label: '100% Open Source (MIT)' },
              { icon: '🔐', label: 'AES-GCM Encrypted at Rest' },
              { icon: '👁️', label: 'Read-Only by Default' },
              { icon: '🧠', label: 'AI Runs Entirely In-Browser' },
              { icon: '☁️', label: 'Azure-Hosted & Self-Hostable' },
            ].map(({ icon, label }) => (
              <span key={label} className="flex items-center gap-1.5 font-medium">
                <span>{icon}</span> {label}
              </span>
            ))}
          </div>
        </div>
      </section>

      {/* ─── Microsoft Entra Auth Note ───────────────────────────────────── */}
      <section className="px-6 pb-8">
        <div className="max-w-4xl mx-auto">
          <div className="flex items-start gap-4 p-5 bg-blue-50 border border-blue-200 rounded-xl shadow-sm">
            <div className="flex-shrink-0 w-10 h-10 bg-blue-100 rounded-lg flex items-center justify-center mt-0.5">
              <Shield className="w-5 h-5 text-blue-600" />
            </div>
            <div>
              <p className="text-sm font-bold text-blue-900 mb-1">
                🔒 Authentication via Microsoft Entra ID (Azure AD)
              </p>
              <p className="text-sm text-blue-800 leading-relaxed">
                When you open the hosted application, you will be redirected to{' '}
                <strong>Microsoft's own login page</strong> — the same identity provider trusted by
                Fortune 500 companies. This is for{' '}
                <strong>access control only</strong>. ServiceHub does{' '}
                <strong>not store your personal information, credentials, or any user data</strong>.
                We do not have a user database. We comply with GDPR and data-minimisation principles.
                Your Azure Service Bus connection strings are encrypted in your session using AES-GCM —
                they are never transmitted to any third party.
              </p>
            </div>
          </div>
        </div>
      </section>

      {/* ─── Stats Bar ───────────────────────────────────────────────────── */}
      <section className="py-12 px-6 bg-gradient-to-r from-primary-600 to-blue-600">
        <div className="max-w-5xl mx-auto grid grid-cols-2 md:grid-cols-4 gap-6 text-center text-white">
          {[
            { value: '10+', label: 'Core Features' },
            { value: '30-day', label: 'DLQ History' },
            { value: '100%', label: 'Client-Side AI' },
            { value: '30s', label: 'Setup Time' },
          ].map(({ value, label }) => (
            <div key={label}>
              <div className="text-3xl md:text-4xl font-extrabold mb-1">{value}</div>
              <div className="text-sm text-white/80 font-medium">{label}</div>
            </div>
          ))}
        </div>
      </section>

      {/* ─── How It Works ────────────────────────────────────────────────── */}
      <section className="py-20 px-6">
        <div className="max-w-5xl mx-auto">
          <h2 className="text-3xl font-bold text-gray-900 text-center mb-4">
            Up and Running in 30 Seconds
          </h2>
          <p className="text-gray-500 text-center mb-14 max-w-xl mx-auto">
            No Docker. No cloud provisioning. No credit card. Just clone and go.
          </p>

          <div className="grid grid-cols-1 md:grid-cols-3 gap-8">
            {[
              {
                step: '01',
                title: 'Connect Your Namespace',
                desc: 'Paste your Azure Service Bus connection string on the Connect page. Listen-only permission is all you need.',
                icon: '🔌',
              },
              {
                step: '02',
                title: 'Browse & Analyse',
                desc: 'Instantly see all queues, message bodies, DLQ messages, AI pattern clusters, and 30-day trends.',
                icon: '🔍',
              },
              {
                step: '03',
                title: 'Replay & Recover',
                desc: 'Fix the root cause, then replay failed messages manually or set rules to do it automatically.',
                icon: '⚡',
              },
            ].map(({ step, title, desc, icon }) => (
              <div key={step} className="relative p-6 bg-white rounded-xl border border-gray-200 shadow-sm hover:shadow-md hover:border-primary-200 transition-all group">
                <div className="absolute -top-3 -left-3 w-8 h-8 bg-primary-600 text-white text-xs font-bold rounded-full flex items-center justify-center shadow">
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
              git clone {GITHUB_URL}.git &amp;&amp; cd servicehub &amp;&amp; ./run.sh
            </code>
          </div>
        </div>
      </section>

      {/* ─── All Features ────────────────────────────────────────────────── */}
      <section id="features" className="py-20 px-6 bg-gray-50/60">
        <div className="max-w-6xl mx-auto">
          <h2 className="text-3xl font-bold text-gray-900 text-center mb-4">
            Every Feature You Need to Master Azure Service Bus
          </h2>
          <p className="text-gray-500 text-center mb-14 max-w-2xl mx-auto">
            ServiceHub covers the full lifecycle — from real-time browsing to automated recovery and deep forensic analysis.
          </p>

          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
            {coreFeatures.map(({ icon: Icon, title, description, badge }) => (
              <div
                key={title}
                className="p-6 bg-white rounded-xl border border-gray-200 shadow-sm hover:shadow-md hover:border-primary-300 transition-all"
              >
                <div className="flex items-start justify-between mb-4">
                  <div className="w-11 h-11 bg-primary-50 rounded-lg flex items-center justify-center">
                    <Icon className="w-6 h-6 text-primary-600" />
                  </div>
                  <span className="text-xs font-semibold text-primary-600 bg-primary-50 border border-primary-100 px-2 py-0.5 rounded-full">
                    {badge}
                  </span>
                </div>
                <h3 className="text-base font-bold text-gray-900 mb-2">{title}</h3>
                <p className="text-sm text-gray-600 leading-relaxed">{description}</p>
              </div>
            ))}
          </div>

          {/* Feature chips */}
          <div className="mt-14 pt-10 border-t border-gray-200">
            <p className="text-center text-sm font-semibold text-gray-500 mb-6 uppercase tracking-wider">All Included in v3.1.0</p>
            <div className="flex flex-wrap justify-center gap-3">
              {[
                '🔍 DLQ Intelligence',
                '⚡ Auto-Replay Rules',
                '🤖 AI Pattern Analysis',
                '🔗 Correlation Explorer',
                '⏱️ Scheduled Messages',
                '💚 Health Monitor',
                '🌐 Multi-Namespace',
                '📊 30-Day Trends',
                '🔐 AES-GCM Security',
                '📤 CSV/JSON Export',
                '🔎 Full-Text Search',
                '♻️ Auto-Refresh Live',
              ].map((chip) => (
                <span
                  key={chip}
                  className="px-4 py-2 bg-white border border-gray-200 rounded-full text-sm text-gray-700 font-medium shadow-sm hover:border-primary-300 hover:text-primary-700 transition-colors"
                >
                  {chip}
                </span>
              ))}
            </div>
          </div>
        </div>
      </section>

      {/* ─── Comparison Table ────────────────────────────────────────────── */}
      <section id="compare" className="py-20 px-6">
        <div className="max-w-5xl mx-auto">
          <h2 className="text-3xl font-bold text-gray-900 text-center mb-4">
            ServiceHub vs Azure Portal
          </h2>
          <p className="text-gray-500 text-center mb-12 max-w-xl mx-auto">
            The Azure Portal is great for provisioning — but terrible for debugging. Here's the difference.
          </p>

          <div className="overflow-x-auto rounded-xl border border-gray-200 shadow-sm">
            <table className="w-full text-sm">
              <thead>
                <tr className="bg-gray-50 border-b border-gray-200">
                  <th className="px-6 py-4 text-left font-bold text-gray-800 w-1/2">Capability</th>
                  <th className="px-6 py-4 text-center font-bold text-gray-500 w-1/4">Azure Portal</th>
                  <th className="px-6 py-4 text-center font-bold text-primary-600 w-1/4">ServiceHub</th>
                </tr>
              </thead>
              <tbody>
                {comparisonRows.map(({ feature, portal, hub }, i) => (
                  <tr key={feature} className={`${i % 2 === 0 ? 'bg-white' : 'bg-gray-50/40'} border-b border-gray-100 hover:bg-primary-50/30 transition-colors`}>
                    <td className="px-6 py-3.5 text-gray-800 font-medium">{feature}</td>
                    <td className="px-6 py-3.5 text-center">
                      <span className="inline-flex items-center gap-1.5 text-red-500 font-medium">
                        <span className="text-red-400">✗</span> {portal}
                      </span>
                    </td>
                    <td className="px-6 py-3.5 text-center">
                      <span className="inline-flex items-center gap-1.5 text-green-600 font-semibold">
                        <span className="text-green-500">✓</span> {hub}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="mt-8 p-5 bg-gradient-to-r from-primary-50 to-blue-50 border border-primary-200 rounded-xl text-center">
            <p className="text-gray-800 font-semibold text-base">
              🎯 ServiceHub turns a 6-hour incident into a 45-minute fix.
            </p>
            <p className="text-gray-600 text-sm mt-1">
              Stop guessing from counts. Start reading messages.
            </p>
          </div>
        </div>
      </section>

      {/* ─── Use Cases ───────────────────────────────────────────────────── */}
      <section id="usecases" className="py-20 px-6 bg-gray-50/60">
        <div className="max-w-5xl mx-auto">
          <h2 className="text-3xl font-bold text-gray-900 text-center mb-4">
            Real-World Scenarios Where ServiceHub Shines
          </h2>
          <p className="text-gray-500 text-center mb-12 max-w-xl mx-auto">
            From 2 AM production fires to weekly post-mortems — ServiceHub is built for every phase.
          </p>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-8">
            {useCases.map(({ emoji, title, story, saving, color }) => (
              <div
                key={title}
                className={`p-6 bg-gradient-to-br ${colorMap[color]} rounded-xl border shadow-sm hover:shadow-md transition-all`}
              >
                <div className="text-4xl mb-3">{emoji}</div>
                <h3 className="text-lg font-bold text-gray-900 mb-3">{title}</h3>
                <p className="text-gray-600 text-sm leading-relaxed mb-4">{story}</p>
                <span className={`text-xs font-bold ${savingColorMap[color]} bg-white/70 px-3 py-1 rounded-full border`}>
                  ⚡ {saving}
                </span>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* ─── Security & Privacy Deep-Dive ────────────────────────────────── */}
      <section className="py-20 px-6">
        <div className="max-w-5xl mx-auto">
          <h2 className="text-3xl font-bold text-gray-900 text-center mb-4">
            Enterprise-Grade Security. Developer-Friendly.
          </h2>
          <p className="text-gray-500 text-center mb-14 max-w-2xl mx-auto">
            ServiceHub was built from day one with the principle that a debugging tool should never
            become a security liability.
          </p>

          <div className="grid grid-cols-1 md:grid-cols-3 gap-6 mb-10">
            {[
              {
                icon: '🔐',
                title: 'AES-GCM Encryption',
                body: 'Your connection strings are encrypted at rest using AES-256-GCM. The encryption key lives only in your server config — never in the database.',
              },
              {
                icon: '👁️',
                title: 'Read-Only by Default',
                body: 'All message reading uses PeekMessagesAsync. ServiceHub never consumes, removes, or alters any messages on your bus. Your consumers are unaffected.',
              },
              {
                icon: '🧠',
                title: 'Zero-Data-Exfiltration AI',
                body: 'AI pattern analysis is pure TypeScript running in your browser tab. Message content never leaves your environment — no API calls, no cloud processing.',
              },
              {
                icon: '🛡️',
                title: 'OWASP Compliant',
                body: 'Built against OWASP Top 10 guidelines. Input validation, secure headers, no SQL injection surface (SQLite queries via EF Core parameterised).',
              },
              {
                icon: '🔑',
                title: 'Minimum-Privilege Design',
                body: 'Full functionality with Listen-only permission. Send permission required only for replay. Manage permission only for message generation.',
              },
              {
                icon: '🏠',
                title: 'Self-Host for Sovereignty',
                body: 'Deploy on your own Azure subscription, on-premises VM, Docker container, or Kubernetes cluster. You own the infrastructure and the data.',
              },
            ].map(({ icon, title, body }) => (
              <div key={title} className="p-5 bg-white rounded-xl border border-gray-200 shadow-sm hover:shadow-md transition-all">
                <div className="text-2xl mb-3">{icon}</div>
                <h4 className="font-bold text-gray-900 mb-2">{title}</h4>
                <p className="text-sm text-gray-600 leading-relaxed">{body}</p>
              </div>
            ))}
          </div>

          {/* Auth note repeated in security context */}
          <div className="p-6 bg-gradient-to-r from-blue-50 to-sky-50 border border-blue-200 rounded-xl">
            <div className="flex items-start gap-4">
              <Shield className="w-6 h-6 text-blue-600 shrink-0 mt-0.5" />
              <div>
                <p className="font-bold text-blue-900 mb-2">About the Hosted Application Authentication</p>
                <p className="text-sm text-blue-800 leading-relaxed">
                  The hosted ServiceHub at{' '}
                  <a
                    href={LIVE_APP_URL}
                    className="underline hover:text-blue-600"
                    target="_blank"
                    rel="noopener noreferrer"
                  >
                    app-servicehub-prod.azurewebsites.net
                  </a>{' '}
                  uses <strong>Microsoft Entra ID (Azure Active Directory)</strong> as the identity provider.
                  When you sign in, you are authenticating directly with <strong>Microsoft's infrastructure</strong> —
                  not with ServiceHub servers. We receive only your Microsoft identity claim to verify you have access.
                  <strong> We store no passwords, no personal data, and no user records.</strong>{' '}
                  This is purely a security gate to prevent unauthorised access to the shared hosting environment.
                  For <strong>full data sovereignty</strong>, self-host ServiceHub on your own infrastructure.
                </p>
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* ─── Why Choose / Comparison Bullets ────────────────────────────── */}
      <section className="py-20 px-6 bg-gray-50/60">
        <div className="max-w-5xl mx-auto">
          <h2 className="text-3xl font-bold text-gray-900 text-center mb-12">
            Why Teams Choose ServiceHub
          </h2>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-12">
            <div>
              <h3 className="text-xl font-bold text-gray-900 mb-6 flex items-center gap-2">
                <span className="text-2xl">🏆</span> Beats the Azure Portal
              </h3>
              <ul className="space-y-4">
                {[
                  'See full message body — not just counts',
                  'Search 10,000 messages in under a second',
                  'Auto-replay failed DLQ messages with rules',
                  '30-day failure history with trend charts',
                  'AI clusters messages by error type automatically',
                  'Trace any Correlation ID across all queues',
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
                <span className="text-2xl">🧑‍💻</span> Built for DevOps & SREs
              </h3>
              <ul className="space-y-4">
                {[
                  'Works on any OS — macOS, Linux, Windows',
                  'One-command setup: clone → run → connect',
                  'Multi-namespace: DEV, UAT, PROD simultaneously',
                  'Enterprise AES-GCM encryption out of the box',
                  'Deploy anywhere: Azure, Docker, Kubernetes, VMs',
                  'Open source, MIT licensed — no vendor lock-in',
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

      {/* ─── Final CTA ───────────────────────────────────────────────────── */}
      <section className="py-24 px-6">
        <div className="max-w-3xl mx-auto bg-gradient-to-br from-primary-600 via-primary-700 to-blue-700 rounded-2xl p-12 text-white text-center shadow-2xl relative overflow-hidden">
          {/* Decorative blobs */}
          <div className="absolute top-0 right-0 w-48 h-48 bg-white/5 rounded-full -translate-y-1/2 translate-x-1/2" />
          <div className="absolute bottom-0 left-0 w-32 h-32 bg-white/5 rounded-full translate-y-1/2 -translate-x-1/2" />

          <div className="relative">
            <h2 className="text-3xl md:text-4xl font-extrabold mb-4">
              Stop Guessing. Start Debugging.
            </h2>
            <p className="text-lg mb-2 text-white/90 leading-relaxed">
              Your DLQ messages are telling a story. ServiceHub helps you read it.
            </p>
            <p className="text-sm text-white/70 mb-10">
              No credit card. No install required. Connect your Azure Service Bus in 30 seconds.
            </p>
            <div className="flex flex-col sm:flex-row items-center justify-center gap-4">
              <Link
                to="/connect"
                className="inline-flex items-center gap-2.5 px-8 py-4 bg-white text-primary-700 font-bold text-base rounded-xl hover:bg-gray-100 transition-all shadow-lg hover:-translate-y-0.5"
                aria-label="Open the ServiceHub application"
              >
                🚀 Open ServiceHub
                <ArrowRight className="w-5 h-5" />
              </Link>
              <Link
                to="/connect"
                className="inline-flex items-center gap-2.5 px-8 py-4 bg-white/15 text-white font-bold text-base rounded-xl border-2 border-white/40 hover:bg-white/25 transition-all"
              >
                💻 Self-Host on localhost
              </Link>
            </div>
          </div>
        </div>
      </section>

      {/* ─── Footer ──────────────────────────────────────────────────────── */}
      <footer className="border-t border-gray-200 py-12 px-6 bg-gray-50">
        <div className="max-w-7xl mx-auto">
          <div className="grid grid-cols-1 md:grid-cols-4 gap-8 mb-10">
            {/* Brand */}
            <div>
              <div className="flex items-center gap-2 mb-4">
                <div className="w-8 h-8 bg-primary-600 rounded-lg flex items-center justify-center">
                  <Search className="w-4 h-4 text-white" />
                </div>
                <span className="font-bold text-gray-900">ServiceHub</span>
              </div>
              <p className="text-xs text-gray-500 leading-relaxed">
                The forensic debugger for Azure Service Bus. Built for engineers who need real answers fast.
              </p>
            </div>

            {/* Product */}
            <div>
              <h4 className="font-semibold text-gray-900 mb-4 text-sm">Product</h4>
              <ul className="space-y-2.5 text-sm text-gray-600">
                <li>
                  <a href={`${GITHUB_URL}/blob/main/README.md`} target="_blank" rel="noopener noreferrer" className="hover:text-primary-600 flex items-center gap-1.5">
                    Documentation <ExternalLink className="w-3 h-3" />
                  </a>
                </li>
                <li>
                  <a href={`${GITHUB_URL}/tree/main/self-hosting`} target="_blank" rel="noopener noreferrer" className="hover:text-primary-600 flex items-center gap-1.5">
                    Self-Hosting Guide <ExternalLink className="w-3 h-3" />
                  </a>
                </li>
                <li>
                  <Link to="/security" className="hover:text-primary-600">Security & Privacy</Link>
                </li>
                <li>
                  <a href={`${GITHUB_URL}/blob/main/CHANGELOG.md`} target="_blank" rel="noopener noreferrer" className="hover:text-primary-600 flex items-center gap-1.5">
                    Changelog <ExternalLink className="w-3 h-3" />
                  </a>
                </li>
              </ul>
            </div>

            {/* Community */}
            <div>
              <h4 className="font-semibold text-gray-900 mb-4 text-sm">Community</h4>
              <ul className="space-y-2.5 text-sm text-gray-600">
                <li>
                  <a href={GITHUB_URL} target="_blank" rel="noopener noreferrer" className="hover:text-primary-600 flex items-center gap-1.5">
                    <Github className="w-4 h-4" /> GitHub Repository
                  </a>
                </li>
                <li>
                  <a href={`${GITHUB_URL}/issues`} target="_blank" rel="noopener noreferrer" className="hover:text-primary-600">Report an Issue</a>
                </li>
                <li>
                  <a href={`${GITHUB_URL}/discussions`} target="_blank" rel="noopener noreferrer" className="hover:text-primary-600">Discussions</a>
                </li>
                <li>
                  <a href={`${GITHUB_URL}/releases`} target="_blank" rel="noopener noreferrer" className="hover:text-primary-600">Releases</a>
                </li>
              </ul>
            </div>

            {/* Legal */}
            <div>
              <h4 className="font-semibold text-gray-900 mb-4 text-sm">Legal</h4>
              <ul className="space-y-2.5 text-sm text-gray-600">
                <li>
                  <a href={`${GITHUB_URL}/blob/main/SECURITY.md`} target="_blank" rel="noopener noreferrer" className="hover:text-primary-600">Security Policy</a>
                </li>
                <li>
                  <a href={`${GITHUB_URL}/blob/main/LICENSE`} target="_blank" rel="noopener noreferrer" className="hover:text-primary-600">MIT License</a>
                </li>
                <li>
                  <Link to="/security" className="hover:text-primary-600">Privacy Notice</Link>
                </li>
              </ul>
            </div>
          </div>

          <div className="border-t border-gray-200 pt-8 flex flex-col md:flex-row items-center justify-between gap-4 text-sm text-gray-500">
            <p>
              ServiceHub is open source, free to use, and MIT licensed. Made with ❤️ by{' '}
              <a href="https://github.com/debdevops" className="text-primary-600 hover:underline font-medium">
                Debasis
              </a>
            </p>
            <p>© 2026 ServiceHub · All rights reserved</p>
          </div>
        </div>
      </footer>
    </div>
  );
}
