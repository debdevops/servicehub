import { useState, useMemo } from 'react';
import { Copy, Check } from 'lucide-react';

// ============================================================================
// BodyTab - JSON viewer with syntax highlighting
// ============================================================================

interface BodyTabProps {
  body: string;
  contentType: string;
}

// Format and prettify JSON with proper indentation
function formatJSON(jsonString: string): string {
  try {
    const parsed = JSON.parse(jsonString);
    return JSON.stringify(parsed, null, 2);
  } catch (err) {
    // If parsing fails, return original
    return jsonString;
  }
}

// Enhanced JSON syntax highlighter with proper indentation and color coding
function highlightJSON(json: string): React.ReactNode {
  try {
    const lines = json.split('\n');
    
    return (
      <div className="font-mono" style={{ whiteSpace: 'pre' }}>
        {lines.map((line, lineIndex) => {
          // Preserve entire line including leading spaces
          const trimmed = line.trim();
          
          // Empty line
          if (!trimmed) {
            return <div key={lineIndex} className="leading-6" style={{ height: '1.5rem' }}>&nbsp;</div>;
          }

          // Find leading spaces count
          const leadingSpaces = line.match(/^(\s*)/)?.[1].length || 0;
          const indent = '\u00A0'.repeat(leadingSpaces); // Use non-breaking spaces for indentation

          // Render line with syntax highlighting
          const renderLine = () => {
            // Just brackets/braces/commas
            if (/^[{}\[\],]*$/.test(trimmed)) {
              return (
                <>
                  {indent}
                  <span className="text-gray-400">{trimmed}</span>
                </>
              );
            }
            
            // Key-value pair
            if (trimmed.includes(':')) {
              // Find the first colon (outside of strings)
              let colonIndex = -1;
              let inString = false;
              for (let i = 0; i < trimmed.length; i++) {
                if (trimmed[i] === '"' && (i === 0 || trimmed[i - 1] !== '\\')) {
                  inString = !inString;
                }
                if (trimmed[i] === ':' && !inString) {
                  colonIndex = i;
                  break;
                }
              }
              
              if (colonIndex > -1) {
                const keyPart = trimmed.substring(0, colonIndex);
                const valuePart = trimmed.substring(colonIndex + 1);
                
                return (
                  <>
                    <span>{indent}</span>
                    <span className="text-blue-400">{keyPart}</span>
                    <span className="text-gray-400">:</span>
                    {renderValue(valuePart)}
                  </>
                );
              }
            }
            
            // Fallback: render as-is with indentation
            return (
              <>
                {indent}
                <span className="text-gray-300">{trimmed}</span>
              </>
            );
          };

          const renderValue = (value: string) => {
            const trimmedValue = value.trim();
            
            // String value (starts and ends with quotes)
            if (trimmedValue.startsWith('"')) {
              return <span className="text-green-400">{value}</span>;
            }
            // Number
            else if (/^-?\d+\.?\d*,?$/.test(trimmedValue)) {
              return <span className="text-orange-400">{value}</span>;
            }
            // Boolean
            else if (/^(true|false),?$/.test(trimmedValue)) {
              return <span className="text-purple-400">{value}</span>;
            }
            // Null
            else if (/^null,?$/.test(trimmedValue)) {
              return <span className="text-gray-500">{value}</span>;
            }
            // Array/Object start
            else if (trimmedValue.match(/^[\[{]/)) {
              return <span className="text-gray-400">{value}</span>;
            }
            // Default
            else {
              return <span className="text-gray-300">{value}</span>;
            }
          };
          
          return (
            <div key={lineIndex} className="leading-6">
              {renderLine()}
            </div>
          );
        })}
      </div>
    );
  } catch (err) {
    return <span className="text-gray-300">{json}</span>;
  }
}

export function BodyTab({ body, contentType }: BodyTabProps) {
  const [copied, setCopied] = useState(false);
  
  const isJSON = contentType.includes('json');
  
  // Format JSON with proper indentation
  const formattedBody = useMemo(() => {
    if (isJSON) {
      return formatJSON(body);
    }
    return body;
  }, [body, isJSON]);

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(formattedBody);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch (err) {
      console.error('Failed to copy:', err);
    }
  };

  return (
    <div className="h-full flex flex-col">
      <div className="relative flex-1 flex flex-col overflow-hidden p-4">
        {/* Copy Button */}
        <button
          onClick={handleCopy}
          className="absolute top-7 right-7 p-2 rounded-md bg-gray-700 hover:bg-gray-600 text-gray-300 transition-colors z-10"
          title="Copy formatted JSON to clipboard"
        >
          {copied ? <Check size={16} className="text-green-400" /> : <Copy size={16} />}
        </button>

        {/* Content Type Badge */}
        <div className="absolute top-7 left-7 z-10">
          <span className="px-2 py-1 text-xs font-medium rounded bg-gray-700 text-gray-300">
            {contentType}
          </span>
        </div>

        {/* Body Content */}
        <div className="bg-gray-900 rounded-lg p-4 pt-12 overflow-y-auto flex-1">
          <div className="text-sm text-gray-100">
            {isJSON ? highlightJSON(formattedBody) : (
              <pre className="font-mono whitespace-pre-wrap break-words">{body}</pre>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
