import { Component, ReactNode } from 'react';

interface Props {
  children: ReactNode;
  fallback?: ReactNode;
}

interface State {
  hasError: boolean;
  error?: Error;
  errorInfo?: React.ErrorInfo;
}

// Common error patterns and user-friendly messages
const ERROR_MESSAGES: Record<string, string> = {
  'network': 'Unable to connect to the API. Please check your network connection and ensure the API server is running.',
  'unauthorized': 'Your session has expired or you are not authorized. Please reconnect to your namespace.',
  'timeout': 'The request took too long. This might indicate connectivity issues with Azure Service Bus.',
  'service bus': 'There was an issue communicating with Azure Service Bus. Please verify your connection string.',
  'connection': 'Connection lost. Please check your namespace configuration.',
};

function getErrorMessage(error: Error): string {
  const message = error.message.toLowerCase();
  
  for (const [pattern, friendlyMessage] of Object.entries(ERROR_MESSAGES)) {
    if (message.includes(pattern)) {
      return friendlyMessage;
    }
  }
  
  // Don't expose raw error messages in production
  if (import.meta.env.PROD) {
    return 'An unexpected error occurred. Please try refreshing the page.';
  }
  
  return error.message || 'An unexpected error occurred.';
}

export class ErrorBoundary extends Component<Props, State> {
  state: State = { hasError: false };

  static getDerivedStateFromError(error: Error): Partial<State> {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, errorInfo: React.ErrorInfo) {
    // Only log detailed errors in development
    if (import.meta.env.DEV) {
      console.error('ErrorBoundary caught:', error, errorInfo);
    } else {
      // In production, log minimal info without stack traces
      console.error('Application error:', error.name);
    }
    
    this.setState({ errorInfo });
  }

  handleReset = () => {
    this.setState({ hasError: false, error: undefined, errorInfo: undefined });
  };

  render() {
    if (this.state.hasError) {
      if (this.props.fallback) {
        return this.props.fallback;
      }

      return (
        <div className="flex flex-col items-center justify-center h-screen bg-gray-50">
          <div className="text-center max-w-md p-8 bg-white rounded-lg shadow-lg">
            <div className="text-6xl mb-4">⚠️</div>
            <h1 className="text-2xl font-bold text-red-600 mb-4">Something went wrong</h1>
            <p className="text-gray-600 mb-6">
              {this.state.error ? getErrorMessage(this.state.error) : 'An unexpected error occurred.'}
            </p>
            
            {/* Show technical details only in development */}
            {import.meta.env.DEV && this.state.error && (
              <details className="mb-6 text-left">
                <summary className="cursor-pointer text-sm text-gray-500 hover:text-gray-700">
                  Technical Details
                </summary>
                <pre className="mt-2 p-3 bg-gray-100 rounded text-xs overflow-auto max-h-40">
                  {this.state.error.name}: {this.state.error.message}
                  {this.state.errorInfo?.componentStack && (
                    <>\n\nComponent Stack:{this.state.errorInfo.componentStack}</>
                  )}
                </pre>
              </details>
            )}
            
            <div className="flex gap-3 justify-center">
              <button 
                onClick={this.handleReset}
                className="px-6 py-3 bg-gray-200 text-gray-700 rounded-lg hover:bg-gray-300 transition-colors"
              >
                Try Again
              </button>
              <button 
                onClick={() => window.location.reload()}
                className="px-6 py-3 bg-sky-500 text-white rounded-lg hover:bg-sky-600 transition-colors"
              >
                Reload Page
              </button>
            </div>
          </div>
        </div>
      );
    }

    return this.props.children;
  }
}
