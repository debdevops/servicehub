# ServiceHub Frontend

**Enterprise React Application for Azure Service Bus Inspection**

> Modern, type-safe React application built with Vite, TypeScript, and Tailwind CSS. Designed for **Class-A product quality** with trust-focused UX principles.

---

## 📖 Documentation

**Complete Frontend Documentation**: See the main [Comprehensive Guide](../../docs/COMPREHENSIVE-GUIDE.md) for:
- Frontend architecture diagrams
- Component hierarchy
- State management with React Query
- Application flows (Connect → Browse → Inspect → AI Analysis)
- UI design principles

---

## 🏗️ Architecture

### Directory Structure

```
apps/web/
├── src/
│   ├── main.tsx              # Application entry point
│   ├── router.tsx            # React Router setup
│   ├── pages/                # Top-level route components
│   │   ├── ConnectPage.tsx   # Connection string input
│   │   ├── MessagesPage.tsx  # Main message browser
│   │   └── InsightsPage.tsx  # AI pattern insights
│   ├── components/           # Reusable UI components
│   │   ├── layout/           # Shell, sidebar, header
│   │   ├── messages/         # Message list, detail panel
│   │   ├── ai/               # AI insights components
│   │   ├── fab/              # Floating action button
│   │   └── grid/             # Virtualized grid
│   ├── hooks/                # Custom React hooks
│   │   ├── useNamespaces.ts  # Namespace management
│   │   ├── useQueues.ts      # Queue data fetching
│   │   ├── useMessages.ts    # Message operations
│   │   └── useInsights.ts    # AI pattern hooks
│   ├── lib/                  # Utilities & configuration
│   │   ├── api/              # API client (Axios)
│   │   ├── queryClient.ts    # React Query config
│   │   └── utils.ts          # Helper functions
│   └── styles/
│       └── index.css         # Global styles & Tailwind
├── public/                   # Static assets
├── index.html                # HTML template
├── vite.config.ts            # Vite configuration
├── tsconfig.json             # TypeScript config
└── package.json              # Dependencies & scripts
```

### Technology Stack

| Technology | Version | Purpose |
|-----------|---------|---------|
| **React** | 18.3 | UI library with concurrent features |
| **TypeScript** | 5.7 | Type safety & IntelliSense |
| **Vite** | 6.0 | Build tool & dev server |
| **React Query** | 5.62 | Server state management |
| **React Router** | 7.1 | Client-side routing |
| **Tailwind CSS** | 3.4 | Utility-first styling |
| **Axios** | 1.7 | HTTP client |
| **Lucide React** | 0.468 | Icon library |
| **React Hot Toast** | 2.4 | Notifications |
| **@tanstack/react-virtual** | 3.11 | Virtualized lists |
| **Prism React Renderer** | 2.4 | Syntax highlighting |

---

## 🚀 Getting Started

### Prerequisites

- **Node.js 20+** — [Download](https://nodejs.org/)
- **npm 10+** — Comes with Node.js

### Installation

```bash
# Navigate to frontend directory
cd apps/web

# Install dependencies
npm install

# Start development server
npm run dev
```

The application will be available at **http://localhost:3000**

### Development Commands

```bash
# Start dev server with HMR
npm run dev

# Build for production
npm run build

# Preview production build
npm run preview

# Run linter
npm run lint

# Type-check without building
npm run type-check
```

---

## 🎨 UI Design Principles

### Class-A Quality Standards

ServiceHub follows **trust-focused UX principles** to achieve enterprise-grade quality:

#### 1. **Clear Fact vs. Inference Separation**

**Problem**: Users can't tell what's from Azure vs. ServiceHub's analysis  
**Solution**: Visual tags distinguish data sources

```typescript
// Properties displayed with source tags
<PropertyItem 
  label="Message ID" 
  value={message.messageId}
  tag="AZURE DATA"  // Clear source indicator
/>

<PropertyItem 
  label="Status" 
  value="Dead-Letter"
  tag="ANALYSIS"  // ServiceHub assessment
/>
```

#### 2. **AI Transparency**

**Problem**: Users don't know how AI categorizes messages  
**Solution**: Tooltips explain classification logic

```typescript
<Badge variant={severity}>
  {severity}
  <HelpCircle size={12} />  // Shows explanation on hover
</Badge>

// Tooltip content:
// "High Priority: Exceptions, critical errors, or timeout patterns"
```

#### 3. **Error Message Guidance**

**Problem**: Generic errors leave users stuck  
**Solution**: Include recovery steps

```typescript
// ❌ Bad
throw new Error("Connection failed");

// ✅ Good
throw new Error(
  "Could not connect to API. " +
  "Please check: 1) API is running (localhost:5153), " +
  "2) CORS is configured, 3) Connection string is valid"
);
```

#### 4. **Empty State Clarity**

**Problem**: "No data" doesn't explain why  
**Solution**: Different states for different scenarios

```typescript
// AI Insights Tab
{!hasAIService && (
  <EmptyState 
    icon={BotOff}
    title="AI Service Not Available"
    description="Enable AI in backend configuration"
  />
)}

{hasAIService && patterns.length === 0 && (
  <EmptyState 
    icon={Sparkles}
    title="No Patterns Detected Yet"
    description="AI will analyze as more messages arrive"
  />
)}
```

#### 5. **Dangerous Action Safety**

**Problem**: Accidental deletions/dangerous operations  
**Solution**: Auto-focus "Cancel" button in danger dialogs

```typescript
<ConfirmDialog
  title="Delete All Messages?"
  description="This cannot be undone"
  variant="danger"
  onConfirm={handleDelete}
  onCancel={handleCancel}
  autoFocusCancel={true}  // Prevents Enter-key accidents
/>
```

---

## 🔄 State Management

### React Query Configuration

ServiceHub uses **React Query** for all server state:

```typescript
// lib/queryClient.ts
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,        // 30 seconds
      cacheTime: 5 * 60 * 1000, // 5 minutes
      refetchOnWindowFocus: false,
      retry: 1,
    },
  },
});
```

### Query Keys Pattern

```typescript
// Hierarchical query keys for granular invalidation
const queryKeys = {
  namespaces: ['namespaces'] as const,
  queues: (namespace: string) => ['queues', namespace] as const,
  messages: (namespace: string, queue: string) => 
    ['messages', namespace, queue] as const,
  insights: (namespace: string) => ['insights', namespace] as const,
};

// Usage
const { data: queues } = useQuery({
  queryKey: queryKeys.queues(namespace),
  queryFn: () => fetchQueues(namespace),
});
```

### Custom Hooks

All API interactions are abstracted into custom hooks:

```typescript
// hooks/useMessages.ts
export function useMessages(namespace: string, queueName: string) {
  return useQuery({
    queryKey: ['messages', namespace, queueName],
    queryFn: () => client.getMessages(namespace, queueName),
    enabled: !!namespace && !!queueName,
  });
}

// hooks/useReplayMessage.ts
export function useReplayMessage() {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: (messageId: string) => client.replayMessage(messageId),
    onSuccess: () => {
      // Invalidate messages cache to show updated DLQ count
      queryClient.invalidateQueries({ queryKey: ['messages'] });
      toast.success('Message replayed successfully');
    },
  });
}
```

---

## 🧩 Key Components

### 1. MessagesPage (Main View)

**Location**: [src/pages/MessagesPage.tsx](src/pages/MessagesPage.tsx)

**Responsibilities**:
- Orchestrates sidebar, message list, and detail panel
- Manages selected message state
- Handles namespace/queue selection
- Provides message actions (send, replay, generate test)

**Key Features**:
- Virtualized message list (handles 10,000+ messages)
- Active/Dead-letter tabs
- Real-time updates via React Query
- Responsive layout (sidebar collapsible on mobile)

### 2. MessageList Component

**Location**: [src/components/messages/MessageList.tsx](src/components/messages/MessageList.tsx)

**Responsibilities**:
- Displays paginated/virtualized message list
- Shows status badges (Normal/Retried/Dead-Letter)
- AI pattern indicators
- Message selection

**Performance**:
- Uses `@tanstack/react-virtual` for efficient rendering
- Only renders visible rows
- Handles 100,000+ messages without lag

### 3. MessageDetailPanel

**Location**: [src/components/messages/MessageDetailPanel.tsx](src/components/messages/MessageDetailPanel.tsx)

**Tabs**:
1. **Properties** — Metadata with AZURE DATA / ANALYSIS tags
2. **Body** — JSON syntax highlighting
3. **AI Insights** — Pattern membership + recommendations
4. **Headers** — System and custom headers

**Features**:
- Copy message ID
- Replay button (DLQ messages only)
- Timestamp formatting
- Visual separation of Azure data vs. ServiceHub analysis

### 4. AIInsightsTab

**Location**: [src/components/ai/AIInsightsTab.tsx](src/components/ai/AIInsightsTab.tsx)

**Features**:
- Pattern membership badges
- Severity indicators with tooltips
- AI recommendations
- Empty states (Not Available vs. No Patterns)

### 5. Sidebar Navigation

**Location**: [src/components/layout/Sidebar.tsx](src/components/layout/Sidebar.tsx)

**Structure**:
- Namespace selector
- Queue tree view
- Topic/subscription hierarchy
- Message counts (active + DLQ)
- AI pattern count badges

---

## 🎨 Styling Guidelines

### Tailwind CSS Configuration

```typescript
// tailwind.config.js
export default {
  theme: {
    extend: {
      colors: {
        primary: {
          50: '#f0f9ff',   // Sky blue scale
          500: '#0ea5e9',  // Primary brand color
          600: '#0284c7',  // Hover states
        },
      },
      animation: {
        'fade-in': 'fadeIn 200ms ease-in',
        'slide-up': 'slideUp 300ms ease-out',
      },
    },
  },
};
```

### Component Styling Patterns

```typescript
// Consistent spacing and sizing
<div className="space-y-4">  {/* Vertical spacing */}
  <Card className="p-6">     {/* Card padding */}
    <h3 className="text-lg font-semibold mb-4">Title</h3>
    <p className="text-sm text-gray-600">Description</p>
  </Card>
</div>

// Responsive design
<div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
  {/* Responsive grid */}
</div>
```

---

## 🔐 API Integration

### Axios Client Configuration

```typescript
// lib/api/client.ts
import axios from 'axios';

export const client = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || 'http://localhost:5153',
  timeout: 30000,
  headers: {
    'Content-Type': 'application/json',
  },
});

// Request interceptor
client.interceptors.request.use((config) => {
  const token = localStorage.getItem('api_token');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// Response interceptor
client.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      // Handle unauthorized
      window.location.href = '/connect';
    }
    return Promise.reject(error);
  }
);
```

### Environment Variables

```bash
# .env.local
VITE_API_BASE_URL=http://localhost:5153
VITE_ENABLE_MOCK_DATA=false
```

---

## 🧪 Testing Strategy

### Unit Tests

```bash
# Install testing dependencies
npm install -D vitest @testing-library/react @testing-library/jest-dom

# Run tests
npm run test
```

### Component Testing Pattern

```typescript
// MessageList.test.tsx
import { render, screen } from '@testing-library/react';
import { QueryClientProvider } from '@tanstack/react-query';
import { MessageList } from './MessageList';

test('renders message list with status badges', () => {
  render(
    <QueryClientProvider client={testQueryClient}>
      <MessageList messages={mockMessages} />
    </QueryClientProvider>
  );
  
  expect(screen.getByText('Normal')).toBeInTheDocument();
  expect(screen.getByText('Dead-Letter')).toBeInTheDocument();
});
```

---

## 📦 Build & Deployment

### Production Build

```bash
# Build optimized bundle
npm run build

# Output: dist/
# - index.html
# - assets/
#   - index.[hash].js
#   - index.[hash].css
```

### Build Configuration

```typescript
// vite.config.ts
export default defineConfig({
  build: {
    outDir: 'dist',
    sourcemap: false,
    rollupOptions: {
      output: {
        manualChunks: {
          'react-vendor': ['react', 'react-dom', 'react-router-dom'],
          'query-vendor': ['@tanstack/react-query'],
        },
      },
    },
  },
});
```

### Environment-Specific Builds

```bash
# Development
npm run build -- --mode development

# Staging
npm run build -- --mode staging

# Production
npm run build -- --mode production
```

### Deployment Options

1. **Static Hosting** (Vercel, Netlify, Azure Static Web Apps)
   ```bash
   # Build and deploy
   npm run build
   # Upload dist/ to hosting provider
   ```

2. **Docker**
   ```dockerfile
   FROM node:20-alpine
   WORKDIR /app
   COPY package*.json ./
   RUN npm ci --production
   COPY . .
   RUN npm run build
   CMD ["npm", "run", "preview"]
   ```

3. **Azure App Service**
   - See [Deployment Guide](../../services/api/DEPLOYMENT_OPERATIONS.md)

---

## 🐛 Troubleshooting

### Common Issues

#### Issue: "Cannot connect to API"

**Solution**:
1. Check API is running: `curl http://localhost:5153/health`
2. Verify CORS configuration in API
3. Check browser console for detailed error

#### Issue: "Messages not loading"

**Solution**:
1. Open DevTools → Network tab
2. Check `/api/messages` request
3. Verify connection string is valid
4. Check Azure Service Bus permissions

#### Issue: "AI Insights not showing"

**Solution**:
1. Verify AI service is configured in backend
2. Check `/api/insights` endpoint
3. Ensure enough messages for pattern detection

---

## 📚 Additional Resources

- **[Comprehensive Guide](../../docs/COMPREHENSIVE-GUIDE.md)** — Complete documentation with diagrams
- **[API Documentation](../../services/api/README.md)** — Backend API reference
- **[Architecture Details](../../services/api/ARCHITECTURE.md)** — System design
- **[Deployment Guide](../../services/api/DEPLOYMENT_OPERATIONS.md)** — Production deployment

---

## 🤝 Contributing

### Development Workflow

1. Create feature branch
2. Make changes
3. Add tests
4. Run linter: `npm run lint`
5. Build: `npm run build`
6. Submit PR

### Code Style

- **TypeScript strict mode** enabled
- **ESLint** for linting
- **Prettier** for formatting (configured in VSCode)
- **Class-A quality standards** — See design principles above

---

**Built with ❤️ for debugging Azure Service Bus**

*Last Updated: January 26, 2026*
