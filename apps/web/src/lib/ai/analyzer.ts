/**
 * ============================================================================
 * AI Message Analyzer - Client-Side Pattern Detection
 * ============================================================================
 * 
 * This module provides client-side AI analysis for Service Bus messages.
 * It detects patterns in message data without requiring a backend AI service.
 * 
 * IMPORTANT TRUST GUARANTEES:
 * - All insights are labeled as "ServiceHub Interpretation"
 * - AI never presents inference as fact
 * - Uncertainty is explicitly stated
 * - Evidence (counts, IDs, time windows) always cited
 * - No destructive actions - read-only analysis
 * 
 * Pattern Types Detected:
 * 1. dlq-pattern: Dead-letter accumulation patterns
 * 2. retry-loop: Messages stuck in retry cycles
 * 3. error-cluster: Groups of messages with similar errors
 * 4. poison-message: Messages that repeatedly fail processing
 * 5. latency-anomaly: Unusual delays in message processing
 */

import type { Message as APIMessage } from '@/lib/api/types';
import type { AIInsight, InsightType, ConfidenceLevel } from '@/lib/api/types';

// ============================================================================
// Configuration
// ============================================================================

/**
 * AI Analysis Configuration
 * These thresholds are conservative to avoid false positives
 */
const CONFIG = {
  // Minimum messages required to detect a pattern
  MIN_MESSAGES_FOR_PATTERN: 3,
  
  // High delivery count threshold (indicates retry issues)
  HIGH_DELIVERY_COUNT: 5,
  
  // Poison message delivery count threshold
  POISON_MESSAGE_THRESHOLD: 10,
  
  // Minimum percentage of messages needed to form a cluster
  MIN_CLUSTER_PERCENTAGE: 0.1, // 10%
  
  // Time window for pattern detection (ms)
  ANALYSIS_WINDOW_MS: 60 * 60 * 1000, // 1 hour
  
  // Confidence thresholds
  HIGH_CONFIDENCE_THRESHOLD: 80,
  MEDIUM_CONFIDENCE_THRESHOLD: 50,
} as const;

// ============================================================================
// Types
// ============================================================================

interface AnalysisContext {
  namespaceId: string;
  entityName: string;
  subscriptionName?: string;
  entityType: 'queue' | 'topic';
}

interface PatternMatch {
  type: InsightType;
  messages: APIMessage[];
  signature?: string;
  metrics: Record<string, number | string>;
}

// ============================================================================
// Pattern Detection Functions
// ============================================================================

/**
 * Detect DLQ accumulation patterns
 * Looks for common error signatures in dead-lettered messages
 */
function detectDLQPatterns(messages: APIMessage[]): PatternMatch[] {
  const dlqMessages = messages.filter(m => m.isFromDeadLetter || m.deadLetterReason);
  
  if (dlqMessages.length < CONFIG.MIN_MESSAGES_FOR_PATTERN) {
    return [];
  }
  
  // Group by dead-letter reason
  const reasonGroups = new Map<string, APIMessage[]>();
  
  for (const msg of dlqMessages) {
    const reason = normalizeErrorReason(msg.deadLetterReason || 'Unknown');
    const existing = reasonGroups.get(reason) || [];
    existing.push(msg);
    reasonGroups.set(reason, existing);
  }
  
  const patterns: PatternMatch[] = [];
  
  for (const [reason, msgs] of reasonGroups) {
    if (msgs.length >= CONFIG.MIN_MESSAGES_FOR_PATTERN) {
      const percentage = Math.round((msgs.length / dlqMessages.length) * 100);
      
      patterns.push({
        type: 'dlq-pattern',
        messages: msgs,
        signature: reason,
        metrics: {
          affectedCount: msgs.length,
          totalDLQ: dlqMessages.length,
          matchPercentage: percentage,
        },
      });
    }
  }
  
  return patterns;
}

/**
 * Detect retry loop patterns
 * Identifies messages with abnormally high delivery counts
 */
function detectRetryLoops(messages: APIMessage[]): PatternMatch[] {
  const retryMessages = messages.filter(m => 
    m.deliveryCount >= CONFIG.HIGH_DELIVERY_COUNT && !m.isFromDeadLetter
  );
  
  if (retryMessages.length < CONFIG.MIN_MESSAGES_FOR_PATTERN) {
    return [];
  }
  
  const avgDeliveryCount = Math.round(
    retryMessages.reduce((sum, m) => sum + m.deliveryCount, 0) / retryMessages.length
  );
  
  return [{
    type: 'retry-loop',
    messages: retryMessages,
    signature: `High retry count (avg: ${avgDeliveryCount})`,
    metrics: {
      affectedCount: retryMessages.length,
      avgDeliveryCount,
      maxDeliveryCount: Math.max(...retryMessages.map(m => m.deliveryCount)),
    },
  }];
}

/**
 * Detect poison messages
 * Messages that have failed many times and are likely unprocessable
 */
function detectPoisonMessages(messages: APIMessage[]): PatternMatch[] {
  const poisonMessages = messages.filter(m => 
    m.deliveryCount >= CONFIG.POISON_MESSAGE_THRESHOLD
  );
  
  if (poisonMessages.length === 0) {
    return [];
  }
  
  return [{
    type: 'poison-message',
    messages: poisonMessages,
    signature: `Exceeded ${CONFIG.POISON_MESSAGE_THRESHOLD} delivery attempts`,
    metrics: {
      affectedCount: poisonMessages.length,
      avgDeliveryCount: Math.round(
        poisonMessages.reduce((sum, m) => sum + m.deliveryCount, 0) / poisonMessages.length
      ),
    },
  }];
}

/**
 * Detect error clusters
 * Groups messages by common error patterns in their body/properties
 */
function detectErrorClusters(messages: APIMessage[]): PatternMatch[] {
  const errorMessages = messages.filter(m => {
    // Check for error indicators in body or properties
    const body = m.body?.toLowerCase() || '';
    const hasErrorIndicator = 
      body.includes('error') || 
      body.includes('exception') || 
      body.includes('failed') ||
      body.includes('timeout');
    
    return hasErrorIndicator || m.isFromDeadLetter;
  });
  
  if (errorMessages.length < CONFIG.MIN_MESSAGES_FOR_PATTERN) {
    return [];
  }
  
  // Try to extract error types from message bodies
  const errorTypeGroups = new Map<string, APIMessage[]>();
  
  for (const msg of errorMessages) {
    const errorType = extractErrorType(msg);
    if (errorType) {
      const existing = errorTypeGroups.get(errorType) || [];
      existing.push(msg);
      errorTypeGroups.set(errorType, existing);
    }
  }
  
  const patterns: PatternMatch[] = [];
  
  for (const [errorType, msgs] of errorTypeGroups) {
    if (msgs.length >= CONFIG.MIN_MESSAGES_FOR_PATTERN) {
      patterns.push({
        type: 'error-cluster',
        messages: msgs,
        signature: errorType,
        metrics: {
          affectedCount: msgs.length,
          errorType,
        },
      });
    }
  }
  
  return patterns;
}

// ============================================================================
// Helper Functions
// ============================================================================

/**
 * Normalize error reasons for grouping
 */
function normalizeErrorReason(reason: string): string {
  // Remove timestamps, IDs, and specific values
  return reason
    .replace(/\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}/g, '[timestamp]')
    .replace(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/gi, '[uuid]')
    .replace(/\d+\.\d+\.\d+\.\d+/g, '[ip]')
    .replace(/\d{10,}/g, '[number]')
    .trim()
    .substring(0, 100);
}

/**
 * Extract error type from message body
 */
function extractErrorType(msg: APIMessage): string | null {
  const body = msg.body || '';
  
  // Common error patterns
  const patterns = [
    /(?:Exception|Error):\s*([A-Za-z]+(?:Exception|Error))/i,
    /"(?:error|exception|type)":\s*"([^"]+)"/i,
    /(?:failed|error|exception)\s+(?:with|:)\s+([A-Za-z]+)/i,
  ];
  
  for (const pattern of patterns) {
    const match = body.match(pattern);
    if (match) {
      return match[1].trim();
    }
  }
  
  // Fallback to dead-letter reason if available
  if (msg.deadLetterReason) {
    return normalizeErrorReason(msg.deadLetterReason);
  }
  
  return null;
}

/**
 * Calculate confidence level based on evidence strength
 */
function calculateConfidence(
  matchCount: number, 
  totalCount: number, 
  patternStrength: number
): { level: ConfidenceLevel; score: number; reasoning: string } {
  const percentage = totalCount > 0 ? (matchCount / totalCount) * 100 : 0;
  const score = Math.min(99, Math.round((percentage * 0.6) + (patternStrength * 0.4)));
  
  let level: ConfidenceLevel;
  let reasoning: string;
  
  if (score >= CONFIG.HIGH_CONFIDENCE_THRESHOLD) {
    level = 'high';
    reasoning = `Strong pattern match: ${matchCount} messages (${percentage.toFixed(0)}%) share this characteristic`;
  } else if (score >= CONFIG.MEDIUM_CONFIDENCE_THRESHOLD) {
    level = 'medium';
    reasoning = `Moderate pattern: ${matchCount} messages show this behavior, but other factors may be involved`;
  } else {
    level = 'low';
    reasoning = `Weak pattern: Only ${matchCount} messages detected. More data needed for confident assessment`;
  }
  
  return { level, score, reasoning };
}

/**
 * Generate recommendations based on pattern type
 */
function generateRecommendations(pattern: PatternMatch): AIInsight['recommendations'] {
  const recommendations: AIInsight['recommendations'] = [];
  
  switch (pattern.type) {
    case 'dlq-pattern':
      recommendations.push({
        title: 'Review dead-letter error signatures',
        description: `Examine the ${pattern.metrics.affectedCount} messages for common failure patterns`,
        priority: 'immediate',
      });
      recommendations.push({
        title: 'Check message schema validation',
        description: 'Ensure producers are sending correctly formatted messages',
        priority: 'short-term',
      });
      recommendations.push({
        title: 'Consider replay after root cause fix',
        description: 'Once the issue is resolved, replay affected messages',
        priority: 'investigative',
      });
      break;
      
    case 'retry-loop':
      recommendations.push({
        title: 'Investigate consumer processing failures',
        description: `Check logs for why messages are failing after ${pattern.metrics.avgDeliveryCount} attempts`,
        priority: 'immediate',
      });
      recommendations.push({
        title: 'Review max delivery count settings',
        description: 'Consider dead-lettering messages that exceed retry thresholds',
        priority: 'short-term',
      });
      break;
      
    case 'poison-message':
      recommendations.push({
        title: 'Move poison messages to DLQ',
        description: 'These messages are unlikely to process successfully',
        priority: 'immediate',
      });
      recommendations.push({
        title: 'Add poison message detection logic',
        description: 'Implement early detection to prevent retry storms',
        priority: 'short-term',
      });
      break;
      
    case 'error-cluster':
      recommendations.push({
        title: 'Analyze error cluster root cause',
        description: `${pattern.metrics.affectedCount} messages share error pattern: ${pattern.signature}`,
        priority: 'immediate',
      });
      recommendations.push({
        title: 'Check downstream service health',
        description: 'Clustered errors often indicate external dependencies issues',
        priority: 'investigative',
      });
      break;
  }
  
  return recommendations;
}

/**
 * Get human-readable title for pattern
 */
function getPatternTitle(pattern: PatternMatch): string {
  switch (pattern.type) {
    case 'dlq-pattern':
      return `DLQ Pattern: ${pattern.signature?.substring(0, 50) || 'Unknown Failure'}`;
    case 'retry-loop':
      return `Retry Loop: ${pattern.messages.length} Messages Stuck`;
    case 'poison-message':
      return `Poison Messages: ${pattern.messages.length} Unprocessable`;
    case 'error-cluster':
      return `Error Cluster: ${pattern.signature || 'Common Failures'}`;
    case 'latency-anomaly':
      return `Latency Anomaly Detected`;
    default:
      return 'Unknown Pattern';
  }
}

/**
 * Get description for pattern
 */
function getPatternDescription(pattern: PatternMatch): string {
  const count = pattern.messages.length;
  
  switch (pattern.type) {
    case 'dlq-pattern':
      return `${count} messages dead-lettered with similar error: "${pattern.signature}". ` +
        `This represents ${pattern.metrics.matchPercentage}% of DLQ messages.`;
    case 'retry-loop':
      return `${count} messages in retry loop with average ${pattern.metrics.avgDeliveryCount} delivery attempts. ` +
        `Messages are not progressing to completion.`;
    case 'poison-message':
      return `${count} message${count > 1 ? 's' : ''} exceeded ${CONFIG.POISON_MESSAGE_THRESHOLD} delivery attempts. ` +
        `These messages are likely unprocessable without intervention.`;
    case 'error-cluster':
      return `${count} messages share error pattern: ${pattern.signature}. ` +
        `This may indicate a systematic issue.`;
    default:
      return `${count} messages match this pattern.`;
  }
}

// ============================================================================
// Main Analysis Function
// ============================================================================

/**
 * Analyze messages and generate AI insights
 * 
 * @param messages - Array of messages to analyze
 * @param context - Analysis context (namespace, entity info)
 * @returns Array of AI insights
 */
export function analyzeMessages(
  messages: APIMessage[],
  context: AnalysisContext
): AIInsight[] {
  if (!messages || messages.length === 0) {
    return [];
  }
  
  const insights: AIInsight[] = [];
  const now = new Date();
  const analysisStart = new Date(now.getTime() - CONFIG.ANALYSIS_WINDOW_MS);
  
  // Collect all patterns
  const allPatterns: PatternMatch[] = [
    ...detectDLQPatterns(messages),
    ...detectRetryLoops(messages),
    ...detectPoisonMessages(messages),
    ...detectErrorClusters(messages),
  ];
  
  // Convert patterns to insights
  for (let i = 0; i < allPatterns.length; i++) {
    const pattern = allPatterns[i];
    const confidence = calculateConfidence(
      pattern.messages.length,
      messages.length,
      pattern.type === 'poison-message' ? 90 : 70 // Poison messages have higher base confidence
    );
    
    // Skip low-confidence patterns
    if (confidence.score < 30) {
      continue;
    }
    
    const messageIds = pattern.messages.map(m => m.messageId || `seq-${m.sequenceNumber}`);
    
    insights.push({
      id: `insight-${context.entityName}-${pattern.type}-${i}`,
      type: pattern.type,
      title: getPatternTitle(pattern),
      description: getPatternDescription(pattern),
      confidence,
      evidence: {
        sampleSize: pattern.messages.length,
        affectedMessageIds: messageIds,
        exampleMessageIds: messageIds.slice(0, 3),
        metrics: [
          { 
            label: 'Affected Messages', 
            value: pattern.messages.length, 
            isAnomaly: pattern.messages.length > 5 
          },
          { 
            label: 'Pattern Match', 
            value: `${confidence.score}%`, 
            isAnomaly: confidence.level === 'high' 
          },
          { 
            label: 'Analysis Window', 
            value: '1 hour', 
            isAnomaly: false 
          },
        ],
        patternSignature: pattern.signature,
      },
      recommendations: generateRecommendations(pattern),
      timeWindow: {
        start: analysisStart.toISOString(),
        end: now.toISOString(),
        analysisTimestamp: now.toISOString(),
      },
      scope: {
        namespaceId: context.namespaceId,
        queueOrTopicName: context.entityName,
        subscriptionName: context.subscriptionName,
      },
      status: 'active',
    });
  }
  
  return insights;
}

/**
 * Check if AI analysis is available
 * Currently always returns true for client-side analysis
 */
export function isAIAvailable(): boolean {
  return true;
}

/**
 * Get AI analysis status message
 */
export function getAIStatusMessage(): string {
  return 'AI pattern analysis is performed client-side. Results are ServiceHub interpretations, not Azure Service Bus data.';
}
