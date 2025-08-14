/**
 * Tool Call Message Handler
 * 
 * Handles the complete lifecycle of tool call messages:
 * - Initialization, streaming chunks, completion, and rendering
 */

import type { 
	MessageTypeHandler, 
	MessageRenderer, 
	MessageSnapshot, 
	HandlerEventListener 
} from '../messageHandlers';
import type { 
	StreamChunkEventEnvelope, 
	MessageCompleteEventEnvelope,
	ToolCallUpdateStreamChunkPayload,
	ToolCallCompletePayload
} from '../sseEventTypes';
import { 
	StreamChunkPayloadGuards, 
	MessageCompletePayloadGuards 
} from '../sseEventTypes';
import type { MessageDto } from '$lib/types/chat';
import { BaseMessageHandler } from '../messageHandlers';

/**
 * Interface for tool call data
 */
export interface ToolCallDto extends MessageDto {
	messageType: 'tool_call';
	toolCalls?: Array<{
		name: string;
		args: any;
		id?: string;
	}>;
}

/**
 * Renderer for tool call messages
 */
export class ToolCallMessageRenderer implements MessageRenderer {
	getStreamingComponent() {
		// Return Svelte component for streaming tool calls
		return null as any; // TODO: Import actual Svelte component when ready
	}
	
	getCompleteComponent() {
		// Return Svelte component for complete tool calls
		return null as any; // TODO: Import actual Svelte component when ready
	}
	
	getStreamingProps(snapshot: MessageSnapshot): Record<string, any> {
		return {
			toolCalls: snapshot.toolCalls || [],
			isStreaming: snapshot.isStreaming,
			messageId: snapshot.id
		};
	}
	
	getCompleteProps(dto: MessageDto): Record<string, any> {
		const toolCallDto = dto as ToolCallDto;
		return {
			toolCalls: toolCallDto.toolCalls || [],
			messageId: dto.id,
			timestamp: dto.timestamp
		};
	}
}

/**
 * Handler for tool call messages
 */
export class ToolCallMessageHandler extends BaseMessageHandler {
	private renderer = new ToolCallMessageRenderer();
	
	getMessageType(): string {
		return 'tools_call_update';
	}
	
	canHandle(messageType: string): boolean {
		return messageType === 'tools_call_update';
	}
	
	getRenderer(): MessageRenderer {
		return this.renderer;
	}
	
	/**
	 * Process tool call streaming chunk
	 */
	processChunk(messageId: string, envelope: StreamChunkEventEnvelope): MessageSnapshot {
		console.log('[ToolCallMessageHandler] processChunk called:', {
			messageId,
			envelopeKind: envelope.kind,
			payload: envelope.payload
		});
		
		let snapshot = this.getSnapshot(messageId);
		
		// Create snapshot if it doesn't exist
		if (!snapshot) {
			console.log('[ToolCallMessageHandler] Creating new snapshot for message:', messageId);
			snapshot = this.initializeMessage(
				messageId,
				envelope.chatId,
				new Date(envelope.ts),
				envelope.sequenceId
			);
			
			// Initialize empty tool calls array
			snapshot = this.updateSnapshot(messageId, {
				toolCalls: [],
				messageType: 'tool_call'
			});
		}
		
		// Validate payload type
		if (!StreamChunkPayloadGuards.isToolCallUpdateStreamChunk(envelope.payload)) {
			console.error('[ToolCallMessageHandler] Invalid payload for tool call message:', envelope.messageId, envelope.payload);
			throw new Error(`Invalid payload for tool call message: ${envelope.messageId}`);
		}
		
		const toolCallPayload = envelope.payload as ToolCallUpdateStreamChunkPayload;
		
		// Process tool call updates from payload
		if (toolCallPayload.toolCallUpdates && Array.isArray(toolCallPayload.toolCallUpdates)) {
				const currentToolCalls = snapshot.toolCalls || [];
				const newToolCalls = [...currentToolCalls];
				
				// Process each tool call update (simpler approach for JSON fragments)
				toolCallPayload.toolCallUpdates.forEach((toolCallUpdate: any) => {
					// For tool call updates, we expect the full structure to be provided
					// and we'll handle it as JSON fragments for progressive rendering
					const toolCall = {
						name: toolCallUpdate.name || '',
						args: toolCallUpdate.arguments || {},
						argsJson: JSON.stringify(toolCallUpdate.arguments || {}),
						id: toolCallUpdate.id
					};
					
					newToolCalls.push(toolCall);
				});
				
				console.log('[ToolCallMessageHandler] Updated tool calls:', newToolCalls);
				
				return this.updateSnapshot(messageId, {
					toolCalls: newToolCalls,
					isStreaming: true
				});
		}
		
		// If no tool call data, keep current snapshot
		return snapshot;
	}
	
	/**
	 * Complete tool call message with final content
	 */
	completeMessage(messageId: string, envelope: MessageCompleteEventEnvelope): MessageDto {
		let snapshot = this.getSnapshot(messageId);
		
		if (!snapshot) {
			throw new Error(`No snapshot found for tool call message: ${messageId}`);
		}
		
		// Validate payload type
		if (!MessageCompletePayloadGuards.isToolCallComplete(envelope.payload)) {
			throw new Error(`Invalid completion payload for tool call message: ${messageId}`);
		}
		
		const toolCallPayload = envelope.payload as ToolCallCompletePayload;
		
		// Update snapshot with final tool calls
		const finalSnapshot = this.updateSnapshot(messageId, {
			toolCalls: toolCallPayload.toolCalls,
			isStreaming: false,
			isComplete: true
		});
		
		// Convert to DTO
		const dto = this.snapshotToDto(finalSnapshot);
		this.emitEvent('message_completed', messageId, envelope.chatId, dto);
		
		return dto;
	}
	
	/**
	 * Convert tool call snapshot to DTO
	 */
	protected snapshotToDto(snapshot: MessageSnapshot): ToolCallDto {
		return {
			id: snapshot.id,
			chatId: snapshot.chatId,
			role: snapshot.role,
			toolCalls: snapshot.toolCalls || [],
			timestamp: snapshot.timestamp,
			sequenceNumber: snapshot.sequenceNumber,
			messageType: 'tool_call'
		};
	}
}

/**
 * Factory function for creating tool call message handler
 */
export function createToolCallMessageHandler(eventListener?: HandlerEventListener): ToolCallMessageHandler {
	return new ToolCallMessageHandler(eventListener);
}