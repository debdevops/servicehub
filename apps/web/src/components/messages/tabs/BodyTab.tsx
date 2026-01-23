import { useState } from 'react';
import { Copy, Check } from 'lucide-react';

// ============================================================================
// BodyTab - JSON viewer with syntax highlighting
// ============================================================================

interface BodyTabProps {
  body: string;
  contentType: string;
}

// Simple JSON syntax highlighter
function highlightJSON(json: string): React.ReactNode[] {
  const lines = json.split('\n');
  
  return lines.map((line, lineIndex) => {
    const elements: React.ReactNode[] = [];
    const remaining = line;
    let keyIndex = 0;
    
    // Simple approach: colorize the line as a whole based on content
    if (remaining.includes(':')) {
      // Line with key-value
      const colonIndex = remaining.indexOf(':');
      const key = remaining.substring(0, colonIndex);
      const value = remaining.substring(colonIndex);
      
      // Key part
      elements.push(
        <span key={`key-${keyIndex++}`} className="text-sky-400">
          {key}
        </span>
      );
      
      // Colon
      elements.push(
        <span key={`colon-${keyIndex++}`} className="text-gray-400">:</span>
      );
      
      // Value part
      const valueContent = value.substring(1);
      if (valueContent.includes('"')) {
        elements.push(
          <span key={`val-${keyIndex++}`} className="text-green-400">
            {valueContent}
          </span>
        );
      } else if (/\d/.test(valueContent)) {
        elements.push(
          <span key={`val-${keyIndex++}`} className="text-amber-400">
            {valueContent}
          </span>
        );
      } else if (/true|false|null/.test(valueContent)) {
        elements.push(
          <span key={`val-${keyIndex++}`} className="text-primary-400">
            {valueContent}
          </span>
        );
      } else {
        elements.push(
          <span key={`val-${keyIndex++}`} className="text-gray-300">
            {valueContent}
          </span>
        );
      }
    } else {
      // Line without key-value (brackets, etc.)
      elements.push(
        <span key={`line-${keyIndex++}`} className="text-gray-400">
          {remaining}
        </span>
      );
    }
    
    return (
      <div key={lineIndex} className="leading-6">
        {elements}
      </div>
    );
  });
}

export function BodyTab({ body, contentType }: BodyTabProps) {
  const [copied, setCopied] = useState(false);

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(body);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch (err) {
      console.error('Failed to copy:', err);
    }
  };

  const isJSON = contentType.includes('json');

  return (
    <div className="p-4">
      <div className="relative">
        {/* Copy Button */}
        <button
          onClick={handleCopy}
          className="absolute top-3 right-3 p-2 rounded-md bg-gray-700 hover:bg-gray-600 text-gray-300 transition-colors z-10"
          title="Copy to clipboard"
        >
          {copied ? <Check size={16} className="text-green-400" /> : <Copy size={16} />}
        </button>

        {/* Content Type Badge */}
        <div className="absolute top-3 left-3 z-10">
          <span className="px-2 py-1 text-xs font-medium rounded bg-gray-700 text-gray-300">
            {contentType}
          </span>
        </div>

        {/* Body Content */}
        <div className="bg-gray-900 rounded-lg p-4 pt-12 overflow-x-auto">
          <pre className="font-mono text-sm text-gray-100">
            {isJSON ? highlightJSON(body) : body}
          </pre>
        </div>
      </div>
    </div>
  );
}
