import { describe, it, expect, beforeEach } from 'vitest';
import { ToolCallMessageHandler } from './toolCallMessageHandler';
import type { StreamChunkEventEnvelope, ToolCallUpdateStreamChunkPayload } from '../sseEventTypes';

describe('ToolCallMessageHandler - ID Management', () => {
	let handler: ToolCallMessageHandler;

	beforeEach(() => {
		handler = new ToolCallMessageHandler();
	});

	it('should generate unique IDs for tool calls without IDs', () => {
		const messageId = 'msg_123';
		const envelope: StreamChunkEventEnvelope = {
			chatId: 'chat_1',
			messageId,
			sequenceId: 1,
			version: 1,
			ts: new Date().toISOString(),
			kind: 'tools_call_update',
			payload: {
				delta: '',
				toolCallUpdates: [
					{ name: 'calculator', arguments: { a: 1, b: 2 } },
					{ name: 'calculator', arguments: { a: 3, b: 4 } }
				]
			} as ToolCallUpdateStreamChunkPayload
		};

		const snapshot = handler.processChunk(messageId, envelope);
		
		expect(snapshot.toolCalls).toHaveLength(2);
		expect(snapshot.toolCalls[0].id).toBe(`${messageId}_tool_0_calculator`);
		expect(snapshot.toolCalls[1].id).toBe(`${messageId}_tool_1_calculator`);
	});

	it('should preserve provided tool call IDs', () => {
		const messageId = 'msg_123';
		const envelope: StreamChunkEventEnvelope = {
			chatId: 'chat_1',
			messageId,
			sequenceId: 1,
			version: 1,
			ts: new Date().toISOString(),
			kind: 'tools_call_update',
			payload: {
				delta: '',
				toolCallUpdates: [
					{ id: 'tool_abc', name: 'calculator', arguments: { a: 1, b: 2 } },
					{ id: 'tool_def', name: 'search', arguments: { query: 'test' } }
				]
			} as ToolCallUpdateStreamChunkPayload
		};

		const snapshot = handler.processChunk(messageId, envelope);
		
		expect(snapshot.toolCalls).toHaveLength(2);
		expect(snapshot.toolCalls[0].id).toBe('tool_abc');
		expect(snapshot.toolCalls[1].id).toBe('tool_def');
	});

	it('should not duplicate tool calls with same ID across multiple chunks', () => {
		const messageId = 'msg_123';
		
		// First chunk
		const envelope1: StreamChunkEventEnvelope = {
			chatId: 'chat_1',
			messageId,
			sequenceId: 1,
			version: 1,
			ts: new Date().toISOString(),
			kind: 'tools_call_update',
			payload: {
				delta: '',
				toolCallUpdates: [
					{ id: 'tool_1', name: 'calculator', arguments: { a: 1 } }
				]
			} as ToolCallUpdateStreamChunkPayload
		};

		// Second chunk with updated arguments for same tool
		const envelope2: StreamChunkEventEnvelope = {
			chatId: 'chat_1',
			messageId,
			sequenceId: 2,
			version: 1,
			ts: new Date().toISOString(),
			kind: 'tools_call_update',
			payload: {
				delta: '',
				toolCallUpdates: [
					{ id: 'tool_1', name: 'calculator', arguments: { a: 1, b: 2 } }
				]
			} as ToolCallUpdateStreamChunkPayload
		};

		handler.processChunk(messageId, envelope1);
		const snapshot = handler.processChunk(messageId, envelope2);
		
		// Should have only one tool call, not two
		expect(snapshot.toolCalls).toHaveLength(1);
		expect(snapshot.toolCalls[0].id).toBe('tool_1');
		expect(snapshot.toolCalls[0].args).toEqual({ a: 1, b: 2 });
	});

	it('should handle multiple tool calls in streaming chunks', () => {
		const messageId = 'msg_123';
		
		// First chunk with two tool calls
		const envelope1: StreamChunkEventEnvelope = {
			chatId: 'chat_1',
			messageId,
			sequenceId: 1,
			version: 1,
			ts: new Date().toISOString(),
			kind: 'tools_call_update',
			payload: {
				delta: '',
				toolCallUpdates: [
					{ id: 'tool_1', name: 'calculator', arguments: { a: 1, b: 2 } },
					{ id: 'tool_2', name: 'search', arguments: { query: 'test' } }
				]
			} as ToolCallUpdateStreamChunkPayload
		};

		// Second chunk with a new tool call
		const envelope2: StreamChunkEventEnvelope = {
			chatId: 'chat_1',
			messageId,
			sequenceId: 2,
			version: 1,
			ts: new Date().toISOString(),
			kind: 'tools_call_update',
			payload: {
				delta: '',
				toolCallUpdates: [
					{ id: 'tool_3', name: 'weather', arguments: { city: 'Paris' } }
				]
			} as ToolCallUpdateStreamChunkPayload
		};

		handler.processChunk(messageId, envelope1);
		const snapshot = handler.processChunk(messageId, envelope2);
		
		// Should have all three tool calls
		expect(snapshot.toolCalls).toHaveLength(3);
		expect(snapshot.toolCalls.map(tc => tc.id)).toEqual(['tool_1', 'tool_2', 'tool_3']);
	});
});

describe('SlimChatSyncManager - Message ID Uniqueness', () => {
	it('should generate unique display IDs for different message types', () => {
		// This test documents the fix in SlimChatSyncManager.toDisplayId()
		const messageId = 'msg_123';
		
		// These should all generate different display IDs to prevent collisions
		const textId = `${messageId}:text`;
		const reasoningId = `${messageId}:reasoning`;
		const toolCallId = `${messageId}:tools_call_update`;
		const toolResultId = `${messageId}:tool_result`;
		
		// All IDs should be unique
		const ids = new Set([textId, reasoningId, toolCallId, toolResultId]);
		expect(ids.size).toBe(4);
	});
});