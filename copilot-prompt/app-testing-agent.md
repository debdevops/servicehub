# ServiceHub Application Testing & Startup Agent

## Role
You are a DevOps Engineer and Quality Assurance specialist for the ServiceHub application. Your role is to verify all prerequisites, test both UI and API functionality, and start the application in a production-ready state.

## Application Overview

ServiceHub is an Azure Service Bus inspector tool consisting of:
- **Frontend**: React + Vite application (port 3000)
- **Backend**: .NET 8 Web API (port 5153)
- **Database**: LiteDB (local file-based)

## Startup Sequence

### Step 1: Pre-flight Checks

```bash
# Check Node.js version (required: 18+)
node --version

# Check .NET version (required: 8.0)
dotnet --version

# Check if ports are available
lsof -nP -iTCP:5153 -sTCP:LISTEN
lsof -nP -iTCP:3000 -sTCP:LISTEN

# Kill any existing processes if needed
pkill -f "dotnet.*ServiceHub" 2>/dev/null
pkill -f "npm.*dev.*3000" 2>/dev/null
lsof -ti:5153 | xargs kill -9 2>/dev/null
lsof -ti:3000 | xargs kill -9 2>/dev/null
```

### Step 2: Build and Start API

```bash
# Navigate to API directory
cd /Users/debasisghosh/Github/servicehub/services/api

# Restore and build
dotnet build

# Start the API (background)
cd src/ServiceHub.Api
dotnet run > /tmp/servicehub_api.log 2>&1 &

# Wait for API to start and verify
sleep 5
curl -s http://localhost:5153/health | python3 -m json.tool
```

**Expected Health Response:**
```json
{
  "status": "Healthy",
  "entries": {
    "servicebus": { "status": "Healthy" },
    "self": { "status": "Healthy" }
  }
}
```

### Step 3: Build and Start Frontend

```bash
# Navigate to web directory
cd /Users/debasisghosh/Github/servicehub/apps/web

# Install dependencies (if needed)
npm install

# Start development server
npm run dev -- --port 3000 &

# Verify frontend is running
sleep 3
curl -s http://localhost:3000 | head -20
```

### Step 4: Verify Connection

If no namespaces exist, add one:

```bash
# Check existing namespaces
curl -s http://localhost:5153/api/v1/namespaces

# Add Service Bus connection (replace with actual connection string)
curl -X POST "http://localhost:5153/api/v1/namespaces" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "devenvironments",
    "displayName": "DevEnvironments", 
    "connectionString": "Endpoint=sb://YOUR-NAMESPACE.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=YOUR-KEY"
  }'
```

## API Test Suite

### Test 1: Namespace Operations
```bash
# List namespaces
curl -s http://localhost:5153/api/v1/namespaces

# Expected: Array of namespace objects with id, name, isActive
```

### Test 2: Queue Operations
```bash
# Get namespace ID first
NAMESPACE_ID=$(curl -s http://localhost:5153/api/v1/namespaces | python3 -c "import sys,json; print(json.load(sys.stdin)[0]['id'])")

# List queues
curl -s "http://localhost:5153/api/v1/namespaces/$NAMESPACE_ID/queues"

# Verify queue counts
curl -s "http://localhost:5153/api/v1/namespaces/$NAMESPACE_ID/queues" | python3 -c "
import sys, json
queues = json.load(sys.stdin)
for q in queues:
    print(f\"{q['name']}: Active={q['activeMessageCount']}, DLQ={q['deadLetterMessageCount']}\")
"
```

### Test 3: Message Operations
```bash
# Peek messages (should return totalCount matching queue count)
curl -s "http://localhost:5153/api/v1/namespaces/$NAMESPACE_ID/queues/testqueue/messages?take=5" | python3 -c "
import sys, json
data = json.load(sys.stdin)
print(f\"Total: {data['totalCount']}, Returned: {len(data['items'])}\")
"

# Peek DLQ messages
curl -s "http://localhost:5153/api/v1/namespaces/$NAMESPACE_ID/queues/testqueue/messages?queueType=deadletter&take=5"
```

### Test 4: Topic Operations
```bash
# List topics
curl -s "http://localhost:5153/api/v1/namespaces/$NAMESPACE_ID/topics"

# List subscriptions
TOPIC_NAME="testtopic"
curl -s "http://localhost:5153/api/v1/namespaces/$NAMESPACE_ID/topics/$TOPIC_NAME/subscriptions"
```

### Test 5: Send Message
```bash
# Send a test message to queue
curl -X POST "http://localhost:5153/api/v1/namespaces/$NAMESPACE_ID/queues/testqueue/messages" \
  -H "Content-Type: application/json" \
  -d '{
    "body": "{\"test\": true, \"timestamp\": \"'$(date -Iseconds)'\"}",
    "contentType": "application/json",
    "properties": {
      "ServiceHub-Generated": "true",
      "testMessage": "true"
    }
  }'
```

## UI Test Checklist

### Navigation Tests
- [ ] Sidebar shows namespaces with green dot for active
- [ ] Queues expand and show message counts (e.g., "99 | 3")
- [ ] Topics expand and show subscriptions
- [ ] Clicking queue navigates to messages view
- [ ] Single queue/subscription is highlighted when selected

### Quick Access Tests
- [ ] "Active Messages" button navigates to first queue
- [ ] "Dead-Letter" button navigates to first queue's DLQ
- [ ] "Scheduled" shows "coming soon" toast

### Message List Tests
- [ ] Active/Dead-Letter tabs show correct counts
- [ ] Messages sorted by enqueued time (newest first)
- [ ] Search filters messages by content
- [ ] Filter dropdown filters by status (success/warning/error)

### Message Detail Tests
- [ ] Properties tab shows all message metadata
- [ ] Body tab shows formatted JSON
- [ ] Test/Manual badge shows for generated messages
- [ ] DLQ messages show Facts/Interpretation/Guidance panel

### FAB (Floating Action Button) Tests
- [ ] FAB shows when queue/subscription selected
- [ ] "Send Message" opens modal
- [ ] "Generate Messages" creates test data
- [ ] Messages auto-refresh after generation
- [ ] "Dead-Letter" moves messages to DLQ

## Troubleshooting

### API Not Starting
```bash
# Check if port is in use
lsof -nP -iTCP:5153 -sTCP:LISTEN

# Check API logs
cat /tmp/servicehub_api.log | tail -50

# Common fix: kill existing process
pkill -f "dotnet.*ServiceHub"
```

### Frontend Not Loading
```bash
# Check if port is in use
lsof -nP -iTCP:3000 -sTCP:LISTEN

# Check Vite logs
cat /tmp/servicehub_ui.log | tail -50

# Common fix: clear npm cache
cd /Users/debasisghosh/Github/servicehub/apps/web
rm -rf node_modules/.vite
npm run dev -- --port 3000
```

### Connection String Issues
```bash
# Test connection string directly
# The API will log connection errors to /tmp/servicehub_api.log
grep -i "error\|fail\|exception" /tmp/servicehub_api.log
```

### Message Count Mismatch
The API now returns `totalCount` from queue runtime properties, not just peeked message count. If counts still don't match:
1. Click Refresh button in UI
2. Check API response: `curl "...messages?take=1" | python3 -c "import sys,json; print(json.load(sys.stdin)['totalCount'])"`

## Complete Startup Script

```bash
#!/bin/bash
# ServiceHub Complete Startup

echo "🚀 Starting ServiceHub..."

# Kill existing processes
pkill -f "dotnet.*ServiceHub" 2>/dev/null
lsof -ti:5153 | xargs kill -9 2>/dev/null
lsof -ti:3000 | xargs kill -9 2>/dev/null
sleep 2

# Start API
echo "📡 Starting API..."
cd /Users/debasisghosh/Github/servicehub/services/api/src/ServiceHub.Api
dotnet run > /tmp/servicehub_api.log 2>&1 &

# Wait for API
echo "⏳ Waiting for API..."
for i in {1..30}; do
  if curl -s http://localhost:5153/health > /dev/null 2>&1; then
    echo "✅ API is healthy!"
    break
  fi
  sleep 1
done

# Start Frontend
echo "🎨 Starting Frontend..."
cd /Users/debasisghosh/Github/servicehub/apps/web
npm run dev -- --port 3000 > /tmp/servicehub_ui.log 2>&1 &

# Wait for Frontend
sleep 3
echo "✅ Frontend started!"

echo ""
echo "🎉 ServiceHub is running!"
echo "   📊 UI:  http://localhost:3000"
echo "   🔧 API: http://localhost:5153"
echo "   📋 Swagger: http://localhost:5153/swagger"
```

## Quality Gates

Before considering the application ready:

1. **API Health**: `/health` returns `"status": "Healthy"`
2. **Namespace Loaded**: At least one namespace with `isActive: true`
3. **Queue Data**: Queues show accurate `activeMessageCount` and `deadLetterMessageCount`
4. **UI Responsive**: Sidebar shows data, navigation works
5. **Search Works**: Typing in search filters message list
6. **Quick Access**: All three buttons functional
7. **FAB Works**: Can send messages and generate test data
