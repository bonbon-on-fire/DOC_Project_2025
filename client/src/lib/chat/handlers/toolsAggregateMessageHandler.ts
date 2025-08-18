/**
 * Tools Aggregate Message Handler
 * 
 * Handles messages that combine tool calls with their results.
 * Maintains a paired structure where each tool call is paired with its result.
 * Creates aggregate message immediately upon first tool call.
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
import type { 
	MessageDto, 
	ToolCall, 
	ToolCallResult, 
	ToolsCallAggregateMessageDto,
	ClientToolsCallAggregateMessageDto,
	ToolCallPair 
} from '$lib/types/chat';
import { BaseMessageHandler } from '../messageHandlers';
import ToolsCallAggregateRenderer from '$lib/components/ToolsCallAggregateRenderer.svelte';
import { JsonFragmentRebuilder } from '$lib/utils/jsonFragmentRebuilder';

/**
 * Renderer for tools aggregate messages
 */
export class ToolsAggregateMessageRenderer implements MessageRenderer {
	getStreamingComponent() {
		return ToolsCallAggregateRenderer as any;
	}
	
	getCompleteComponent() {
		return ToolsCallAggregateRenderer as any;
	}
	
	getStreamingProps(snapshot: MessageSnapshot): Record<string, any> {
		return {
			message: {
				...snapshot,
				toolCallPairs: snapshot.toolCallPairs || [],
				messageType: 'tools_aggregate'
			} as ClientToolsCallAggregateMessageDto,
			isStreaming: snapshot.isStreaming
		};
	}
	
	getCompleteProps(dto: MessageDto): Record<string, any> {
		const aggregateDto = dto as ClientToolsCallAggregateMessageDto;
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
	// Map of messageId -> Map of toolCallId -> ToolCallPair
	private toolCallPairsMap = new Map<string, Map<string, ToolCallPair>>();
	// Map of messageId -> Map of toolCallId -> JsonFragmentRebuilder
	private fragmentRebuilders = new Map<string, Map<string, JsonFragmentRebuilder>>();
	
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
		
		// Create snapshot if it doesn't exist (first tool call creates the aggregate)
		if (!snapshot) {
			console.log('[ToolsAggregateMessageHandler] Creating new aggregate message for:', messageId);
			snapshot = this.initializeMessage(
				messageId,
				envelope.chatId,
				new Date(envelope.ts),
				envelope.sequenceId
			);
			
			// Initialize with empty pairs array
			snapshot = this.updateSnapshot(messageId, {
				toolCallPairs: [],
				messageType: 'tools_aggregate'
			});
			
			// Initialize map for this message
			this.toolCallPairsMap.set(messageId, new Map());
			this.fragmentRebuilders.set(messageId, new Map());
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
		
		// Update snapshot with current paired state
		const pairsMap = this.toolCallPairsMap.get(messageId);
		const toolCallPairs = pairsMap ? Array.from(pairsMap.values()) : [];
		
		return this.updateSnapshot(messageId, {
			toolCallPairs,
			isStreaming: true
		});
	}
	
	private processToolCallUpdate(messageId: string, update: ToolCallUpdate) {
		const pairsMap = this.toolCallPairsMap.get(messageId);
		if (!pairsMap) return;
		
		const toolCallId = update.tool_call_id || `${messageId}_tool_${update.index || 0}`;
		
		// Get or create pair for this tool call
		let pair = pairsMap.get(toolCallId);
		if (!pair) {
			pair = {
				toolCall: {
					id: toolCallId,
					tool_call_id: toolCallId,
					index: update.index
				} as ToolCall,
				toolResult: undefined
			};
			pairsMap.set(toolCallId, pair);
		}

		// Apply JsonFragments if present
		if (update.json_update_fragments && update.json_update_fragments.length > 0) {
			let byMessage = this.fragmentRebuilders.get(messageId);
			if (!byMessage) {
				byMessage = new Map();
				this.fragmentRebuilders.set(messageId, byMessage);
			}
			let rebuilder = byMessage.get(toolCallId);
			if (!rebuilder) {
				rebuilder = new JsonFragmentRebuilder();
				byMessage.set(toolCallId, rebuilder);
			}
			rebuilder.apply(update.json_update_fragments);
			pair.toolCall.args = rebuilder.getValue();
		}
		
		// Update tool call data
		if (update.function_name) {
			pair.toolCall.function_name = update.function_name;
			pair.toolCall.name = update.function_name;
		}
		
		// Accumulate function arguments (fallback + persistence parity)
		if (update.function_args) {
			const currentArgs = (pair.toolCall.function_args || '') + update.function_args;
			pair.toolCall.function_args = currentArgs;
			
			// If no fragments were applied, try to parse accumulated JSON as a best-effort
			if (!(update.json_update_fragments && update.json_update_fragments.length > 0)) {
				try {
					pair.toolCall.args = JSON.parse(currentArgs);
				} catch {
					pair.toolCall.args = { partial: currentArgs };
				}
			}
		}
		
		console.log('[ToolsAggregateMessageHandler] Updated tool call in pair:', {
			toolCallId,
			name: pair.toolCall.function_name,
			hasArgs: !!pair.toolCall.args,
			hasResult: !!pair.toolResult
		});
	}
	
	private processToolResult(messageId: string, result: ToolResultStreamChunkPayload) {
		const pairsMap = this.toolCallPairsMap.get(messageId);
		if (!pairsMap) return;
		
		// Find the pair with matching toolCallId
		let pair = pairsMap.get(result.toolCallId);
		
		// If pair doesn't exist yet (shouldn't happen normally), create placeholder
		if (!pair) {
			console.warn('[ToolsAggregateMessageHandler] Received result for unknown tool call:', result.toolCallId);
			pair = {
				toolCall: {
					id: result.toolCallId,
					tool_call_id: result.toolCallId,
					function_name: 'unknown',
					name: 'unknown'
				} as ToolCall,
				toolResult: undefined
			};
			pairsMap.set(result.toolCallId, pair);
		}
		
		// Update the result in the pair
		pair.toolResult = {
			toolCallId: result.toolCallId,
			result: result.result
		};
		
		console.log('[ToolsAggregateMessageHandler] Added result to pair:', {
			toolCallId: result.toolCallId,
			isError: result.isError,
			resultLength: result.result.length,
			pairComplete: !!(pair.toolCall && pair.toolResult)
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
		let toolCallPairs: ToolCallPair[] = [];
		
		// If we have a snapshot from streaming, use its pairs
		if (snapshot) {
			const pairsMap = this.toolCallPairsMap.get(messageId);
			if (pairsMap) {
				toolCallPairs = Array.from(pairsMap.values());
			}
		}
		
		// If server sends a complete aggregate, transform it to paired format
		if (envelope.kind === 'tools_aggregate' || envelope.kind === 'tools_call') {
			const payload = envelope.payload as any;
			
			if (payload?.toolCalls || payload?.toolResults) {
				// Transform server's array format to paired format
				const serverToolCalls = payload.toolCalls || [];
				const serverToolResults = payload.toolResults || [];
				
				// Create a map of results by toolCallId for easy lookup
				const resultsMap = new Map<string, ToolCallResult>();
				for (const result of serverToolResults) {
					// Handle both toolCallId and tool_call_id formats from server
					const toolCallId = result.toolCallId || result.tool_call_id;
					if (toolCallId) {
						resultsMap.set(toolCallId, result);
					}
				}
				
				// Build pairs from server data
				const serverPairs: ToolCallPair[] = serverToolCalls.map((toolCall: ToolCall) => {
					const toolCallId = toolCall.id || toolCall.tool_call_id || `tool_${toolCall.index}`;
					const result = resultsMap.get(toolCallId);
					
					console.log('[ToolsAggregateMessageHandler] Pairing tool call with result:', {
						toolCallId,
						toolName: toolCall.function_name || toolCall.name,
						hasResult: !!result,
						resultValue: result?.result?.substring(0, 50)
					});
					
					return {
						toolCall,
						toolResult: result
					};
				});
				
				// If we don't have streaming pairs or server has more complete data, use server pairs
				if (toolCallPairs.length === 0 || serverPairs.length > toolCallPairs.length) {
					toolCallPairs = serverPairs;
					console.log('[ToolsAggregateMessageHandler] Using server pairs:', {
						count: serverPairs.length,
						withResults: serverPairs.filter(p => p.toolResult).length
					});
				}
			}
		}
		
		// Create or update snapshot with final pairs
		if (!snapshot) {
			snapshot = this.initializeMessage(
				messageId,
				envelope.chatId,
				new Date(envelope.ts),
				envelope.sequenceId
			);
		}
		
		snapshot = this.updateSnapshot(messageId, {
			toolCallPairs,
			messageType: 'tools_aggregate',
			isStreaming: false,
			isComplete: true
		});
		
		// Convert to DTO
		const dto = this.snapshotToDto(snapshot);
		console.log('[ToolsAggregateMessageHandler] Final DTO:', {
			id: dto.id,
			messageType: dto.messageType,
			pairsCount: dto.toolCallPairs?.length || 0,
			pairsWithResults: dto.toolCallPairs?.filter(p => p.toolResult).length || 0
		});
		
		this.emitEvent('message_completed', messageId, envelope.chatId, dto);
		
		// Clean up map
		this.toolCallPairsMap.delete(messageId);
		this.fragmentRebuilders.delete(messageId);
		
		return dto;
	}
	
	/**
	 * Convert snapshot to DTO
	 */
	protected snapshotToDto(snapshot: MessageSnapshot): ClientToolsCallAggregateMessageDto {
		return {
			id: snapshot.id,
			chatId: snapshot.chatId,
			role: snapshot.role,
			timestamp: snapshot.timestamp,
			sequenceNumber: snapshot.sequenceNumber,
			messageType: 'tools_aggregate',
			toolCallPairs: snapshot.toolCallPairs || []
		};
	}
}

/**
 * Factory function for creating tools aggregate message handler
 */
export function createToolsAggregateMessageHandler(eventListener?: HandlerEventListener): ToolsAggregateMessageHandler {
	return new ToolsAggregateMessageHandler(eventListener);
}
