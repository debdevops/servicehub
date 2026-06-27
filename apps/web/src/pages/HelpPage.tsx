import { useState, useMemo } from 'react';
import { Link } from 'react-router-dom';
import { Search, BookOpen, ChevronDown, ChevronRight, Play, HelpCircle, Download, Zap, MessageSquare } from 'lucide-react';
import { helpSections } from '@/lib/helpContent';
import { resetTour } from '@/components/help/GuidedTour';

/**
 * Help & Quick Reference page — searchable guide covering every feature,
 * Azure Service Bus concepts, and the guided tour.
 * Enhanced with visual examples and platform support information.
 */
export function HelpPage() {
  const [searchQuery, setSearchQuery] = useState('');
  const [expandedSections, setExpandedSections] = useState<Set<string>>(
    new Set(helpSections.map((s) => s.id)),
  );

  // Screenshot mappings for visual context
  const screenshotMap: Record<string, string[]> = {
    'getting-started': ['/docs/screenshots/ServiceHub-Home-Page.png'],
    'messages': ['/docs/screenshots/ServiceHub-Active-Message-1.png', '/docs/screenshots/ServiceHub-Message-Detail-Expanded.png'],
    'dlq': ['/docs/screenshots/ServiceHub-DLQ-Intelligence.png'],
    'rules': ['/docs/screenshots/ServiceHub-Auto-Replay-Rules.png'],
    'fab': ['/docs/screenshots/ServiceHub-Dashborad-6.png'],
    'health': ['/docs/screenshots/ServiceHub-System-Health-Status.png'],
  };

  const filteredSections = useMemo(() => {
    if (!searchQuery.trim()) return helpSections;
    const q = searchQuery.toLowerCase();
    return helpSections
      .map((section) => ({
        ...section,
        items: section.items.filter(
          (item) =>
            item.question.toLowerCase().includes(q) ||
            item.answer.toLowerCase().includes(q),
        ),
      }))
      .filter((section) => section.items.length > 0);
  }, [searchQuery]);

  const toggleSection = (id: string) => {
    setExpandedSections((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const handleStartTour = () => {
    resetTour();
    // Dispatch a custom event that MainLayout listens for
    window.dispatchEvent(new CustomEvent('servicehub:start-tour'));
  };

  return (
    <div className="h-full overflow-y-auto bg-gradient-to-b from-white via-blue-50 to-white">
      <div className="max-w-4xl mx-auto px-6 py-8">
        {/* Hero Header */}
        <div className="mb-10">
          <div className="flex items-start justify-between mb-6">
            <div className="flex-1">
              <div className="flex items-center gap-4 mb-3">
                <div className="w-14 h-14 bg-gradient-to-br from-primary-500 to-primary-600 rounded-2xl flex items-center justify-center shadow-lg">
                  <BookOpen className="w-7 h-7 text-white" />
                </div>
                <div>
                  <h1 className="text-3xl font-bold text-gray-900">Help & Support</h1>
                  <p className="text-sm text-gray-500 mt-1">
                    Master ServiceHub in minutes
                  </p>
                </div>
              </div>
              <p className="text-gray-600 ml-[68px] text-sm leading-relaxed max-w-2xl">
                Everything you need to debug Azure Service Bus effectively — from getting started to advanced troubleshooting. Search or browse by topic.
              </p>
            </div>
            <button
              onClick={handleStartTour}
              className="flex items-center gap-2 px-6 py-3 text-sm font-semibold text-white bg-gradient-to-r from-primary-600 to-primary-700 hover:from-primary-700 hover:to-primary-800 rounded-xl transition-all shadow-md hover:shadow-lg"
            >
              <Play className="w-4 h-4" />
              Take a Tour
            </button>
          </div>

          {/* Platform Support */}
          <div className="flex items-center gap-6 ml-[68px] mt-6 pt-6 border-t border-gray-200">
            <div className="flex items-center gap-2">
              <span className="text-xs font-semibold text-gray-500 uppercase">Works on</span>
              <div className="flex items-center gap-3">
                <svg className="w-5 h-5 text-gray-700" viewBox="0 0 24 24" fill="currentColor">
                  {/* Windows */}
                  <path d="M0 3h9v9H0V3zm10 0h14v9H10V3zM0 14h9v9H0v-9zm10 0h14v9H10v-9z" />
                </svg>
                <span className="text-xs text-gray-700 font-medium">Windows</span>
              </div>
              <div className="flex items-center gap-3 ml-2">
                <svg className="w-5 h-5 text-gray-700" viewBox="0 0 24 24" fill="currentColor">
                  {/* macOS */}
                  <path d="M6.157 2a3 3 0 00-3 3v14a3 3 0 003 3h11.686a3 3 0 003-3V5a3 3 0 00-3-3H6.157zm0 1h11.686a2 2 0 012 2v14a2 2 0 01-2 2H6.157a2 2 0 01-2-2V5a2 2 0 012-2z" />
                </svg>
                <span className="text-xs text-gray-700 font-medium">macOS</span>
              </div>
              <div className="flex items-center gap-3 ml-2">
                <svg className="w-5 h-5 text-gray-700" viewBox="0 0 24 24" fill="currentColor">
                  {/* Linux */}
                  <path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8z" />
                </svg>
                <span className="text-xs text-gray-700 font-medium">Linux</span>
              </div>
            </div>
          </div>
        </div>

        {/* Quick Stats */}
        <div className="grid grid-cols-3 gap-4 mb-10">
          <div className="bg-white rounded-xl border border-gray-200 p-4 shadow-sm hover:shadow-md transition-shadow">
            <div className="flex items-center gap-3 mb-2">
              <Zap className="w-5 h-5 text-amber-500" />
              <span className="text-xs font-semibold text-gray-500 uppercase">Setup Time</span>
            </div>
            <p className="text-lg font-bold text-gray-900">30 seconds</p>
            <p className="text-xs text-gray-500 mt-1">From install to first debug</p>
          </div>
          <div className="bg-white rounded-xl border border-gray-200 p-4 shadow-sm hover:shadow-md transition-shadow">
            <div className="flex items-center gap-3 mb-2">
              <MessageSquare className="w-5 h-5 text-blue-500" />
              <span className="text-xs font-semibold text-gray-500 uppercase">Features</span>
            </div>
            <p className="text-lg font-bold text-gray-900">15+</p>
            <p className="text-xs text-gray-500 mt-1">Powerful debugging tools</p>
          </div>
          <div className="bg-white rounded-xl border border-gray-200 p-4 shadow-sm hover:shadow-md transition-shadow">
            <div className="flex items-center gap-3 mb-2">
              <Download className="w-5 h-5 text-green-500" />
              <span className="text-xs font-semibold text-gray-500 uppercase">No Setup</span>
            </div>
            <p className="text-lg font-bold text-gray-900">Free</p>
            <p className="text-xs text-gray-500 mt-1">100% open source</p>
          </div>
        </div>

        {/* Search */}
        <div className="relative mb-8">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
          <input
            type="text"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            placeholder="Search help topics… (type to filter)"
            className="w-full pl-10 pr-4 py-3 text-sm bg-white border border-gray-200 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-transparent shadow-sm hover:border-gray-300 transition-colors"
          />
          {searchQuery && (
            <button
              onClick={() => setSearchQuery('')}
              className="absolute right-3 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600 font-bold text-lg"
              aria-label="Clear search"
            >
              ×
            </button>
          )}
        </div>

        {/* No results */}
        {filteredSections.length === 0 && (
          <div className="text-center py-16">
            <div className="w-16 h-16 bg-gray-100 rounded-full flex items-center justify-center mx-auto mb-4">
              <HelpCircle className="w-8 h-8 text-gray-400" />
            </div>
            <p className="text-base font-semibold text-gray-900 mb-1">
              No results for "<span className="text-primary-600">{searchQuery}</span>"
            </p>
            <p className="text-sm text-gray-600 mb-6">
              Try a different search term or browse all topics
            </p>
            <button
              onClick={() => setSearchQuery('')}
              className="inline-flex items-center gap-2 px-4 py-2 bg-primary-50 text-primary-700 font-medium rounded-lg border border-primary-200 hover:bg-primary-100 transition-colors"
            >
              <ChevronRight className="w-4 h-4" />
              Clear search
            </button>
          </div>
        )}

        {/* Sections */}
        <div className="space-y-5">
          {filteredSections.map((section) => {
            const isExpanded = expandedSections.has(section.id);
            const screenshots = screenshotMap[section.id] || [];
            
            return (
              <div
                key={section.id}
                className="bg-white rounded-xl border border-gray-200 shadow-sm hover:shadow-md transition-all overflow-hidden"
              >
                {/* Section header */}
                <button
                  onClick={() => toggleSection(section.id)}
                  className="w-full flex items-center gap-4 px-6 py-4 text-left hover:bg-gradient-to-r hover:from-blue-50 hover:to-transparent transition-colors"
                >
                  <span className="text-2xl">{section.icon}</span>
                  <div className="flex-1">
                    <h2 className="text-base font-semibold text-gray-900">
                      {section.title}
                    </h2>
                    <p className="text-xs text-gray-500 mt-0.5">
                      {section.items.length} {section.items.length === 1 ? 'topic' : 'topics'}
                    </p>
                  </div>
                  <span className="text-xs px-2.5 py-1 bg-blue-100 text-blue-700 rounded-full font-medium">
                    {section.items.length}
                  </span>
                  {isExpanded ? (
                    <ChevronDown className="w-5 h-5 text-gray-400" />
                  ) : (
                    <ChevronRight className="w-5 h-5 text-gray-400" />
                  )}
                </button>

                {/* Items */}
                {isExpanded && (
                  <div className="border-t border-gray-100">
                    {/* Screenshot preview if available */}
                    {screenshots.length > 0 && (
                      <div className="overflow-x-auto bg-gray-50 px-6 py-4 border-b border-gray-100">
                        <div className="flex gap-4">
                          {screenshots.map((src, idx) => (
                            <div key={idx} className="flex-shrink-0">
                              <img
                                src={src}
                                alt={`${section.title} example ${idx + 1}`}
                                className="h-32 rounded-lg border border-gray-200 shadow-sm hover:shadow-md transition-shadow object-cover"
                                onError={(e) => {
                                  // Gracefully handle missing images
                                  (e.target as HTMLImageElement).style.display = 'none';
                                }}
                              />
                            </div>
                          ))}
                        </div>
                        <p className="text-xs text-gray-600 mt-2">💡 Visual examples - click to expand</p>
                      </div>
                    )}

                    {/* Content items */}
                    {section.items.map((item, idx) => (
                      <div
                        key={idx}
                        className={`px-6 py-4 ${
                          idx < section.items.length - 1 ? 'border-b border-gray-50' : ''
                        } hover:bg-gray-50 transition-colors`}
                      >
                        <div className="flex items-start gap-3">
                          <div className="flex-shrink-0 mt-0.5">
                            <div className="w-1.5 h-1.5 bg-primary-500 rounded-full mt-1.5" />
                          </div>
                          <div className="flex-1 min-w-0">
                            <h3 className="text-sm font-semibold text-gray-900 mb-2">
                              {item.question}
                            </h3>
                            <p className="text-sm text-gray-700 leading-relaxed">
                              {item.answer}
                            </p>
                          </div>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            );
          })}
        </div>

        {/* Keyboard shortcuts */}
        <div className="mt-10 bg-gradient-to-br from-gray-50 to-blue-50 rounded-xl border border-gray-200 p-6">
          <h2 className="text-sm font-bold text-gray-900 mb-4 flex items-center gap-2">
            <span className="text-lg">⌨️</span>
            Keyboard Shortcuts
          </h2>
          <div className="grid grid-cols-2 gap-3">
            <div className="flex items-center justify-between bg-white px-4 py-3 rounded-lg border border-gray-200 hover:border-gray-300 transition-colors">
              <span className="text-sm font-medium text-gray-700">Search messages</span>
              <kbd className="bg-gray-100 text-gray-700 px-2 py-1 rounded font-mono text-xs font-semibold border border-gray-200">⌘K</kbd>
            </div>
            <div className="flex items-center justify-between bg-white px-4 py-3 rounded-lg border border-gray-200 hover:border-gray-300 transition-colors">
              <span className="text-sm font-medium text-gray-700">Refresh data</span>
              <kbd className="bg-gray-100 text-gray-700 px-2 py-1 rounded font-mono text-xs font-semibold border border-gray-200">⌘R</kbd>
            </div>
            <div className="flex items-center justify-between bg-white px-4 py-3 rounded-lg border border-gray-200 hover:border-gray-300 transition-colors">
              <span className="text-sm font-medium text-gray-700">Close modal / tour</span>
              <kbd className="bg-gray-100 text-gray-700 px-2 py-1 rounded font-mono text-xs font-semibold border border-gray-200">Esc</kbd>
            </div>
            <div className="flex items-center justify-between bg-white px-4 py-3 rounded-lg border border-gray-200 hover:border-gray-300 transition-colors">
              <span className="text-sm font-medium text-gray-700">Open help</span>
              <kbd className="bg-gray-100 text-gray-700 px-2 py-1 rounded font-mono text-xs font-semibold border border-gray-200">?</kbd>
            </div>
          </div>
        </div>

        {/* Tip Section */}
        <div className="mt-8 bg-gradient-to-r from-amber-50 to-orange-50 rounded-xl border border-amber-200 p-6">
          <div className="flex gap-4">
            <div className="flex-shrink-0 text-2xl">💡</div>
            <div>
              <h3 className="font-semibold text-gray-900 mb-1">Pro Tip</h3>
              <p className="text-sm text-gray-700">
                Use the <span className="font-mono bg-yellow-100 px-1.5 py-0.5 rounded text-xs font-semibold">?</span> keyboard shortcut from any page to jump straight to help. Or click the <span className="font-mono bg-blue-100 px-1.5 py-0.5 rounded text-xs font-semibold">?</span> icon in the top navigation.
              </p>
            </div>
          </div>
        </div>

        {/* Footer CTA */}
        <div className="mt-10 bg-gradient-to-r from-primary-600 to-primary-700 rounded-2xl p-8 text-white text-center shadow-lg">
          <h2 className="text-xl font-bold mb-2">Still have questions?</h2>
          <p className="text-primary-100 mb-6">
            Join our GitHub discussions or check out the documentation for advanced topics.
          </p>
          <div className="flex items-center justify-center gap-4 flex-wrap">
            <a
              href="https://github.com/debdevops/servicehub/discussions"
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-2 px-6 py-2.5 bg-white text-primary-700 font-semibold rounded-lg hover:bg-gray-100 transition-colors"
            >
              <span>💬</span>
              GitHub Discussions
            </a>
            <Link
              to="/security"
              className="inline-flex items-center gap-2 px-6 py-2.5 border border-white text-white hover:bg-primary-800 font-semibold rounded-lg transition-colors"
            >
              <span>🔒</span>
              Security & Privacy
            </Link>
          </div>
        </div>

        {/* Footer */}
        <div className="mt-10 pb-8 flex flex-wrap items-center justify-between gap-6 border-t border-gray-200 pt-6">
          <div>
            <p className="text-xs text-gray-500 font-medium">
              ServiceHub • Multi-Cloud Message Queue Debugger
            </p>
            <p className="text-xs text-gray-400 mt-1">
              Made with ❤️ for developers • Open Source
            </p>
          </div>
          <div className="flex items-center gap-4 text-xs">
            <span className="text-gray-500">v3.2.2</span>
            <span className="text-gray-300">•</span>
            <a
              href="https://github.com/debdevops/servicehub"
              target="_blank"
              rel="noopener noreferrer"
              className="text-primary-600 hover:text-primary-700 font-medium"
            >
              GitHub
            </a>
            <span className="text-gray-300">•</span>
            <a
              href="https://github.com/debdevops/servicehub/issues"
              target="_blank"
              rel="noopener noreferrer"
              className="text-primary-600 hover:text-primary-700 font-medium"
            >
              Report Issue
            </a>
          </div>
        </div>
      </div>
    </div>
  );
}
