// Svelte stores for chat state management - Handler-Based Architecture
import { writable, derived, get } from 'svelte/store';
import type { ChatDto, CreateChatRequest } from '$lib/types/chat';
import type { StreamingUIState } from '$lib/chat/types';
import { HandlerBasedSSEOrchestrator } from '$lib/chat/handlerBasedOrchestrator';
import { apiClient } from '$lib/api/client';
import { logger } from '$lib/utils/logger';

// Chat state stores
export const currentChatId = writable<string | null>(null);
export const chats = writable<ChatDto[]>([]);
export const currentChat = writable<ChatDto | null>(null);
export const isLoading = writable(false);
export const error = writable<string | null>(null);

// Streaming state store - now properly typed
export const streamingState = writable<StreamingUIState>({
	isStreaming: false,
	currentMessageId: null,
	streamingSnapshots: {},
	error: null
});

// Legacy stores for backward compatibility
export const isStreaming = derived(streamingState, $state => $state.isStreaming);
export const currentStreamingMessageId = derived(streamingState, $state => $state.currentMessageId);
export const streamingSnapshots = derived(streamingState, $state => $state.streamingSnapshots);

// User state (mock for now - will be replaced with actual auth)
export const currentUser = writable({
	id: 'user-123',
	name: 'Demo User',
	email: 'demo@example.com'
});

// Handler-Based SSE Orchestrator - implements new handler architecture
let sseOrchestrator: HandlerBasedSSEOrchestrator | null = null;

function getOrchestrator(): HandlerBasedSSEOrchestrator {
	if (!sseOrchestrator) {
		sseOrchestrator = new HandlerBasedSSEOrchestrator(
			currentChat,
			chats,
			streamingState,
			() => get(currentUser).id,
			currentChatId
		);
	}
	return sseOrchestrator;
}

// Derived store for current chat messages
export const currentChatMessages = derived([currentChat], ([chat]) => {
	if (!chat) return [];
	// Sort messages by sequence number
	return chat.messages
		.map((m) => ({ ...m, timestamp: new Date(m.timestamp) }))
		.sort((a, b) => a.sequenceNumber - b.sequenceNumber);
});

// Chat actions
export const chatActions = {
	// Load chat history
	async loadChatHistory(): Promise<void> {
		logger.trace({ operation: 'loadChatHistory' }, 'Starting to load chat history');
		try {
			isLoading.set(true);
			error.set(null);

			const user = get(currentUser);
			logger.trace({ userId: user.id, operation: 'loadChatHistory' }, 'Loading chat history for user');
			
			const response = await apiClient.getChatHistory(user.id);
			logger.trace({ 
				chatCount: response.chats?.length || 0, 
				operation: 'loadChatHistory' 
			}, 'Chat history loaded successfully');
			
			chats.set(response.chats);
		} catch (err) {
			const errorMessage = err instanceof Error ? err.message : 'Failed to load chat history';
			error.set(errorMessage);
			logger.error({ error: err, operation: 'loadChatHistory' }, 'Failed to load chat history');
		} finally {
			isLoading.set(false);
			logger.trace({ operation: 'loadChatHistory' }, 'Finished loading chat history');
		}
	},

	// Select a chat
	async selectChat(chatId: string): Promise<void> {
		try {
			isLoading.set(true);
			error.set(null);

			// Load chat data with all messages
			const chat = await apiClient.getChat(chatId);
			
			// Ensure message timestamps are properly converted to Date objects
			chat.messages = chat.messages.map((m) => ({ 
				...m, 
				timestamp: new Date(m.timestamp) 
			}));
			
			currentChat.set(chat);
			currentChatId.set(chatId);
		} catch (err) {
			error.set(err instanceof Error ? err.message : 'Failed to load chat');
			console.error('Failed to select chat:', err);
		} finally {
			isLoading.set(false);
		}
	},

	// Create new chat
	async createChat(message: string): Promise<string | null> {
		try {
			isLoading.set(true);
			error.set(null);

			const user = get(currentUser);
			const newChat = await apiClient.createChat({
				userId: user.id,
				message,
				systemPrompt: undefined
			});

			// Convert timestamp strings to Date objects
			newChat.messages = newChat.messages.map((m) => ({ ...m, timestamp: new Date(m.timestamp) }));

			// Add to chats list
			chats.update((chatList) => [newChat, ...chatList]);

			// Select the new chat
			await chatActions.selectChat(newChat.id);

			return newChat.id;
		} catch (err) {
			error.set(err instanceof Error ? err.message : 'Failed to create chat');
			console.error('Failed to create chat:', err);
			return null;
		} finally {
			isLoading.set(false);
		}
	},

	async streamNewChat(message: string, systemPrompt?: string): Promise<void> {
		const user = get(currentUser);

		try {
			error.set(null);

			// Use the event-driven SSE orchestrator
			const orchestrator = getOrchestrator();
			await orchestrator.streamNewChat(message);

		} catch (err) {
			error.set(err instanceof Error ? err.message : 'Failed to stream chat completion');
			console.error('Failed to stream chat completion:', err);
			streamingState.update(state => ({ ...state, isStreaming: false }));
			throw err;
		}
	},

	// Delete chat
	async deleteChat(chatId: string): Promise<void> {
		try {
			isLoading.set(true);
			error.set(null);

			await apiClient.deleteChat(chatId);

			// Remove from chats list
			chats.update((chatList) => chatList.filter((chat) => chat.id !== chatId));

			// If this was the current chat, clear selection
			const currentId = get(currentChatId);
			if (currentId === chatId) {
				currentChatId.set(null);
				currentChat.set(null);
			}
		} catch (err) {
			error.set(err instanceof Error ? err.message : 'Failed to delete chat');
			console.error('Failed to delete chat:', err);
		} finally {
			isLoading.set(false);
		}
	},

	async streamReply(message: string): Promise<void> {
		const user = get(currentUser);
		const chatId = get(currentChatId);

		if (!chatId) {
			error.set('No active chat selected');
			return;
		}

		try {
			error.set(null);

			// Ensure we have the current chat loaded with all its messages
			const existingChat = get(currentChat);
			if (!existingChat || existingChat.id !== chatId) {
				console.log('Reloading chat before streaming reply');
				await chatActions.selectChat(chatId);
			}

			// Use the event-driven SSE orchestrator
			const orchestrator = getOrchestrator();
			await orchestrator.streamReply(message, chatId);

		} catch (err) {
			error.set(err instanceof Error ? err.message : 'Failed to stream reply');
			console.error('Failed to stream reply:', err);
			streamingState.update(state => ({ ...state, isStreaming: false }));
		}
	},

	clearError(): void {
		error.set(null);
	},

	// Initialize chat system
	async initialize(): Promise<void> {
		try {
			// Load chat history
			await chatActions.loadChatHistory();
		} catch (err) {
			error.set(err instanceof Error ? err.message : 'Failed to initialize chat system');
			console.error('Failed to initialize chat system:', err);
		}
	},

	// Cleanup
	async cleanup(): Promise<void> {
		// Cleanup orchestrator
		if (sseOrchestrator) {
			await sseOrchestrator.cleanup();
			sseOrchestrator = null; // Reset so next getOrchestrator() creates fresh instance
		}

		// Reset state
		currentChatId.set(null);
		currentChat.set(null);
		chats.set([]);
		streamingState.set({
			isStreaming: false,
			currentMessageId: null,
			streamingSnapshots: {},
			error: null
		});
	}
};
