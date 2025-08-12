/**
 * Core Chat Interfaces
 * 
 * This module contains only the essential interfaces needed for the
 * new handler-based chat architecture. No legacy code.
 */

// ============================================================================
// Core Types for New Architecture Only
// ============================================================================

/**
 * Streaming UI state for the chat interface
 * Used to track current streaming status across all message types
 */
export interface StreamingUIState {
	isStreaming: boolean;
	currentMessageId: string | null;
	currentTextDelta: string;
	currentReasoningDelta: string;
	currentReasoningVisibility: string | null;
	error: string | null;
}

/**
 * Simple error event for the new architecture
 */
export interface ErrorEvent {
	chatId?: string;
	messageId?: string;
	message: string;
	kind: 'error';
}

/**
 * Simple initialization event for the new architecture
 */
export interface InitEvent {
	chatId: string;
	userMessageId: string;
	userTimestamp: string;
	userSequenceNumber: number;
}
