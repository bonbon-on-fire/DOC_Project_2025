/**
 * Server-Side Event (SSE) Types
 * 
 * This module contains TypeScript types that exactly match the server's
 * SSE event envelope structures defined in AIChat.Server.Models.SSE.
 * 
 * These types ensure type safety when parsing SSE events from the server
 * and prevent mismatches between client and server event structures.
 */

// ============================================================================
// Base SSE Event Envelope Types
// ============================================================================

/**
 * Base interface for all SSE event envelopes
 * Matches: AIChat.Server.Models.SSE.SSEEventEnvelope
 */
export interface SSEEventEnvelope {
	chatId: string;
	version: number;
	ts: string; // ISO timestamp from server
	kind: string;
}

// ============================================================================
// Initialization Events
// ============================================================================

/**
 * Payload for initialization events
 * Matches: AIChat.Server.Models.SSE.InitPayload
 */
export interface InitPayload {
	userMessageId: string;
	userTimestamp: string; // ISO timestamp
	userSequenceNumber: number;
}

/**
 * Envelope for initialization events
 * Matches: AIChat.Server.Models.SSE.InitEventEnvelope
 */
export interface InitEventEnvelope extends SSEEventEnvelope {
	kind: 'meta';
	payload: InitPayload;
}

// ============================================================================
// Stream Chunk Events (Delta Updates)
// ============================================================================

/**
 * Base payload for streaming chunk events
 * Matches: AIChat.Server.Models.SSE.StreamChunkPayload
 */
export interface StreamChunkPayload {
	delta: string;
}

/**
 * Payload for text streaming chunks
 * Matches: AIChat.Server.Models.SSE.TextStreamChunkPayload
 */
export interface TextStreamChunkPayload extends StreamChunkPayload {
	done: boolean;
}

/**
 * Payload for reasoning streaming chunks
 * Matches: AIChat.Server.Models.SSE.ReasoningStreamChunkPayload
 */
export interface ReasoningStreamChunkPayload extends StreamChunkPayload {
	visibility?: string; // 'plain' | 'summary' | 'encrypted'
}

/**
 * Envelope for streaming chunk events (delta updates)
 * Matches: AIChat.Server.Models.SSE.StreamChunkEventEnvelope
 */
export interface StreamChunkEventEnvelope extends SSEEventEnvelope {
	messageId: string;
	sequenceId: number;
	payload: TextStreamChunkPayload | ReasoningStreamChunkPayload;
}

// ============================================================================
// Message Complete Events
// ============================================================================

/**
 * Base payload for message completion events
 * Matches: AIChat.Server.Models.SSE.MessageCompletePayload
 */
export interface MessageCompletePayload {
	// Base class - no additional properties
}

/**
 * Payload for completed text messages
 * Matches: AIChat.Server.Models.SSE.TextCompletePayload
 */
export interface TextCompletePayload extends MessageCompletePayload {
	text: string;
}

/**
 * Payload for completed reasoning messages
 * Matches: AIChat.Server.Models.SSE.ReasoningCompletePayload
 */
export interface ReasoningCompletePayload extends MessageCompletePayload {
	reasoning: string;
	visibility?: string; // 'plain' | 'summary' | 'encrypted'
}

/**
 * Payload for usage information
 * Matches: AIChat.Server.Models.SSE.UsageCompletePayload
 */
export interface UsageCompletePayload extends MessageCompletePayload {
	usage: Record<string, unknown>;
}

/**
 * Envelope for complete message events
 * Matches: AIChat.Server.Models.SSE.MessageCompleteEventEnvelope
 */
export interface MessageCompleteEventEnvelope extends SSEEventEnvelope {
	messageId: string;
	sequenceId: number;
	payload: TextCompletePayload | ReasoningCompletePayload | UsageCompletePayload;
}

// ============================================================================
// Stream Completion Events
// ============================================================================

/**
 * Envelope for stream completion events
 * Matches: AIChat.Server.Models.SSE.StreamCompleteEventEnvelope
 */
export interface StreamCompleteEventEnvelope extends SSEEventEnvelope {
	kind: 'complete';
	// No additional properties beyond base
}

// ============================================================================
// Error Events
// ============================================================================

/**
 * Payload for error events
 * Matches: AIChat.Server.Models.SSE.ErrorPayload
 */
export interface ErrorPayload {
	message: string;
	code?: string;
}

/**
 * Envelope for error events
 * Matches: AIChat.Server.Models.SSE.ErrorEventEnvelope
 */
export interface ErrorEventEnvelope extends SSEEventEnvelope {
	messageId?: string;
	sequenceId?: number;
	kind: 'error';
	payload: ErrorPayload;
}

// ============================================================================
// Union Types for Type-Safe Event Processing
// ============================================================================

/**
 * Union type for all SSE event envelopes
 * Enables type-safe parsing of different SSE event types
 */
export type SSEEventEnvelopeUnion =
	| InitEventEnvelope
	| StreamChunkEventEnvelope
	| MessageCompleteEventEnvelope
	| StreamCompleteEventEnvelope
	| ErrorEventEnvelope;

/**
 * Type guards for SSE event envelopes
 */
export namespace SSEEventGuards {
	export function isInitEvent(envelope: SSEEventEnvelope): envelope is InitEventEnvelope {
		return envelope.kind === 'meta' && 'payload' in envelope;
	}

	export function isStreamChunkEvent(envelope: SSEEventEnvelope): envelope is StreamChunkEventEnvelope {
		if (!('messageId' in envelope) || !('sequenceId' in envelope) || !('payload' in envelope)) return false;
		const payload: any = (envelope as any).payload;
		// Heuristics: chunk payloads include delta; text chunk also has done flag present (boolean)
		return typeof payload === 'object' && payload != null && 'delta' in payload && (
			'done' in payload || 'visibility' in payload || Object.keys(payload).length === 1 // delta-only
		);
	}

	export function isMessageCompleteEvent(envelope: SSEEventEnvelope): envelope is MessageCompleteEventEnvelope {
		if (!('messageId' in envelope) || !('sequenceId' in envelope) || !('payload' in envelope)) return false;
		const payload: any = (envelope as any).payload;
		// Completion payloads have final fields like text, reasoning, or usage
		return typeof payload === 'object' && payload != null && (
			'text' in payload || 'reasoning' in payload || 'usage' in payload
		);
	}

	export function isStreamCompleteEvent(envelope: SSEEventEnvelope): envelope is StreamCompleteEventEnvelope {
		return envelope.kind === 'complete';
	}

	export function isErrorEvent(envelope: SSEEventEnvelope): envelope is ErrorEventEnvelope {
		return envelope.kind === 'error' && 'payload' in envelope;
	}
}

/**
 * Payload type guards for streaming chunks
 */
export namespace StreamChunkPayloadGuards {
	export function isTextStreamChunk(payload: StreamChunkPayload): payload is TextStreamChunkPayload {
		// Treat as text when it has no reasoning-specific field.
		// Some backends may omit 'done' on intermediate chunks; default to text.
		return 'done' in payload || !('visibility' in (payload as any));
	}

	export function isReasoningStreamChunk(payload: StreamChunkPayload): payload is ReasoningStreamChunkPayload {
		return 'visibility' in (payload as any);
	}
}

/**
 * Payload type guards for message completion
 */
export namespace MessageCompletePayloadGuards {
	export function isTextComplete(payload: MessageCompletePayload): payload is TextCompletePayload {
		return 'text' in payload;
	}

	export function isReasoningComplete(payload: MessageCompletePayload): payload is ReasoningCompletePayload {
		return 'reasoning' in payload;
	}

	export function isUsageComplete(payload: MessageCompletePayload): payload is UsageCompletePayload {
		return 'usage' in payload;
	}
}
