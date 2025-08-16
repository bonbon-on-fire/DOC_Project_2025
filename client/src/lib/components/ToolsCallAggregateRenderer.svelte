<script lang="ts">
	import type { ToolsCallAggregateMessageDto, ToolCall, ToolCallResult } from '$lib/types/chat';
	import { writable } from 'svelte/store';
	import { onMount } from 'svelte';
	
	export let message: ToolsCallAggregateMessageDto;
	export let isStreaming = false;
	
	// Track which results have been received
	const resultMap = writable(new Map<string, ToolCallResult>());
	
	$: {
		if (message.toolResults) {
			const map = new Map<string, ToolCallResult>();
			message.toolResults.forEach(result => {
				map.set(result.toolCallId, result);
			});
			resultMap.set(map);
		}
	}
	
	function getToolCallId(toolCall: ToolCall): string {
		return toolCall.tool_call_id || toolCall.id || `tool_${toolCall.index || 0}`;
	}
	
	function formatArgsAsYaml(args: any): Array<{type: string, content: string}> {
		let parsed = args;
		if (typeof args === 'string') {
			try {
				parsed = JSON.parse(args);
			} catch {
				return [{type: 'text', content: args}];
			}
		}
		
		const lines: Array<{type: string, content: string}> = [];
		
		function formatValue(value: any, indent = 0): void {
			const spaces = '  '.repeat(indent);
			
			if (value === null) {
				lines.push({type: 'null', content: 'null'});
			} else if (typeof value === 'boolean') {
				lines.push({type: 'boolean', content: String(value)});
			} else if (typeof value === 'number') {
				lines.push({type: 'number', content: String(value)});
			} else if (typeof value === 'string') {
				lines.push({type: 'string', content: value});
			} else if (Array.isArray(value)) {
				value.forEach((item, i) => {
					if (i === 0 || typeof item === 'object') {
						lines.push({type: 'text', content: '\n' + spaces + '- '});
					} else {
						lines.push({type: 'text', content: ', '});
					}
					formatValue(item, indent + 1);
				});
			} else if (typeof value === 'object') {
				Object.entries(value).forEach(([key, val], i) => {
					if (i > 0) lines.push({type: 'text', content: '\n'});
					lines.push({type: 'text', content: spaces});
					lines.push({type: 'key', content: key + ':'});
					lines.push({type: 'text', content: ' '});
					formatValue(val, indent + 1);
				});
			}
		}
		
		formatValue(parsed);
		return lines;
	}
</script>

<div class="tools-aggregate-container" data-testid="tool-call-renderer">
	<div class="tools-header">
		<svg class="tools-icon" viewBox="0 0 24 24" width="16" height="16">
			<path fill="currentColor" d="M22.7 19l-9.1-9.1c.9-2.3.4-5-1.5-6.9-2-2-5-2.4-7.4-1.3L9 6 6 9 1.6 4.7C.4 7.1.9 10.1 2.9 12.1c1.9 1.9 4.6 2.4 6.9 1.5l9.1 9.1c.4.4 1 .4 1.4 0l2.3-2.3c.5-.4.5-1.1.1-1.4z"/>
		</svg>
		<span class="tools-title">Tool Calls</span>
	</div>
	
	<div class="tool-calls-list">
		{#each message.toolCalls as toolCall, index}
			{@const toolCallId = getToolCallId(toolCall)}
			{@const result = $resultMap.get(toolCallId)}
			<div class="tool-call-item" 
				data-testid="tool-call-item" 
				data-id={toolCallId}
				data-tool-name={toolCall.function_name || toolCall.name || 'Unknown Tool'}
				data-tool-index={index}>
				<div class="tool-call-header">
					<span class="tool-name" data-testid="tool-call-name">{toolCall.function_name || toolCall.name || 'Unknown Tool'}</span>
					{#if result}
						{#if result.result.startsWith('Error')}
							<span class="status-badge error">Error</span>
						{:else}
							<span class="status-badge success">Complete</span>
						{/if}
					{:else if isStreaming}
						<span class="status-badge pending">Running...</span>
					{:else}
						<span class="status-badge waiting">Waiting</span>
					{/if}
				</div>
				
				{#if toolCall.function_args || toolCall.args}
					<div class="tool-args" data-testid="tool-call-args">
						<div class="args-label">Arguments:</div>
						<pre class="args-content">
							{#each formatArgsAsYaml(toolCall.function_args || toolCall.args) as segment}
								{#if segment.type === 'key'}
									<span class="text-orange-600">{segment.content}</span>
								{:else if segment.type === 'string'}
									<span class="text-green-600">{segment.content}</span>
								{:else if segment.type === 'number'}
									<span class="text-blue-600">{segment.content}</span>
								{:else if segment.type === 'boolean'}
									<span class="text-purple-600">{segment.content}</span>
								{:else if segment.type === 'null'}
									<span class="text-gray-600">{segment.content}</span>
								{:else}
									<span>{segment.content}</span>
								{/if}
							{/each}
						</pre>
					</div>
				{:else}
					<div class="tool-args" data-testid="tool-call-args">
						<span>No arguments</span>
					</div>
				{/if}
				
				{#if result}
					<div class="tool-result" class:error={result.result.startsWith('Error')}>
						<div class="result-label">Result:</div>
						<pre class="result-content">{result.result}</pre>
					</div>
				{:else if isStreaming}
					<div class="tool-pending">
						<div class="spinner"></div>
						<span>Executing tool...</span>
					</div>
				{/if}
			</div>
		{/each}
	</div>
</div>

<style>
	.tools-aggregate-container {
		background: var(--color-surface-secondary, #f8f9fa);
		border: 1px solid var(--color-border, #e0e0e0);
		border-radius: 8px;
		overflow: hidden;
		margin: 0.5rem 0;
	}
	
	.tools-header {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		padding: 0.75rem 1rem;
		background: var(--color-surface-tertiary, #e8eaf0);
		border-bottom: 1px solid var(--color-border, #e0e0e0);
	}
	
	.tools-icon {
		color: var(--color-text-secondary, #666);
	}
	
	.tools-title {
		font-weight: 500;
		color: var(--color-text-primary, #333);
		font-size: 0.875rem;
	}
	
	.tool-calls-list {
		padding: 0.75rem;
	}
	
	.tool-call-item {
		background: white;
		border: 1px solid var(--color-border-light, #f0f0f0);
		border-radius: 6px;
		padding: 0.75rem;
		margin-bottom: 0.75rem;
	}
	
	.tool-call-item:last-child {
		margin-bottom: 0;
	}
	
	.tool-call-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		margin-bottom: 0.5rem;
	}
	
	.tool-name {
		font-weight: 500;
		color: var(--color-text-primary, #333);
		font-size: 0.875rem;
	}
	
	.status-badge {
		padding: 0.125rem 0.5rem;
		border-radius: 12px;
		font-size: 0.75rem;
		font-weight: 500;
	}
	
	.status-badge.success {
		background: #d4edda;
		color: #155724;
	}
	
	.status-badge.error {
		background: #f8d7da;
		color: #721c24;
	}
	
	.status-badge.pending {
		background: #fff3cd;
		color: #856404;
	}
	
	.status-badge.waiting {
		background: #e2e3e5;
		color: #383d41;
	}
	
	.tool-args,
	.tool-result {
		margin-top: 0.5rem;
	}
	
	.args-label,
	.result-label {
		font-size: 0.75rem;
		color: var(--color-text-secondary, #666);
		margin-bottom: 0.25rem;
	}
	
	.args-content,
	.result-content {
		background: var(--color-surface-code, #f5f5f5);
		border: 1px solid var(--color-border-light, #e0e0e0);
		border-radius: 4px;
		padding: 0.5rem;
		font-family: 'Monaco', 'Menlo', monospace;
		font-size: 0.75rem;
		overflow-x: auto;
		margin: 0;
		white-space: pre-wrap;
		word-break: break-word;
	}
	
	.tool-result.error .result-content {
		background: #fff5f5;
		border-color: #ffcccc;
		color: #cc0000;
	}
	
	.tool-pending {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		padding: 0.5rem;
		background: var(--color-surface-info, #f0f8ff);
		border-radius: 4px;
		font-size: 0.813rem;
		color: var(--color-text-secondary, #666);
	}
	
	.spinner {
		width: 14px;
		height: 14px;
		border: 2px solid #ddd;
		border-top-color: #666;
		border-radius: 50%;
		animation: spin 0.8s linear infinite;
	}
	
	@keyframes spin {
		to {
			transform: rotate(360deg);
		}
	}
</style>