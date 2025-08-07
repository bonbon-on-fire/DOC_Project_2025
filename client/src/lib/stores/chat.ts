// Svelte stores for chat state management
import { writable, derived, get } from 'svelte/store';
import type { ChatDto, MessageDto, CreateChatRequest } from '$lib/types/chat';
import { apiClient } from '$lib/api/client';
import { streamChat } from '$lib/api/sse-client';

// Chat state
export const currentChatId = writable<string | null>(null);
export const chats = writable<ChatDto[]>([]);
export const currentChat = writable<ChatDto | null>(null);
export const isLoading = writable(false);
export const error = writable<string | null>(null);
export const isStreaming = writable(false);
export const currentStreamingMessage = writable('');

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
    .map(m => ({ ...m, timestamp: new Date(m.timestamp) }))
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
      newChat.messages = newChat.messages.map(m => ({ ...m, timestamp: new Date(m.timestamp) }));

      // Add to chats list
      chats.update(chatList => [newChat, ...chatList]);
      
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
            if (line.startsWith('event:') || line.startsWith('data:')) {
              // Parse SSE event
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
                const data = JSON.parse(eventData);
                
                // Handle different event types
                switch (data.type) {
                  case 'init':
                  // Initialize new chat and store assistant message ID
                  assistantMessageId = data.messageId;
                  
                  // Add user message with correct server timestamp
                  const userMessage: MessageDto = {
                    id: data.userMessageId,
                    chatId: data.chatId,
                    role: 'user',
                    content: message,
                    timestamp: new Date(data.userTimestamp),
                    sequenceNumber: data.userSequenceNumber || 0
                  };
                  
                  // Add assistant placeholder for streaming
                  const assistantPlaceholder: MessageDto = {
                    id: assistantMessageId,
                    chatId: data.chatId,
                    role: 'assistant',
                    content: '',
                    timestamp: new Date(data.assistantTimestamp),
                    sequenceNumber: data.assistantSequenceNumber || 1
                  };
                  
                  const newChat: ChatDto = {
                    id: data.chatId,
                    userId: user.id,
                    title: message.substring(0, 50) + (message.length > 50 ? '...' : ''),
                    messages: [userMessage, assistantPlaceholder],
                    createdAt: new Date(data.userTimestamp),
                    updatedAt: new Date(data.assistantTimestamp)
                  };
                  
                  chats.update(chats => [newChat, ...chats]);
                  currentChatId.set(data.chatId);
                  currentChat.set(newChat);
                  break;
                    
                  case 'chunk':
                    // Add chunk to current message
                    if (data.delta) {
                      currentStreamingMessage.update((msg: string) => msg + data.delta);
                    }
                    break;
                    
                  case 'complete':
                  // Finalize message
                  const chatId = get(currentChatId);
                  if (chatId && data.content && assistantMessageId) {
                    // Update the assistant placeholder message with the final content
                    currentChat.update(chat => {
                      if (!chat) return null;
                      const updatedMessages = chat.messages.map(msg =>
                        msg.id === assistantMessageId ? { ...msg, content: data.content } : msg
                      );
                      return { ...chat, messages: updatedMessages, updatedAt: new Date() };
                    });
                    chats.update(chatList =>
                      chatList.map(chat => {
                        if (chat.id !== chatId) return chat;
                        const updatedMessages = chat.messages.map(msg =>
                          msg.id === assistantMessageId ? { ...msg, content: data.content } : msg
                        );
                        return { ...chat, messages: updatedMessages, updatedAt: new Date() };
                      })
                    );
                    currentStreamingMessage.set('');
                  }
                  break;
                    
                  case 'error':
                    error.set(data.message || 'Streaming error occurred');
                    break;
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
      chats.update(chatList => chatList.filter(chat => chat.id !== chatId));
      
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

      // Note: We'll add the user message when we receive the init event from the server
      // This ensures we use the correct timestamp from the database

      const request: CreateChatRequest = {
        chatId: chatId,
        userId: user.id,
        message: message,
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
            if (line.startsWith('event:') || line.startsWith('data:')) {
              const eventLines = line.split('\n');
              let eventData = '';
              for (const eventLine of eventLines) {
                if (eventLine.startsWith('data:')) {
                  eventData = eventLine.substring(5).trim();
                }
              }

              try {
                const data = JSON.parse(eventData);

                switch (data.type) {
                  case 'init':
                    assistantMessageId = data.messageId;
                    
                    // Add user message with correct server timestamp
                    const userMessage: MessageDto = {
                      id: data.userMessageId,
                      chatId: chatId,
                      role: 'user',
                      content: message,
                      timestamp: new Date(data.userTimestamp),
                      sequenceNumber: data.userSequenceNumber || 0
                    };
                    
                    // Add assistant placeholder
                    const assistantPlaceholder: MessageDto = {
                      id: assistantMessageId,
                      chatId: chatId,
                      role: 'assistant',
                      content: '',
                      timestamp: new Date(data.assistantTimestamp),
                      sequenceNumber: data.assistantSequenceNumber || 1
                    };
                    
                    // Update both stores with both messages
                    currentChat.update(chat => chat ? { 
                      ...chat, 
                      messages: [...chat.messages, userMessage, assistantPlaceholder],
                      updatedAt: new Date(data.assistantTimestamp)
                    } : null);
                    chats.update(chatList =>
                      chatList.map(chat =>
                        chat.id === chatId
                          ? { 
                              ...chat, 
                              messages: [...chat.messages, userMessage, assistantPlaceholder],
                              updatedAt: new Date(data.assistantTimestamp)
                            }
                          : chat
                      )
                    );
                    break;

                  case 'chunk':
                    if (data.delta) {
                      currentStreamingMessage.update(msg => msg + data.delta);
                    }
                    break;

                  case 'complete':
                    const finalContent = data.content;
                    currentChat.update(chat => {
                      if (!chat) return null;
                      const updatedMessages = chat.messages.map(msg =>
                        msg.id === assistantMessageId ? { ...msg, content: finalContent } : msg
                      );
                      return { ...chat, messages: updatedMessages, updatedAt: new Date() };
                    });
                    chats.update(chatList =>
                      chatList.map(chat => {
                        if (chat.id !== chatId) return chat;
                        const updatedMessages = chat.messages.map(msg =>
                          msg.id === assistantMessageId ? { ...msg, content: finalContent } : msg
                        );
                        return { ...chat, messages: updatedMessages, updatedAt: new Date() };
                      })
                    );
                    currentStreamingMessage.set('');
                    break;

                  case 'error':
                    error.set(data.message || 'Streaming error occurred');
                    break;
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