import { useState, useMemo } from 'react';
import { InsightsSidebar, InsightCard } from '@/components/insights';
import {
  MOCK_INSIGHTS,
  INSIGHT_COUNTS,
  type InsightCategory,
} from '@/lib/insightsMockData';

// Category title mapping
const CATEGORY_TITLES: Record<InsightCategory, string> = {
  critical: 'Critical Issues Requiring Attention',
  warnings: 'Warnings & Performance Issues',
  patterns: 'Detected Patterns & Trends',
  performance: 'Performance Optimization Opportunities',
  security: 'Security Analysis',
};

/**
 * AI Insights Hub Page
 * 
 * Features:
 * - Left sidebar with category filters
 * - Main content with insight cards
 * - Metrics grid per insight
 * - Recommendations with priority labels
 */
export function InsightsPage() {
  const [selectedCategory, setSelectedCategory] = useState<InsightCategory>('critical');

  // Filter insights by selected category
  const filteredInsights = useMemo(
    () => MOCK_INSIGHTS.filter(insight => insight.category === selectedCategory),
    [selectedCategory]
  );

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      {/* Header */}
      <div className="bg-gradient-to-r from-primary-600 to-primary-500 px-6 py-4 shrink-0">
        <div className="flex items-center justify-between">
          <h1 className="text-xl font-semibold text-white">AI Insights Dashboard</h1>
          <div className="flex items-center gap-2 text-primary-100">
            <span className="text-sm">Analyzing:</span>
            <span className="px-2 py-1 bg-white/20 rounded text-white font-medium text-sm">
              OrdersQueue
            </span>
            <span className="w-2 h-2 rounded-full bg-green-400 ml-1" />
          </div>
        </div>
      </div>

      {/* Main Content */}
      <div className="flex-1 flex overflow-hidden">
        {/* Sidebar */}
        <InsightsSidebar
          selectedCategory={selectedCategory}
          onSelectCategory={setSelectedCategory}
          categoryCounts={INSIGHT_COUNTS}
        />

        {/* Content Area */}
        <div className="flex-1 overflow-auto bg-transparent p-6">
          {/* Section Title */}
          <h2 className="text-lg font-semibold text-gray-900 mb-6">
            {CATEGORY_TITLES[selectedCategory]}
          </h2>

          {/* Insight Cards */}
          {filteredInsights.length > 0 ? (
            <div className="space-y-6">
              {filteredInsights.map(insight => (
                <InsightCard key={insight.id} insight={insight} />
              ))}
            </div>
          ) : (
            <div className="flex flex-col items-center justify-center py-16 text-gray-500">
              <div className="w-16 h-16 bg-green-100 rounded-full flex items-center justify-center mb-4">
                <span className="text-3xl">✓</span>
              </div>
              <p className="text-lg font-medium text-gray-700">No Issues Detected</p>
              <p className="text-sm text-gray-500 mt-1">
                {selectedCategory === 'security'
                  ? 'No security issues detected in your queues'
                  : `No ${selectedCategory} issues found`}
              </p>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
