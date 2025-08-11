// Svelte stores for chat state management
import { writable, derived, get } from 'svelte/store';
import type { ChatDto, CreateChatRequest, TextMessageDto } from '$lib/types/chat';
import { apiClient } from '$lib/api/client';

// SSE envelope typing for current server format only
type SsePayload = {
	userMessageId?: string;
	assistantMessageId?: string;
	userTimestamp?: string;
	assistantTimestamp?: string;
	userSequenceNumber?: number;
	assistantSequenceNumber?: number;
	delta?: string;
	content?: string;
	visibility?: string;
	message?: string;
};

// Helper function for safe property access
function isObject(value: unknown): value is Record<string, unknown> {
	return typeof value === 'object' && value !== null;
}

// Chat state
export const currentChatId = writable<string | null>(null);
export const chats = writable<ChatDto[]>([]);
export const currentChat = writable<ChatDto | null>(null);
export const isLoading = writable(false);
export const error = writable<string | null>(null);
export const isStreaming = writable(false);
export const currentStreamingMessage = writable('');
export const currentReasoningMessage = writable('');
export const currentReasoningVisibility = writable<string | null>(null);

// User state (mock for now - will be replaced with actual auth)
export const currentUser = writable({
	id: 'user-123',
	name: 'Demo User',
	email: 'demo@example.com'
});

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
		try {
			isLoading.set(true);
			error.set(null);

			const user = get(currentUser);
			const response = await apiClient.getChatHistory(user.id);
			chats.set(response.chats);
		} catch (err) {
			error.set(err instanceof Error ? err.message : 'Failed to load chat history');
			console.error('Failed to load chat history:', err);
		} finally {
			isLoading.set(false);
		}
	},

	// Select a chat
	async selectChat(chatId: string): Promise<void> {
		try {
			isLoading.set(true);
			error.set(null);

			// Load chat data
			const chat = await apiClient.getChat(chatId);
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
		let assistantMessageId = '';

		try {
			error.set(null);
			isStreaming.set(true);
			currentStreamingMessage.set('');
			currentReasoningMessage.set('');
			currentReasoningVisibility.set(null);

			// Create request object
			const request = {
				userId: user.id,
				message: message,
				systemPrompt: systemPrompt
			};

			// Start streaming
			const response = await apiClient.streamChatCompletion(request);

			if (!response.body) {
				throw new Error('No response body');
			}

			// Process SSE events
			const reader = response.body.getReader();
			const decoder = new TextDecoder();
			let buffer = '';

			try {
				while (true) {
					const { done, value } = await reader.read();

					if (done) {
						break;
					}

					buffer += decoder.decode(value, { stream: true });

					// Process complete SSE events
					const lines = buffer.split('\n\n');
					buffer = lines.pop() || '';

					for (const line of lines) {
						{
							// Parse SSE event (ignore leading id: lines)
							const eventLines = line.split('\n');
							let eventType = '';
							let eventData = '';

							for (const eventLine of eventLines) {
								if (eventLine.startsWith('event:')) {
									eventType = eventLine.substring(6).trim();
								} else if (eventLine.startsWith('data:')) {
									eventData = eventLine.substring(5).trim();
								}
							}

							try {
								const data: Record<string, unknown> = JSON.parse(eventData);

								// Handle current server envelope format only
								if (eventType && data && typeof data === 'object' && 'kind' in data) {
									const kind = data.kind as string;
									const payload = (
										isObject(data.payload) ? (data.payload as SsePayload) : {}
									) as SsePayload;

									switch (eventType) {
										case 'init': {
											assistantMessageId = String(data.messageId);
											const userMessage: TextMessageDto = {
												id: payload.userMessageId || String(data.userMessageId),
												chatId: String(data.chatId),
												role: 'user',
												text: message,
												timestamp: new Date(payload.userTimestamp || String(data.ts)),
												sequenceNumber: payload.userSequenceNumber ?? 0
											};
											const assistantPlaceholder: TextMessageDto = {
												id: assistantMessageId,
												chatId: String(data.chatId),
												role: 'assistant',
												text: '',
												timestamp: new Date(payload.assistantTimestamp || String(data.ts)),
												sequenceNumber:
													payload.assistantSequenceNumber ?? (Number(data.sequenceId) || 1)
											};

											const newChat: ChatDto = {
												id: String(data.chatId),
												userId: user.id,
												title: message.substring(0, 50) + (message.length > 50 ? '...' : ''),
												messages: [userMessage, assistantPlaceholder],
												createdAt: new Date(payload.userTimestamp || String(data.ts)),
												updatedAt: new Date(payload.assistantTimestamp || String(data.ts))
											};

											chats.update((chats) => [newChat, ...chats]);
											currentChatId.set(String(data.chatId));
											currentChat.set(newChat);
											isStreaming.set(true);
											break;
										}
										case 'messageupdate': {
											if (kind === 'text' && payload.delta) {
												currentStreamingMessage.update((msg: string) => msg + payload.delta);
											} else if (kind === 'reasoning' && payload.delta) {
												currentReasoningMessage.update((msg: string) => msg + payload.delta);
												if (payload.visibility) currentReasoningVisibility.set(payload.visibility);
											}
											break;
										}
										case 'complete': {
											const finalContent =
												kind === 'text' && typeof payload.content === 'string'
													? payload.content
													: get(currentStreamingMessage);
											const chatId = get(currentChatId);
											if (chatId && assistantMessageId) {
												currentChat.update((chat) => {
													if (!chat) return null;
													const updatedMessages = chat.messages.map((msg) =>
														msg.id === assistantMessageId ? { ...msg, text: finalContent } : msg
													);
													return { ...chat, messages: updatedMessages, updatedAt: new Date() };
												});
												chats.update((chatList) =>
													chatList.map((chat) => {
														if (chat.id !== chatId) return chat;
														const updatedMessages = chat.messages.map((msg) =>
															msg.id === assistantMessageId ? { ...msg, text: finalContent } : msg
														);
														return { ...chat, messages: updatedMessages, updatedAt: new Date() };
													})
												);
												currentStreamingMessage.set('');
												currentReasoningMessage.set('');
												currentReasoningVisibility.set(null);
											}
											reader.releaseLock();
											isStreaming.set(false);
											return;
										}
										case 'message': {
											if (kind === 'error') {
												error.set(payload.message || 'Streaming error occurred');
												reader.releaseLock();
												isStreaming.set(false);
												return;
											}
											break;
										}
									}
								} else {
									console.warn('Received SSE event in unexpected format:', { eventType, data });
								}
							} catch (parseError) {
								console.error('Failed to parse SSE data:', parseError);
							}
						}
					}
				}
			} finally {
				reader.releaseLock();
				isStreaming.set(false);
			}
		} catch (err) {
			error.set(err instanceof Error ? err.message : 'Failed to stream chat completion');
			console.error('Failed to stream chat completion:', err);
			isStreaming.set(false);
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
			isStreaming.set(true);
			currentStreamingMessage.set('');
			currentReasoningMessage.set('');
			currentReasoningVisibility.set(null);

			// Note: We'll add the user message when we receive the init event from the server
			// This ensures we use the correct timestamp from the database

			const request: CreateChatRequest = {
				chatId: chatId,
				userId: user.id,
				message: message
			};

			const response = await apiClient.streamChatCompletion(request);

			if (!response.body) {
				throw new Error('No response body');
			}

			const reader = response.body.getReader();
			const decoder = new TextDecoder();
			let buffer = '';
			let assistantMessageId = '';

			try {
				while (true) {
					const { done, value } = await reader.read();
					if (done) break;

					buffer += decoder.decode(value, { stream: true });
					const lines = buffer.split('\n\n');
					buffer = lines.pop() || '';

					for (const line of lines) {
						{
							const eventLines = line.split('\n');
							let eventType = '';
							let eventData = '';
							for (const eventLine of eventLines) {
								if (eventLine.startsWith('event:')) {
									eventType = eventLine.substring(6).trim();
								} else if (eventLine.startsWith('data:')) {
									eventData = eventLine.substring(5).trim();
								}
							}

							try {
								const data: Record<string, unknown> = JSON.parse(eventData);

								// Handle current server envelope format only
								if (eventType && data && typeof data === 'object' && 'kind' in data) {
									const kind = String(data.kind);
									const payload = (
										isObject(data.payload) ? (data.payload as SsePayload) : {}
									) as SsePayload;

									switch (eventType) {
										case 'init': {
											assistantMessageId = String(data.messageId);
											// Add user message with server timestamp
											const userMessage: TextMessageDto = {
												id: payload.userMessageId || String(data.userMessageId),
												chatId: chatId,
												role: 'user',
												text: message,
												timestamp: new Date(payload.userTimestamp || String(data.ts)),
												sequenceNumber: payload.userSequenceNumber ?? 0
											};
											const assistantPlaceholder: TextMessageDto = {
												id: assistantMessageId,
												chatId: chatId,
												role: 'assistant',
												text: '',
												timestamp: new Date(payload.assistantTimestamp || String(data.ts)),
												sequenceNumber:
													payload.assistantSequenceNumber ?? (Number(data.sequenceId) || 1)
											};
											currentChat.update((chat) =>
												chat
													? {
															...chat,
															messages: [...chat.messages, userMessage, assistantPlaceholder],
															updatedAt: new Date(payload.assistantTimestamp || String(data.ts))
														}
													: null
											);
											chats.update((chatList) =>
												chatList.map((chat) =>
													chat.id === chatId
														? {
																...chat,
																messages: [...chat.messages, userMessage, assistantPlaceholder],
																updatedAt: new Date(payload.assistantTimestamp || String(data.ts))
															}
														: chat
												)
											);
											break;
										}
										case 'messageupdate': {
											if (kind === 'text' && payload.delta) {
												currentStreamingMessage.update((msg) => msg + payload.delta);
											} else if (kind === 'reasoning' && payload.delta) {
												currentReasoningMessage.update((msg: string) => msg + payload.delta);
												if (payload.visibility) currentReasoningVisibility.set(payload.visibility);
											}
											break;
										}
										case 'complete': {
											const finalContent =
												kind === 'text' && typeof payload.content === 'string'
													? payload.content
													: get(currentStreamingMessage);
											currentChat.update((chat) => {
												if (!chat) return null;
												const updatedMessages = chat.messages.map((msg) =>
													msg.id === assistantMessageId ? { ...msg, text: finalContent } : msg
												);
												return { ...chat, messages: updatedMessages, updatedAt: new Date() };
											});
											chats.update((chatList) =>
												chatList.map((chat) => {
													if (chat.id !== chatId) return chat;
													const updatedMessages = chat.messages.map((msg) =>
														msg.id === assistantMessageId ? { ...msg, text: finalContent } : msg
													);
													return { ...chat, messages: updatedMessages, updatedAt: new Date() };
												})
											);
											currentStreamingMessage.set('');
											currentReasoningMessage.set('');
											currentReasoningVisibility.set(null);
											reader.releaseLock();
											isStreaming.set(false);
											return;
										}
										case 'message': {
											if (kind === 'error') {
												error.set(payload.message || 'Streaming error occurred');
												reader.releaseLock();
												isStreaming.set(false);
												return;
											}
											break;
										}
									}
								} else {
									console.warn('Received SSE event in unexpected format:', { eventType, data });
								}
							} catch (parseError) {
								console.error('Failed to parse SSE data:', parseError);
							}
						}
					}
				}
			} finally {
				reader.releaseLock();
				isStreaming.set(false);
			}
		} catch (err) {
			error.set(err instanceof Error ? err.message : 'Failed to stream reply');
			console.error('Failed to stream reply:', err);
			isStreaming.set(false);
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
		currentChatId.set(null);
		currentChat.set(null);
		chats.set([]);
	}
};
