# User Feedback and Learnings: Manual CRUD Storage

## Initial Request (captured)
- Replace Entity Framework with simple table management and manual CRUD operations.
- Use JSON-based columns to store non-metadata fields (e.g., full message DTO).
- Dedicated columns for key metadata like MessageId, ChatId, UserId, SequenceNumber, TimeStamp, Kind, etc.
- Introduce a provider layer abstraction for different databases; implement Sqlite first.
- Scope currently focused on Sqlite only.

## Open Questions
- Should EF be fully removed immediately, or phased out behind a feature flag with side-by-side operation during migration?
- What entities are in scope for v1 (e.g., Users, Chats, Messages, Attachments)?
- What are the must-have operations for v1 (reads, writes, listing, filtering, pagination)?
- Where should the Sqlite DB file live and how should it be configured?
- Do we need migration tooling or will schemas be recreated on startup when versions change?

## Learnings (to be filled as we iterate)
- 

## Decisions to date
- Big-bang replacement: remove EF entirely in this effort.
- v1 scope: migrate `Chats` and `Messages`; keep `Users` seeded/static.
- Message storage: drop `Content` column; persist full `MessageDto` (including polymorphic types) in a JSON column; keep metadata columns (Id, ChatId, UserId if needed, Role, SequenceNumber, Timestamp, Kind if applicable).
- Chat storage: add `ChatJson` JSON extension column to store non-critical and evolving chat metadata.
- Messages: include dedicated `Kind` column (NOT NULL) for quick filtering/indexing in addition to `Role`.
- Test mode: skip Test-only reset endpoint in v1; restarting server resets in-memory DB.
- IDs: keep GUID-as-string identifiers as currently used by models.
