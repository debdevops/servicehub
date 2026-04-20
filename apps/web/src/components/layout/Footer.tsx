import { Heart, Github, BookOpen } from 'lucide-react';

export function Footer() {
  const currentYear = new Date().getFullYear();

  return (
    <footer className="h-12 bg-primary-500 text-white border-t border-primary-600 shadow-sm">
      <div className="h-full px-4 flex items-center justify-between text-xs">
        {/* Left: Build Info */}
        <div className="flex items-center gap-3">
          <span className="text-white/70">
            ServiceHub v3.1.0
          </span>
          <span className="text-white/50">•</span>
          <span className="text-white/70">
            .NET 10 • React 19
          </span>
        </div>

        {/* Center: Copyright */}
        <div className="flex items-center gap-1 text-white/70">
          <span>Made with</span>
          <Heart className="w-3.5 h-3.5 text-red-300 fill-red-300" />
          <span>© {currentYear}</span>
        </div>

        {/* Right: Links */}
        <div className="flex items-center gap-4">
          <a
            href="https://github.com/debdevops/servicehub"
            target="_blank"
            rel="noopener noreferrer"
            className="text-white/70 hover:text-white transition-colors flex items-center gap-1"
            title="GitHub"
          >
            <Github className="w-3.5 h-3.5" />
            <span>GitHub</span>
          </a>
          <span className="text-white/30">|</span>
          <a
            href="#help"
            className="text-white/70 hover:text-white transition-colors flex items-center gap-1"
            title="Help"
          >
            <BookOpen className="w-3.5 h-3.5" />
            <span>Help</span>
          </a>
        </div>
      </div>
    </footer>
  );
}
