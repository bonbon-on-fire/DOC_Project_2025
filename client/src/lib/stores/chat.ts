// Svelte stores for chat state management
import { writable, derived, get } from 'svelte/store';
import type { ChatDto, MessageDto } from '$lib/types/chat';
import { apiClient } from '$lib/api/client';
import { chatHub, chatHubMessages, chatHubStreamingMessages } from '$lib/signalr/chat-hub';

// Chat state
export const currentChatId = writable<string | null>(null);
export const chats = writable<ChatDto[]>([]);
export const currentChat = writable<ChatDto | null>(null);
export const isLoading = writable(false);
export const error = writable<string | null>(null);

// User state (mock for now - will be replaced with actual auth)
export const currentUser = writable({
  id: 'user-123',
  name: 'Demo User',
  email: 'demo@example.com'
});

// Derived store for current chat messages
export const currentChatMessages = derived(
  [currentChat, chatHubMessages, chatHubStreamingMessages],
  ([chat, hubMessages, streamingMessages]) => {
    if (!chat) return [];
    
    // Combine stored messages with real-time messages
    const allMessages = [...chat.messages];
    
    // Add messages from SignalR hub that belong to current chat
    const hubChatMessages = hubMessages.filter(msg => msg.chatId === chat.id);
    hubChatMessages.forEach(hubMsg => {
      // Only add if not already in chat messages
      if (!allMessages.some(msg => msg.id === hubMsg.id)) {
        allMessages.push({
          id: hubMsg.id,
          chatId: hubMsg.chatId,
          role: hubMsg.role as 'user' | 'assistant' | 'system',
          content: hubMsg.content,
          timestamp: new Date(hubMsg.timestamp)
        });
      }
    });

    // Sort by timestamp
    return allMessages.sort((a, b) => 
      new Date(a.timestamp).getTime() - new Date(b.timestamp).getTime()
    );
  }
);

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

      // Leave current chat group if connected
      const prevChatId = get(currentChatId);
      if (prevChatId && prevChatId !== chatId) {
        await chatHub.leaveChatGroup(prevChatId);
      }

      // Clear previous messages
      chatHub.clearMessages();

      // Load chat data
      const chat = await apiClient.getChat(chatId);
      currentChat.set(chat);
      currentChatId.set(chatId);

      // Join new chat group
      await chatHub.joinChatGroup(chatId);
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

  // Send message in current chat
  async sendMessage(message: string): Promise<void> {
    const chatId = get(currentChatId);
    const user = get(currentUser);
    
    if (!chatId) {
      throw new Error('No chat selected');
    }

    try {
      error.set(null);
      await chatHub.sendMessage(chatId, user.id, message);
    } catch (err) {
      error.set(err instanceof Error ? err.message : 'Failed to send message');
      console.error('Failed to send message:', err);
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
        await chatHub.leaveChatGroup(chatId);
        currentChatId.set(null);
        currentChat.set(null);
        chatHub.clearMessages();
      }
    } catch (err) {
      error.set(err instanceof Error ? err.message : 'Failed to delete chat');
      console.error('Failed to delete chat:', err);
    } finally {
      isLoading.set(false);
    }
  },

  // Clear error
  clearError(): void {
    error.set(null);
  },

  // Initialize chat system
  async initialize(): Promise<void> {
    try {
      // Connect to SignalR hub
      await chatHub.connect();
      
      // Load chat history
      await chatActions.loadChatHistory();
    } catch (err) {
      error.set(err instanceof Error ? err.message : 'Failed to initialize chat system');
      console.error('Failed to initialize chat system:', err);
    }
  },

  // Cleanup
  async cleanup(): Promise<void> {
    await chatHub.disconnect();
    currentChatId.set(null);
    currentChat.set(null);
    chats.set([]);
    chatHub.clearMessages();
  }
};