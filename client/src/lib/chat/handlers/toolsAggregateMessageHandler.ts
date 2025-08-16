/**
 * Tools Aggregate Message Handler
 * 
 * Handles messages that combine tool calls with their results.
 * Supports incremental updates of both calls and results.
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
	ToolResultStreamChunkPayload,
	ToolCallUpdate
} from '../sseEventTypes';
import { 
	StreamChunkPayloadGuards, 
	MessageCompletePayloadGuards 
} from '../sseEventTypes';
import type { MessageDto, ToolCall, ToolCallResult, ToolsCallAggregateMessageDto } from '$lib/types/chat';
import { BaseMessageHandler } from '../messageHandlers';
import ToolsCallAggregateRenderer from '$lib/components/ToolsCallAggregateRenderer.svelte';

/**
 * Renderer for tools aggregate messages
 */
export class ToolsAggregateMessageRenderer implements MessageRenderer {
	getStreamingComponent() {
		return ToolsCallAggregateRenderer;
	}
	
	getCompleteComponent() {
		return ToolsCallAggregateRenderer;
	}
	
	getStreamingProps(snapshot: MessageSnapshot): Record<string, any> {
		return {
			message: {
				...snapshot,
				toolCalls: snapshot.toolCalls || [],
				toolResults: snapshot.toolResults || [],
				messageType: 'tools_aggregate'
			} as ToolsCallAggregateMessageDto,
			isStreaming: snapshot.isStreaming
		};
	}
	
	getCompleteProps(dto: MessageDto): Record<string, any> {
		const aggregateDto = dto as ToolsCallAggregateMessageDto;
		return {
			message: aggregateDto,
			isStreaming: false
		};
	}
}

/**
 * Handler for tools aggregate messages
 */
export class ToolsAggregateMessageHandler extends BaseMessageHandler {
	private renderer = new ToolsAggregateMessageRenderer();
	private toolCallsMap = new Map<string, Map<string, ToolCall>>();
	private toolResultsMap = new Map<string, Map<string, ToolCallResult>>();
	
	getMessageType(): string {
		return 'tools_aggregate';
	}
	
	canHandle(messageType: string): boolean {
		// Handle aggregate messages and tool results
		return messageType === 'tools_aggregate' || 
		       messageType === 'tools_call_update' || 
		       messageType === 'tool_result';
	}
	
	getRenderer(): MessageRenderer {
		return this.renderer;
	}
	
	/**
	 * Process streaming chunk (tool call updates or results)
	 */
	processChunk(messageId: string, envelope: StreamChunkEventEnvelope): MessageSnapshot {
		console.log('[ToolsAggregateMessageHandler] processChunk called:', {
			messageId,
			envelopeKind: envelope.kind,
			payload: envelope.payload
		});
		
		let snapshot = this.getSnapshot(messageId);
		
		// Create snapshot if it doesn't exist
		if (!snapshot) {
			console.log('[ToolsAggregateMessageHandler] Creating new snapshot for message:', messageId);
			snapshot = this.initializeMessage(
				messageId,
				envelope.chatId,
				new Date(envelope.ts),
				envelope.sequenceId
			);
			
			// Initialize empty arrays
			snapshot = this.updateSnapshot(messageId, {
				toolCalls: [],
				toolResults: [],
				messageType: 'tools_aggregate'
			});
			
			// Initialize maps for this message
			this.toolCallsMap.set(messageId, new Map());
			this.toolResultsMap.set(messageId, new Map());
		}
		
		// Handle tool call updates
		if (envelope.kind === 'tools_call_update') {
			if (!StreamChunkPayloadGuards.isToolCallUpdateStreamChunk(envelope.payload)) {
				console.error('[ToolsAggregateMessageHandler] Invalid tool call update payload');
				return snapshot;
			}
			
			const payload = envelope.payload as ToolCallUpdateStreamChunkPayload;
			if (payload.toolCallUpdate) {
				this.processToolCallUpdate(messageId, payload.toolCallUpdate);
			}
		}
		
		// Handle tool result updates
		else if (envelope.kind === 'tool_result') {
			if (!StreamChunkPayloadGuards.isToolResultStreamChunk(envelope.payload)) {
				console.error('[ToolsAggregateMessageHandler] Invalid tool result payload');
				return snapshot;
			}
			
			const payload = envelope.payload as ToolResultStreamChunkPayload;
			this.processToolResult(messageId, payload);
		}
		
		// Update snapshot with current state
		const toolCalls = this.toolCallsMap.get(messageId);
		const toolResults = this.toolResultsMap.get(messageId);
		
		return this.updateSnapshot(messageId, {
			toolCalls: toolCalls ? Array.from(toolCalls.values()) : [],
			toolResults: toolResults ? Array.from(toolResults.values()) : [],
			isStreaming: true
		});
	}
	
	private processToolCallUpdate(messageId: string, update: ToolCallUpdate) {
		const toolCallsMap = this.toolCallsMap.get(messageId);
		if (!toolCallsMap) return;
		
		const toolCallId = update.tool_call_id || `${messageId}_tool_${update.index || 0}`;
		
		// Get or create tool call
		let toolCall = toolCallsMap.get(toolCallId) || {
			id: toolCallId,
			tool_call_id: toolCallId,
			index: update.index
		} as ToolCall;
		
		// Update with new data
		if (update.function_name) {
			toolCall.function_name = update.function_name;
			toolCall.name = update.function_name;
		}
		
		// Accumulate function arguments
		if (update.function_args) {
			const currentArgs = (toolCall.function_args || '') + update.function_args;
			toolCall.function_args = currentArgs;
			
			// Try to parse accumulated JSON
			try {
				toolCall.args = JSON.parse(currentArgs);
			} catch {
				// Keep as partial JSON string
				toolCall.args = { partial: currentArgs };
			}
		}
		
		toolCallsMap.set(toolCallId, toolCall);
		
		console.log('[ToolsAggregateMessageHandler] Updated tool call:', {
			toolCallId,
			name: toolCall.function_name,
			hasArgs: !!toolCall.args
		});
	}
	
	private processToolResult(messageId: string, result: ToolResultStreamChunkPayload) {
		const toolResultsMap = this.toolResultsMap.get(messageId);
		if (!toolResultsMap) return;
		
		const toolResult: ToolCallResult = {
			toolCallId: result.toolCallId,
			result: result.result
		};
		
		toolResultsMap.set(result.toolCallId, toolResult);
		
		console.log('[ToolsAggregateMessageHandler] Received tool result:', {
			toolCallId: result.toolCallId,
			isError: result.isError,
			resultLength: result.result.length
		});
	}
	
	/**
	 * Complete the aggregate message
	 */
	completeMessage(messageId: string, envelope: MessageCompleteEventEnvelope): MessageDto {
		console.log('[ToolsAggregateMessageHandler] Completing message:', {
			messageId,
			kind: envelope.kind,
			payload: envelope.payload
		});
		
		let snapshot = this.getSnapshot(messageId);
		
		// If no snapshot exists, create one from the complete payload
		// This happens when server sends a complete aggregate message without streaming
		if (!snapshot) {
			console.log('[ToolsAggregateMessageHandler] No snapshot found, creating from complete payload');
			
			// Initialize snapshot
			snapshot = this.initializeMessage(
				messageId,
				envelope.chatId,
				new Date(envelope.ts),
				envelope.sequenceId
			);
			
			// Extract tool calls and results from payload
			const payload = envelope.payload as any;
			const toolCalls = payload?.toolCalls || [];
			const toolResults = payload?.toolResults || [];
			
			// Update snapshot with complete data
			snapshot = this.updateSnapshot(messageId, {
				toolCalls,
				toolResults,
				messageType: 'tools_aggregate',
				isStreaming: false,
				isComplete: true
			});
			
			console.log('[ToolsAggregateMessageHandler] Created snapshot from complete payload:', {
				toolCallsCount: toolCalls.length,
				toolResultsCount: toolResults.length
			});
		} else {
			// Mark existing snapshot as complete
			snapshot = this.updateSnapshot(messageId, {
				isStreaming: false,
				isComplete: true
			});
		}
		
		// Convert to DTO
		const dto = this.snapshotToDto(snapshot);
		console.log('[ToolsAggregateMessageHandler] Final DTO:', {
			id: dto.id,
			messageType: dto.messageType,
			toolCallsCount: dto.toolCalls?.length || 0,
			toolResultsCount: dto.toolResults?.length || 0
		});
		
		this.emitEvent('message_completed', messageId, envelope.chatId, dto);
		
		// Clean up maps
		this.toolCallsMap.delete(messageId);
		this.toolResultsMap.delete(messageId);
		
		return dto;
	}
	
	/**
	 * Convert snapshot to DTO
	 */
	protected snapshotToDto(snapshot: MessageSnapshot): ToolsCallAggregateMessageDto {
		return {
			id: snapshot.id,
			chatId: snapshot.chatId,
			role: snapshot.role,
			timestamp: snapshot.timestamp,
			sequenceNumber: snapshot.sequenceNumber,
			messageType: 'tools_aggregate',
			toolCalls: snapshot.toolCalls || [],
			toolResults: snapshot.toolResults || []
		};
	}
}

/**
 * Factory function for creating tools aggregate message handler
 */
export function createToolsAggregateMessageHandler(eventListener?: HandlerEventListener): ToolsAggregateMessageHandler {
	return new ToolsAggregateMessageHandler(eventListener);
}