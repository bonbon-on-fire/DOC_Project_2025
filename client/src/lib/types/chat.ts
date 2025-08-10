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
  messages: (TextMessageDto | ReasoningMessageDto | MessageDto)[];
  createdAt: Date;
  updatedAt: Date;
}

export interface MessageDto {
  id: string;
  chatId: string;
  role: string;
  timestamp: Date;
  sequenceNumber: number;
}

export interface TextMessageDto extends MessageDto {
  text: string;
}

export interface ReasoningMessageDto extends MessageDto {
  reasoning: string;
  visibility?: 'Plain' | 'Summary' | 'Encrypted';
}

/**
 * Extended MessageDto interface for Rich Message Rendering support.
 * Adds messageType field for renderer routing and future extensibility.
 */
export interface RichMessageDto extends MessageDto {
  /**
   * The type of message content for renderer routing.
   * Determines which renderer component will handle this message.
   */
  messageType?: 'text' | 'reasoning' | 'tool_call' | 'tool_result' | 'usage' | string;
}