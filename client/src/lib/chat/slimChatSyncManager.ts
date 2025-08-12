/**
 * Slim Chat Synchronization Manager
 * 
 * A lightweight orchestrator that routes SSE events to appropriate message type handlers.
 * No longer handles message specifics - just coordinates and manages chat-level state.
 */

import { writable, get, type Writable } from 'svelte/store';
import type { ChatDto, MessageDto } from '$lib/types/chat';
import type { StreamingUIState, InitEvent, ErrorEvent } from './types';
import type { 
	SSEEventEnvelopeUnion,
	InitEventEnvelope,
	StreamChunkEventEnvelope,
	MessageCompleteEventEnvelope,
	StreamCompleteEventEnvelope,
	ErrorEventEnvelope
} from './sseEventTypes';
import { SSEEventGuards } from './sseEventTypes';
import type { 
	MessageHandlerRegistry, 
	HandlerEvent, 
	HandlerEventListener 
} from './messageHandlers';

/**
 * Slim chat sync manager - just routes events to handlers
 */
export class SlimChatSyncManager implements HandlerEventListener {
	private handlerRegistry: MessageHandlerRegistry;
	
	// Svelte stores for reactive state management
	private currentChatStore: Writable<ChatDto | null>;
	private chatsStore: Writable<ChatDto[]>;
	private streamingStateStore: Writable<StreamingUIState>;
	private currentChatIdStore?: Writable<string | null>;
	// Provisional sequence numbers assigned per streaming message
	private provisionalSeqByMessageId: Map<string, number> = new Map();
	// Final sequence numbers provided by server on completion
	private finalSeqByMessageId: Map<string, number> = new Map();

	// Current state tracking
	private currentChatId: string | null = null;
	private currentUserMessage: string = '';

	constructor(
		handlerRegistry: MessageHandlerRegistry,
		currentChatStore: Writable<ChatDto | null>,
		chatsStore: Writable<ChatDto[]>,
		streamingStateStore: Writable<StreamingUIState>,
		currentChatIdStore?: Writable<string | null>
	) {
		this.handlerRegistry = handlerRegistry;
		this.currentChatStore = currentChatStore;
		this.chatsStore = chatsStore;
		this.streamingStateStore = streamingStateStore;
		this.currentChatIdStore = currentChatIdStore;
	}

	/**
	 * Process a strongly-typed SSE event
	 */
	processSSEEvent(envelope: SSEEventEnvelopeUnion): void {
		try {
			if (SSEEventGuards.isInitEvent(envelope)) {
				this.handleInitEvent(envelope);
			} else if (SSEEventGuards.isStreamChunkEvent(envelope)) {
				this.handleStreamChunkEvent(envelope);
			} else if (SSEEventGuards.isMessageCompleteEvent(envelope)) {
				this.handleMessageCompleteEvent(envelope);
			} else if (SSEEventGuards.isStreamCompleteEvent(envelope)) {
				this.handleStreamCompleteEvent(envelope);
			} else if (SSEEventGuards.isErrorEvent(envelope)) {
				this.handleErrorEvent(envelope);
			} else {
				console.warn('Unknown SSE event type:', envelope);
			}
		} catch (error) {
			console.error('Error processing SSE event:', error, envelope);
			this.handleError({
				chatId: envelope.chatId,
				messageId: 'messageId' in envelope ? envelope.messageId : undefined,
				message: error instanceof Error ? error.message : 'Unknown error',
				kind: 'error'
			});
		}
	}

	/**
	 * Handle initialization event - create user message and prepare for streaming
	 */
	private handleInitEvent(envelope: InitEventEnvelope): void {
		this.currentChatId = envelope.chatId;

		// Convert to legacy InitEvent format for compatibility
		const initEvent: InitEvent = {
			chatId: envelope.chatId,
			userMessageId: envelope.payload.userMessageId,
			userTimestamp: envelope.payload.userTimestamp,
			userSequenceNumber: envelope.payload.userSequenceNumber
		};

		this.initializeChat(initEvent, this.currentUserMessage);
	}

	/**
	 * Handle stream chunk event - route to appropriate message handler
	 */
	private handleStreamChunkEvent(envelope: StreamChunkEventEnvelope): void {
		const handler = this.handlerRegistry.getHandler(envelope.kind);
		if (!handler) {
			console.warn(`No handler found for message type: ${envelope.kind}`);
			return;
		}

		try {
			// Ensure a stable, message-level sequence number exists on the envelope so
			// handlers use the same sequence across chunks and completion.
			const seq = this.getOrAssignProvisionalSequence(envelope.chatId, envelope.messageId);
			const fixedEnvelope: StreamChunkEventEnvelope = { ...envelope, sequenceId: seq };
			const snapshot = handler.processChunk(envelope.messageId, fixedEnvelope);
			
			// Update streaming UI state based on the updated snapshot
			this.updateStreamingState(envelope.messageId, snapshot.messageType, snapshot);
		} catch (error) {
			console.error(`Error processing chunk for ${envelope.kind}:`, error);
		}
	}

	/**
	 * Handle message complete event - route to appropriate message handler
	 */
	private handleMessageCompleteEvent(envelope: MessageCompleteEventEnvelope): void {
		const handler = this.handlerRegistry.getHandler(envelope.kind);
		if (!handler) {
			console.warn(`No handler found for message type: ${envelope.kind}`);
			return;
		}

		try {
			// Cache authoritative sequence number from server for this message
			this.finalSeqByMessageId.set(envelope.messageId, envelope.sequenceId);
			// Pass through completion envelope so handlers receive the final server sequenceId
			const dto = handler.completeMessage(envelope.messageId, envelope);
			
			// Message completion is handled by the handler event listener
			// The handler will emit a 'message_completed' event that we'll handle
		} catch (error) {
			console.error(`Error completing message for ${envelope.kind}:`, error);
		}
	}

	/**
	 * Handle stream complete event - finalize all messages and clear state
	 */
	private handleStreamCompleteEvent(envelope: StreamCompleteEventEnvelope): void {
		console.log(`Stream completed for chat ${envelope.chatId}`);
		
		// Finalize any remaining messages that haven't been explicitly completed
		this.finalizeAllPendingMessages();
		// Clear sequence caches for next turn
		this.provisionalSeqByMessageId.clear();
		this.finalSeqByMessageId.clear();
		
		// Clear streaming state
		this.streamingStateStore.update(state => ({
			...state,
			isStreaming: false,
			currentMessageId: null,
			currentTextDelta: '',
			currentReasoningDelta: '',
			currentReasoningVisibility: null,
			error: null
		}));

		this.currentUserMessage = '';
	}

	/**
	 * Handle error event
	 */
	private handleErrorEvent(envelope: ErrorEventEnvelope): void {
		this.handleError({
			chatId: envelope.chatId,
			messageId: envelope.messageId,
			message: envelope.payload.message,
			kind: 'error'
		});
	}

	/**
	 * Handler event listener - receives events from message handlers
	 */
	onHandlerEvent(event: HandlerEvent): void {
		switch (event.type) {
			case 'message_created':
				this.onMessageCreated(event.messageId, event.chatId, event.data);
				break;
			case 'message_updated':
				this.onMessageUpdated(event.messageId, event.chatId, event.data);
				break;
			case 'message_completed':
				this.onMessageCompleted(event.messageId, event.chatId, event.data);
				break;
			case 'error':
				this.handleError({
					chatId: event.chatId,
					messageId: event.messageId,
					message: event.data?.message || 'Handler error',
					kind: 'error'
				});
				break;
		}
	}

	/**
	 * Handle message created by handler
	 */
	private onMessageCreated(messageId: string, chatId: string, snapshot: any): void {
		console.log(`Creating placeholder for message ${messageId} with sequence ${snapshot.sequenceNumber}`);
		
		// Create placeholder message DTO and add to chat
		const messageDto: MessageDto = {
			id: messageId,
			chatId: chatId,
			role: 'assistant',
			timestamp: snapshot.timestamp,
			sequenceNumber: snapshot.sequenceNumber,
			messageType: snapshot.messageType
		};

		this.addMessageToChat(messageDto);
	}

	/**
	 * Handle message updated by handler (streaming)
	 */
	private onMessageUpdated(messageId: string, chatId: string, snapshot: any): void {
		// Update handled by streaming state - no need to update chat DTO during streaming
	}

	/**
	 * Handle message completed by handler
	 */
	private onMessageCompleted(messageId: string, chatId: string, dto: MessageDto): void {
		console.log(`Completing message ${messageId} with sequence ${dto.sequenceNumber}, messageType: ${dto.messageType}`);
		
		// Preserve original messageType before any modifications
		const originalMessageType = dto.messageType;
		
		// Overwrite with final server-provided sequence number if available
		const finalSeq = this.finalSeqByMessageId.get(messageId);
		if (typeof finalSeq === 'number') {
			console.log(`Overriding sequence ${dto.sequenceNumber} -> ${finalSeq} for message ${messageId}`);
			(dto as any).sequenceNumber = finalSeq;
			// Ensure messageType is preserved after sequence override
			(dto as any).messageType = originalMessageType;
			this.finalSeqByMessageId.delete(messageId);
		}
		
		// Double-check that messageType is still set
		if (!dto.messageType) {
			console.warn(`MessageType missing for ${messageId}, attempting to restore from completion envelope`);
			// Try to determine messageType from the completion envelope kind
			// This is a fallback that shouldn't normally be needed
			(dto as any).messageType = originalMessageType || 'text';
		}
		
		console.log(`Final DTO for message ${messageId}:`, {
			id: dto.id,
			messageType: dto.messageType,
			role: dto.role,
			sequenceNumber: dto.sequenceNumber
		});
		
		// Check if this message already exists in chat (from placeholder creation)
		const currentChat = get(this.currentChatStore);
		const existingMessage = currentChat?.messages.find(m => m.id === messageId);
		
		if (existingMessage) {
			console.log(`Replacing existing placeholder for message ${messageId}`, {
				oldMessageType: existingMessage.messageType,
				newMessageType: dto.messageType
			});
			// Replace the placeholder with the final DTO
			this.updateMessageInChat(messageId, dto);
		} else {
			console.log(`Adding late completion for message ${messageId} (no placeholder found)`);
			// Add the completed message to chat - this handles late completions
			// where the message completed after a new message started streaming
			this.addMessageToChat(dto);
		}
		
		// Clear streaming state if this was the active message
		this.streamingStateStore.update(state => {
			if (state.currentMessageId === messageId) {
				return {
					...state,
					currentTextDelta: '',
					currentReasoningDelta: '',
					currentReasoningVisibility: null
				};
			}
			return state;
		});
	}

	// ... (keep existing initialization and state management methods)
	
	/**
	 * Set current user message for streaming context
	 */
	setCurrentUserMessage(userMessage: string): void {
		this.currentUserMessage = userMessage;
	}

	/**
	 * Initialize a new chat or continue existing chat
	 */
	initializeChat(event: InitEvent, userMessage: string): void {
		this.currentChatId = event.chatId;

		// Create user message DTO for immediate UI feedback
		const userMessageDto: MessageDto = {
			id: event.userMessageId,
			chatId: event.chatId,
			role: 'user',
			timestamp: new Date(event.userTimestamp),
			sequenceNumber: event.userSequenceNumber,
			messageType: 'text',
			text: userMessage
		} as MessageDto;

		// If chat doesn't exist, create it. Otherwise, append/update message.
		const existing = get(this.currentChatStore);
		if (!existing || existing.id !== event.chatId) {
			const newChat: ChatDto = {
				id: event.chatId,
				userId: 'unknown',
				title: userMessage.substring(0, 50) + (userMessage.length > 50 ? '...' : ''),
				messages: [userMessageDto],
				createdAt: new Date(event.userTimestamp),
				updatedAt: new Date(event.userTimestamp)
			};
			this.currentChatStore.set(newChat);
			this.chatsStore.update(list => [newChat, ...list.filter(c => c.id !== newChat.id)]);
			
			// Ensure the new chat is selected - update currentChatId store as well
			this.updateCurrentChatSelection(event.chatId);
		} else {
			// For existing chats, check if this user message already exists
			const already = existing.messages.find(m => m.id === event.userMessageId);
			if (!already) {
				// Add new user message while preserving all existing messages
				const updatedMessages = [...existing.messages, userMessageDto].sort((a,b)=>a.sequenceNumber-b.sequenceNumber);
				this.currentChatStore.update(chat => chat ? {
					...chat,
					messages: updatedMessages,
					updatedAt: new Date()
				} : chat);
				this.syncCurrentChatToChatsStore();
			}
		}

		// Set streaming state true at start
		this.streamingStateStore.update(s => ({
			...s,
			isStreaming: true,
			currentMessageId: null,
			currentTextDelta: '',
			currentReasoningDelta: '',
			currentReasoningVisibility: null,
			error: null
		}));
	}

	/**
	 * Handle errors
	 */
	handleError(error: ErrorEvent): void {
		console.error('SlimChatSyncManager error:', error);
		this.streamingStateStore.update(state => ({
			...state,
			isStreaming: false,
			error: error.message
		}));
	}

	// Helper methods for chat state management
	private addMessageToChat(messageDto: MessageDto): void {
		this.currentChatStore.update(chat => {
			if (!chat) return null;
			
			// Check if message already exists to avoid duplicates
			const exists = chat.messages.some(m => m.id === messageDto.id);
			if (exists) return chat;
			
			return {
				...chat,
				messages: [...chat.messages, messageDto].sort((a,b)=>a.sequenceNumber-b.sequenceNumber),
				updatedAt: new Date()
			};
		});
		this.syncCurrentChatToChatsStore();
	}

	private updateMessageInChat(messageId: string, dto: MessageDto): void {
		this.currentChatStore.update(chat => {
			if (!chat) return null;
			return {
				...chat,
				messages: chat.messages.map(m => m.id === messageId ? dto : m),
				updatedAt: new Date()
			};
		});
		this.syncCurrentChatToChatsStore();
	}

	/** Sync currentChat to chats store list */
	private syncCurrentChatToChatsStore(): void {
		const current = get(this.currentChatStore);
		if (!current) return;
		
		this.chatsStore.update(chats => {
			const index = chats.findIndex(c => c.id === current.id);
			if (index >= 0) {
				// Update existing chat in the list
				const updatedChats = [...chats];
				updatedChats[index] = current;
				return updatedChats;
			} else {
				// Add new chat to the beginning of the list
				return [current, ...chats];
			}
		});
	}

	private updateStreamingState(messageId: string, messageType: string, snapshot: any): void {
		console.log('[SlimChatSyncManager] updateStreamingState called:', {
			messageId,
			messageType,
			snapshotTextDelta: snapshot.textDelta,
			snapshotReasoningDelta: snapshot.reasoningDelta,
			snapshotIsStreaming: snapshot.isStreaming
		});
		
		this.streamingStateStore.update(state => {
			const next = { ...state, currentMessageId: state.currentMessageId ?? messageId };
			if (messageType === 'text') {
				next.currentTextDelta = snapshot.textDelta || '';
				console.log('[SlimChatSyncManager] Set currentTextDelta to:', next.currentTextDelta);
			} else if (messageType === 'reasoning') {
				next.currentReasoningDelta = snapshot.reasoningDelta || '';
				next.currentReasoningVisibility = snapshot.visibility ?? next.currentReasoningVisibility;
				console.log('[SlimChatSyncManager] Set currentReasoningDelta to:', next.currentReasoningDelta);
			}
			return next;
		});
	}

	private finalizeAllPendingMessages(): void {
		const chat = get(this.currentChatStore);
		if (!chat) return;
		const handlers = this.handlerRegistry.getAllHandlers();
		for (const msg of chat.messages) {
			if (msg.role !== 'assistant') continue;
			const handler = handlers.find(h => h.canHandle(msg.messageType ?? 'text'));
			if (!handler) continue;
			const dto = handler.finalizeMessage(msg.id);
			if (dto) {
				this.updateMessageInChat(msg.id, dto);
			}
		}
	}

	/** Assign or get a stable provisional sequence number for a streaming message */
	private getOrAssignProvisionalSequence(chatId: string, messageId: string): number {
		const existing = this.provisionalSeqByMessageId.get(messageId);
		if (existing !== undefined) return existing;
		
		const current = get(this.currentChatStore);
		if (!current || current.id !== chatId) {
			// No current chat or different chat - start from 0
			const seq = 0;
			this.provisionalSeqByMessageId.set(messageId, seq);
			return seq;
		}
		
		// Calculate next sequence based on current messages + already assigned provisional sequences
		const baseSeq = current.messages?.length ?? 0;
		const assignedProvisionalSeqs = Array.from(this.provisionalSeqByMessageId.values());
		const maxProvisional = assignedProvisionalSeqs.length > 0 ? Math.max(...assignedProvisionalSeqs) : baseSeq - 1;
		const seq = Math.max(baseSeq, maxProvisional + 1);
		
		this.provisionalSeqByMessageId.set(messageId, seq);
		return seq;
	}

	/**
	 * Update the current chat selection in the store
	 */
	private updateCurrentChatSelection(chatId: string): void {
		if (this.currentChatIdStore) {
			this.currentChatIdStore.set(chatId);
		}
	}
}

/**
 * Factory function for creating slim chat sync manager
 */
export function createSlimChatSyncManager(
	handlerRegistry: MessageHandlerRegistry,
	currentChatStore: Writable<ChatDto | null>,
	chatsStore: Writable<ChatDto[]>,
	streamingStateStore: Writable<StreamingUIState>,
	currentChatIdStore?: Writable<string | null>
): SlimChatSyncManager {
	return new SlimChatSyncManager(
		handlerRegistry,
		currentChatStore,
		chatsStore,
		streamingStateStore,
		currentChatIdStore
	);
}
