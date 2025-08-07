# Technical Overview - AI Chat Application

## Architecture Summary

### Technology Stack

**Frontend (SvelteKit)**
- **Framework**: SvelteKit 2.22 with Svelte 5.0
- **Language**: TypeScript 5.0
- **Styling**: Tailwind CSS 4.0
- **Real-time**: Microsoft SignalR 9.0.6
- **Database**: Drizzle ORM with better-sqlite3
- **Testing**: Playwright 1.49.1, Vitest 3.2.3
- **Authentication**: Argon2 hashing, Oslo crypto libraries

**Backend (ASP.NET 9.0)**
- **Framework**: ASP.NET 9.0 Web API
- **Database**: Entity Framework Core 9.0 with SQLite
- **Real-time**: SignalR Hub with extended timeouts
- **AI Integration**: AchieveAi.LmDotnetTools suite
- **Streaming**: Server-Sent Events (SSE) support
- **Caching**: File-based LLM response caching

### System Architecture

```
┌─────────────────┐    HTTP/WSS     ┌──────────────────┐
│   SvelteKit     │◄──────────────►│   ASP.NET 9.0    │
│   Frontend      │                │   Web API        │
│                 │                │                  │
│ - Chat UI       │                │ - ChatController │
│ - SignalR Client│                │ - ChatHub        │
│ - State Mgmt    │                │ - Services       │
└─────────────────┘                └──────────────────┘
                                             │
                                             ▼
                                   ┌──────────────────┐
                                   │   SQLite DB      │
                                   │                  │
                                   │ - Chats          │
                                   │ - Messages       │
                                   │ - Users          │
                                   └──────────────────┘
                                             │
                                             ▼
                                   ┌──────────────────┐
                                   │   LLM Provider   │
                                   │                  │
                                   │ - OpenAI API     │
                                   │ - Caching Layer  │
                                   └──────────────────┘
```

### Key Components

#### Backend Components

1. **ChatController** - REST API endpoints for chat management
2. **ChatHub** - SignalR hub for real-time messaging
3. **AIChatDbContext** - Entity Framework database context
4. **Services**:
   - `ChatService` - Business logic for chat operations
   - `MessageSequenceService` - Message ordering logic
   - `SseService` - Server-Sent Events handling

#### Frontend Components

1. **Chat Interface** - Main chat UI with message display
2. **Sidebar** - Conversation list and navigation
3. **Message Input** - Text input with keyboard shortcuts
4. **Real-time Connection** - SignalR client management

### Database Schema

**Chat Entity**
- Id (int, primary key)
- Title (string)
- CreatedAt (DateTime)
- UpdatedAt (DateTime)
- UserId (int, foreign key)

**Message Entity**
- Id (int, primary key)
- Content (string)
- Timestamp (DateTime)
- ChatId (int, foreign key)
- IsFromUser (bool)
- SequenceNumber (int, planned)

**User Entity**
- Id (int, primary key)
- Username (string)
- Email (string)
- PasswordHash (string)

### Communication Patterns

#### Real-time Messaging (SignalR)
- Client connects to `/api/chat-hub`
- Messages sent via `SendMessage` hub method
- Responses streamed back via SignalR groups
- Connection status monitoring

#### REST API Endpoints
- `POST /api/chat` - Create new conversation
- `GET /api/chat/history` - Get chat history
- `GET /api/chat/{id}` - Get specific chat
- `DELETE /api/chat/{id}` - Delete chat
- `POST /api/chat/{chatId}/messages` - Send message

#### Server-Sent Events (SSE)
- `POST /api/chat/stream-sse` - Alternative streaming endpoint
- Structured JSON events with proper error handling
- Event types: `init`, `chunk`, `complete`, `error`

### AI Integration

#### LLM Provider Configuration
- Uses AchieveAi.LmDotnetTools suite
- OpenAI provider with caching support
- Environment variable configuration
- File-based cache storage in `./llm-cache`

#### Streaming Implementation
- Real-time response streaming via SignalR
- Alternative SSE streaming with structured events
- Response caching for performance optimization
- Timeout handling and error recovery

### Security Considerations

#### Authentication (Planned)
- JWT-based authentication system
- Argon2 password hashing
- OAuth provider integration support

#### API Security
- CORS configuration for development
- Input validation and sanitization
- Rate limiting considerations

### Performance Optimizations

#### Caching Strategy
- LLM response caching with FileKvStore
- Configurable cache expiration (24 hours default)
- Maximum cache items limit (10,000 default)

#### Database Optimization
- Entity Framework with proper indexing
- Query optimization for message retrieval
- Pagination considerations for large conversations

### Deployment Configuration

#### Development Environment
- Hot reload for both client and server
- SQLite database for rapid development
- Comprehensive logging and debugging

#### Production Considerations
- PostgreSQL database migration planned
- Docker containerization support
- Environment-specific configuration
- Health check endpoints

### Monitoring and Diagnostics

#### Logging
- Comprehensive server-side logging
- Client-side error tracking
- LLM API usage monitoring

#### Health Checks
- `/api/health` endpoint for service monitoring
- Database connectivity checks
- External service dependency monitoring

This technical overview provides the foundation for understanding the system architecture and implementation details of the AI Chat application.
