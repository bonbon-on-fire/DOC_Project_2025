// Copy of shared types for client-side use
export interface Message {
  id: string;
  chatId: string;
  role: 'user' | 'assistant' | 'system';
  content: string;
  timestamp: Date;
  sequenceNumber: number;
}

export interface Chat {
  id: string;
  userId: string;
  title: string;
  messages: Message[];
  createdAt: Date;
  updatedAt: Date;
}

export interface CreateChatRequest {
  chatId?: string;
  userId: string;
  message: string;
  systemPrompt?: string;
}

export interface ContinueChatRequest {
  chatId: string;
  message: string;
}

export interface ChatResponse {
  chatId: string;
  messageId: string;
  content: string;
  role: 'assistant';
  timestamp: Date;
}

export interface StreamChatResponse {
  chatId: string;
  messageId: string;
  delta: string;
  role: 'assistant';
  done: boolean;
}

export interface ChatHistoryResponse {
  chats: Chat[];
  totalCount: number;
  page: number;
  pageSize: number;
}

// DTOs for API responses (matching backend)
export interface ChatDto {
  id: string;
  userId: string;
  title: string;
  messages: MessageDto[];
  createdAt: Date;
  updatedAt: Date;
}

export interface MessageDto {
  id: string;
  chatId: string;
  role: string;
  content: string;
  timestamp: Date;
  sequenceNumber: number;
}