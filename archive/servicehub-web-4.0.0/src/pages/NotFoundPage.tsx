import { Link, useLocation, useNavigate } from 'react-router-dom';
import { Home, ArrowLeft, Search } from 'lucide-react';

/**
 * Rendered inside MainLayout for any unmatched path. A normal route element, not a
 * <Navigate> — the URL the user actually requested stays in the address bar.
 */
export function NotFoundPage() {
  const location = useLocation();
  const navigate = useNavigate();

  return (
    <div className="flex flex-col items-center justify-center h-full px-4 py-16">
      <div className="text-center max-w-md">
        <Search className="w-14 h-14 text-gray-300 mx-auto mb-4" />
        <h1 className="text-2xl font-bold text-gray-800 mb-2">Page not found</h1>
        <p className="text-gray-600 mb-2">
          There&apos;s nothing at{' '}
          <code className="px-2 py-1 bg-gray-100 rounded text-sm break-all">{location.pathname}</code>.
        </p>
        <p className="text-gray-600 mb-6">Check the URL, or head back to a page that exists.</p>
        <div className="flex flex-wrap gap-3 justify-center">
          <button
            onClick={() => navigate(-1)}
            className="inline-flex items-center gap-2 px-6 py-3 bg-gray-200 text-gray-700 rounded-lg hover:bg-gray-300 transition-colors"
          >
            <ArrowLeft className="w-4 h-4" />
            Go Back
          </button>
          <Link
            to="/welcome"
            className="inline-flex items-center gap-2 px-6 py-3 bg-sky-500 text-white rounded-lg hover:bg-sky-600 transition-colors"
          >
            <Home className="w-4 h-4" />
            Go Home
          </Link>
        </div>
      </div>
    </div>
  );
}
