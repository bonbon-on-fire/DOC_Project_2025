// Copy of shared types for client-side use

// UI-level message and chat types (optional; not used directly by API client)
export interface Message {
	id: string;
	chatId: string;
	role: 'user' | 'assistant' | 'system';
	// No generic 'content' at the boundary; UI may map from text/reasoning
	timestamp: Date | string;
	sequenceNumber: number;
}

export interface Chat {
	id: string;
	userId: string;
	title: string;
	messages: Message[];
	createdAt: Date | string;
	updatedAt: Date | string;
}

// Requests
// SSE and REST share this shape; SSE accepts optional chatId for continuation
export interface CreateChatRequest {
	chatId?: string;
	userId: string;
	message: string;
	systemPrompt?: string;
}

// Legacy/unused types removed: ContinueChatRequest, ChatResponse, StreamChatResponse

// REST responses
export interface ChatHistoryResponse {
	chats: ChatDto[];
	totalCount: number;
	page: number;
	pageSize: number;
}

// DTOs for API responses (matching backend)
export interface ChatDto {
	id: string;
	userId: string;
	title: string;
	messages: (TextMessageDto | ReasoningMessageDto | ToolCallMessageDto | MessageDto)[];
	createdAt: Date | string;
	updatedAt: Date | string;
}

export interface MessageDto {
	id: string;
	chatId: string;
	role: string;
	timestamp: Date | string;
	sequenceNumber: number;
	// messageType is emitted by server due to JsonPolymorphic discriminator
	messageType?: 'text' | 'reasoning' | string;
}

export interface TextMessageDto extends MessageDto {
	text: string;
	messageType?: 'text';
}

export interface ReasoningMessageDto extends MessageDto {
	reasoning: string;
	visibility?: 'Plain' | 'Summary' | 'Encrypted';
	messageType?: 'reasoning';
}

export interface ToolCallMessageDto extends MessageDto {
	toolCalls: Array<{
		function_name?: string;
		function_args?: string;
		index?: number;
		tool_call_id?: string;
		name?: string;
		args?: any;
		id?: string;
	}>;
	messageType?: 'tool_call';
}

// Extended message type used by renderers
export interface RichMessageDto extends MessageDto {
	messageType?: 'text' | 'reasoning' | 'tool_call' | 'tool_result' | 'usage' | string;
}
