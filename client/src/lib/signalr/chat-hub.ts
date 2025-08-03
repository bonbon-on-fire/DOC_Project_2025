// SignalR client for real-time chat communication
import { HubConnectionBuilder, LogLevel, HubConnection } from '@microsoft/signalr';
import { writable } from 'svelte/store';

export interface StreamChunkData {
  messageId: string;
  chatId: string;
  delta: string;
  done: boolean;
}

export interface MessageData {
  id: string;
  chatId: string;
  role: string;
  content: string;
  timestamp: string;
}

export interface ErrorData {
  chatId: string;
  error: string;
  timestamp: string;
}

export interface AIResponseStartedData {
  id: string;
  chatId: string;
  role: string;
  timestamp: string;
}

export class ChatHubClient {
  private connection: HubConnection | null = null;
  private connectionUrl: string;

  // Stores for reactive updates
  public messages = writable<MessageData[]>([]);
  public streamingMessages = writable<Map<string, string>>(new Map());
  public errors = writable<ErrorData[]>([]);
  public connectionStatus = writable<'disconnected' | 'connecting' | 'connected'>('disconnected');

  constructor(hubUrl: string = 'http://localhost:5130/api/chat-hub') {
    this.connectionUrl = hubUrl;
  }

  async connect(): Promise<void> {
    if (this.connection?.state === 'Connected') {
      return;
    }

    this.connectionStatus.set('connecting');

    this.connection = new HubConnectionBuilder()
      .withUrl(this.connectionUrl)
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Information)
      .build();

    // Set up event handlers
    this.setupEventHandlers();

    try {
      await this.connection.start();
      this.connectionStatus.set('connected');
      console.log('SignalR Connected');
    } catch (error) {
      console.error('SignalR Connection failed:', error);
      this.connectionStatus.set('disconnected');
      throw error;
    }
  }

  async disconnect(): Promise<void> {
    if (this.connection) {
      await this.connection.stop();
      this.connectionStatus.set('disconnected');
      console.log('SignalR Disconnected');
    }
  }

  private setupEventHandlers(): void {
    if (!this.connection) return;

    // Handle incoming messages
    this.connection.on('ReceiveMessage', (message: MessageData) => {
      this.messages.update(messages => [...messages, message]);
    });

    // Handle AI response started
    this.connection.on('AIResponseStarted', (data: AIResponseStartedData) => {
      this.streamingMessages.update(map => {
        map.set(data.id, '');
        return new Map(map);
      });
    });

    // Handle streaming chunks
    this.connection.on('ReceiveStreamChunk', (chunk: StreamChunkData) => {
      this.streamingMessages.update(map => {
        const current = map.get(chunk.messageId) || '';
        if (chunk.done) {
          // Streaming complete, add to messages and remove from streaming
          map.delete(chunk.messageId);
          this.messages.update(messages => [...messages, {
            id: chunk.messageId,
            chatId: chunk.chatId,
            role: 'assistant',
            content: current,
            timestamp: new Date().toISOString()
          }]);
        } else {
          // Update streaming content
          map.set(chunk.messageId, current + chunk.delta);
        }
        return new Map(map);
      });
    });

    // Handle errors
    this.connection.on('ReceiveError', (error: ErrorData) => {
      this.errors.update(errors => [...errors, error]);
    });

    // Handle connection events
    this.connection.onreconnecting(() => {
      this.connectionStatus.set('connecting');
      console.log('SignalR Reconnecting...');
    });

    this.connection.onreconnected(() => {
      this.connectionStatus.set('connected');
      console.log('SignalR Reconnected');
    });

    this.connection.onclose(() => {
      this.connectionStatus.set('disconnected');
      console.log('SignalR Connection closed');
    });
  }

  async joinChatGroup(chatId: string): Promise<void> {
    if (this.connection?.state === 'Connected') {
      await this.connection.invoke('JoinChatGroup', chatId);
    }
  }

  async leaveChatGroup(chatId: string): Promise<void> {
    if (this.connection?.state === 'Connected') {
      await this.connection.invoke('LeaveChatGroup', chatId);
    }
  }

  async sendMessage(chatId: string, userId: string, message: string): Promise<void> {
    if (this.connection?.state === 'Connected') {
      await this.connection.invoke('SendMessage', chatId, userId, message);
    } else {
      throw new Error('SignalR connection not established');
    }
  }

  // Clear all data (useful when switching chats)
  clearMessages(): void {
    this.messages.set([]);
    this.streamingMessages.set(new Map());
    this.errors.set([]);
  }
}

// Export singleton instance
export const chatHub = new ChatHubClient();

// Export stores for direct access with reactive $ syntax
export const chatHubConnectionStatus = chatHub.connectionStatus;
export const chatHubMessages = chatHub.messages;
export const chatHubStreamingMessages = chatHub.streamingMessages;
export const chatHubErrors = chatHub.errors;