# Codebase Research Notes: Manual CRUD Storage

Planned areas to inspect:
- `server/Data/AIChatDbContext.cs` for EF DbContext and entities mapping
- `server/Models/*` for domain models currently mapped via EF
- `server/Services/*` for data access usage patterns and queries
- `server/Migrations/*` for current schema and constraints
- `server/Controllers/*` endpoints relying on EF services

Findings:
- Entities (EF): `User`, `Chat`, `Message` under `server/Models/`.
  - `AIChatDbContext` exposes `DbSet<User> Users`, `DbSet<Chat> Chats`, `DbSet<Message> Messages` and configures:
    - `User`: PK `Id`, unique index on `Email`, required `Email/Name/Provider`.
    - `Chat`: PK `Id`, required `Title`, FK `UserId` with cascade delete to `User`.
    - `Message`: PK `Id`, required `Content` and `Role`, FK `ChatId` with cascade delete to `Chat`.
    - Indexes on `Message`: `(ChatId, Timestamp)` and unique `(ChatId, SequenceNumber)`.
    - Seeds two users: system and demo.
- Service layer: `IChatService` defines all operations used by API including chat CRUD, pagination, sequence numbers, SSE streaming helpers.
- `ChatService` (964 LOC) uses EF extensively:
  - Creates chats and messages, uses `_messageSequenceService.GetNextSequenceNumberAsync(chat.Id)` for per-chat monotonic sequence.
  - Eager loads messages ordered by `SequenceNumber` for single chat and history listings.
  - Persists messages with roles `user/system/assistant` and builds DTOs (`TextMessageDto`, `ReasoningMessageDto`).
  - Provides streaming initialization and chunk forwarding APIs; uses `GetMessageContentAsync(messageId)` to fetch final content.
- Controller: `ChatController` depends only on `IChatService` (good seam). Endpoints: history, get chat, create chat, send message, delete chat, and SSE streaming.
- Migrations: initial create, add demo user, add `SequenceNumber` to `Messages`. Snapshot shows current schema and indexes.

Impact summary for big-bang replacement:
- Remove EF `AIChatDbContext`, migrations, and EF usages in `ChatService`.
- Introduce a storage provider (Sqlite first) with manual SQL (ADO.NET `Microsoft.Data.Sqlite`).
- Replace data access inside `ChatService` with provider calls; keep `IChatService` API intact to avoid controller changes.
- Rework models to no longer depend on EF attributes; introduce DTO-json persistence for messages and possibly chats as needed.
- Preserve constraints:
  - Per-chat unique `SequenceNumber`.
  - Ordering by `SequenceNumber`.
  - Cascade delete of messages when deleting a chat.

Open design questions to confirm with user:
1. v1 entity scope: `Chats` and `Messages` only, keep `Users` as pre-seeded/static?
2. Message storage: Drop `Content` column and rely solely on JSON field for full DTO, keeping metadata columns (`MessageId`, `ChatId`, `Role/Kind`, `SequenceNumber`, `Timestamp`)?
3. Chat storage: Keep `Title` and timestamps as columns plus a JSON field?
4. Indexing: Maintain `(ChatId, SequenceNumber)` unique and `(ChatId, Timestamp)` non-unique; any additional indexes on `Kind`/`Role`?

## Decisions
- v1 scope confirmed: `Chats` and `Messages` only; `Users` remain seeded/static.
- Messages will not have a separate `Content` column; full content is in a JSON column, with metadata columns for ordering/filtering.
