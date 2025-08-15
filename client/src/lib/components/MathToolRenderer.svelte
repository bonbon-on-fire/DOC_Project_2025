<script lang="ts">
	import { createEventDispatcher } from 'svelte';
	import CollapsibleMessageRenderer from '$lib/components/CollapsibleMessageRenderer.svelte';
	import type { RichMessageDto } from '$lib/types';
	import type { MessageRenderer } from '$lib/types/renderer';

	export let message: any & RichMessageDto;
	export let isLatest: boolean = false;
	export let isLastAssistantMessage: boolean = false;
	export let expanded: boolean = true;
	export let renderPhase: 'initial' | 'streaming' | 'complete' = 'initial';

	const dispatch = createEventDispatcher<{ 
		stateChange: { expanded: boolean }; 
		toggleExpansion: { expanded: boolean } 
	}>();

	const rendererInterface: MessageRenderer<any> = {
		messageType: 'math_tool'
	};

	function getMathCalls(msg: any): Array<{name: string, args: any, id?: string, result?: any}> {
		const toolCalls = [];
		if (msg.toolCalls) toolCalls.push(...msg.toolCalls);
		if (msg.tool_calls) toolCalls.push(...msg.tool_calls);
		if (msg.content && typeof msg.content === 'string') {
			try {
				const parsed = JSON.parse(msg.content);
				if (parsed.tool_calls) toolCalls.push(...parsed.tool_calls);
			} catch {}
		}
		return toolCalls.filter(tc => 
			tc.name && (
				tc.name.toLowerCase().includes('math') ||
				tc.name.toLowerCase().includes('calc') ||
				tc.name.toLowerCase().includes('compute') ||
				tc.name.toLowerCase().includes('evaluate')
			)
		);
	}

	function formatExpression(args: any): string {
		if (args.expression) return args.expression;
		if (args.formula) return args.formula;
		if (args.equation) return args.equation;
		
		if (args.operation && args.operands) {
			const ops = Array.isArray(args.operands) ? args.operands : [args.a || args.x, args.b || args.y];
			switch(args.operation.toLowerCase()) {
				case 'add':
				case 'addition':
					return ops.join(' + ');
				case 'subtract':
				case 'subtraction':
					return ops.join(' - ');
				case 'multiply':
				case 'multiplication':
					return ops.join(' × ');
				case 'divide':
				case 'division':
					return ops.join(' ÷ ');
				case 'power':
					return `${ops[0]}^${ops[1]}`;
				case 'sqrt':
					return `√${ops[0]}`;
				default:
					return `${args.operation}(${ops.join(', ')})`;
			}
		}
		
		if (args.a !== undefined && args.b !== undefined) {
			if (args.operator) {
				return `${args.a} ${args.operator} ${args.b}`;
			}
			return `${args.a}, ${args.b}`;
		}
		
		return JSON.stringify(args, null, 2);
	}

	function formatResult(result: any): string {
		if (result === null || result === undefined) return '...';
		if (typeof result === 'number') {
			if (Number.isInteger(result)) return result.toString();
			return result.toFixed(10).replace(/\.?0+$/, '');
		}
		if (typeof result === 'string') return result;
		if (typeof result === 'object' && result.value !== undefined) {
			return formatResult(result.value);
		}
		return JSON.stringify(result);
	}

	function createCollapsedPreview(mathCalls: any[]): string {
		if (mathCalls.length === 0) return 'No math calculations';
		if (mathCalls.length === 1) {
			const call = mathCalls[0];
			const expr = formatExpression(call.args);
			const result = call.result ? ` = ${formatResult(call.result)}` : '';
			return expr.length > 30 ? `${call.name}(...)${result}` : `${expr}${result}`;
		}
		return `${mathCalls.length} calculations`;
	}

	$: mathCalls = getMathCalls(message);
	$: collapsedPreview = createCollapsedPreview(mathCalls);

	function forwardStateChange(e: CustomEvent<{ expanded: boolean }>) {
		expanded = e.detail.expanded;
		dispatch('stateChange', e.detail);
	}
	function forwardToggle(e: CustomEvent<{ expanded: boolean }>) {
		dispatch('toggleExpansion', e.detail);
	}
</script>

<div data-component="math-tool-renderer" data-testid="math-tool-renderer">
<CollapsibleMessageRenderer
	{message}
	{isLatest}
	{expanded}
	collapsible={true}
	iconPath="M12 6.253v13m0-13C10.832 5.477 9.246 5 7.5 5S4.168 5.477 3 6.253v13C4.168 18.477 5.754 18 7.5 18s3.332.477 4.5 1.253m0-13C13.168 5.477 14.754 5 16.5 5c1.747 0 3.332.477 4.5 1.253v13C19.832 18.477 18.247 18 16.5 18c-1.746 0-3.332.477-4.5 1.253"
	iconColors="from-purple-500 to-pink-600"
	messageType="Math Calculation"
	{collapsedPreview}
	borderColor="border-purple-200"
	bgColor="bg-purple-50"
	textColor="text-purple-900"
	darkBorderColor="dark:border-purple-700"
	darkBgColor="dark:bg-purple-900/20"
	darkTextColor="dark:text-purple-100"
	on:stateChange={forwardStateChange}
	on:toggleExpansion={forwardToggle}
>
	<div class="space-y-3">
		{#each mathCalls as mathCall, index}
			<div 
				class="rounded-lg border border-purple-300 bg-white p-4 dark:border-purple-600 dark:bg-purple-800/30"
				data-testid="math-call-item"
				data-tool-name={mathCall.name}
				data-tool-index={index}
			>
				<div class="mb-3 flex items-center justify-between">
					<div class="flex items-center space-x-2">
						<svg class="h-5 w-5 text-purple-600 dark:text-purple-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
							<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" 
								  d="M9 7h6m0 10v-3m-3 3h.01M9 17h.01M9 14h.01M12 14h.01M15 11h.01M12 11h.01M9 11h.01M7 21h10a2 2 0 002-2V5a2 2 0 00-2-2H7a2 2 0 00-2 2v14a2 2 0 002 2z"></path>
						</svg>
						<span class="font-semibold text-purple-800 dark:text-purple-200" data-testid="math-call-name">
							{mathCall.name}
						</span>
						{#if mathCall.id}
							<span class="text-xs text-purple-600 dark:text-purple-400" data-testid="math-call-id">
								#{mathCall.id}
							</span>
						{/if}
					</div>
					<span class="text-xs text-purple-600 dark:text-purple-400">
						Calculation #{index + 1}
					</span>
				</div>

				<div class="mt-2">
					<div class="rounded-lg bg-gradient-to-r from-purple-50 to-pink-50 p-4 dark:from-purple-900/30 dark:to-pink-900/30">
						<div class="flex items-center justify-center">
							<div class="text-center">
								<div class="mb-2 text-2xl font-mono text-purple-900 dark:text-purple-100" data-testid="math-expression">
									{formatExpression(mathCall.args)}
								</div>
								
								{#if mathCall.result !== undefined}
									<div class="mt-2 border-t border-purple-300 pt-2 dark:border-purple-600">
										<div class="text-3xl font-bold text-purple-800 dark:text-purple-200" data-testid="math-result">
											= {formatResult(mathCall.result)}
										</div>
									</div>
								{:else if isLatest && renderPhase === 'streaming' && index === mathCalls.length - 1}
									<div class="mt-2 flex items-center justify-center space-x-2 text-sm text-purple-600 dark:text-purple-400">
										<span class="animate-pulse">▋</span>
										<span>Calculating...</span>
									</div>
								{/if}
							</div>
						</div>
					</div>

					{#if mathCall.args && Object.keys(mathCall.args).length > 0}
						<details class="mt-3">
							<summary class="cursor-pointer text-xs text-purple-600 hover:text-purple-800 dark:text-purple-400 dark:hover:text-purple-200">
								View parameters
							</summary>
							<div class="mt-2 rounded bg-gray-50 p-2 text-xs font-mono dark:bg-gray-800">
								<pre class="whitespace-pre-wrap text-gray-700 dark:text-gray-300">{JSON.stringify(mathCall.args, null, 2)}</pre>
							</div>
						</details>
					{/if}
				</div>
			</div>
		{/each}

		{#if mathCalls.length === 0}
			<div class="text-center text-gray-500 dark:text-gray-400">
				<svg class="mx-auto h-8 w-8 mb-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
					<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" 
						  d="M9 7h6m0 10v-3m-3 3h.01M9 17h.01M9 14h.01M12 14h.01M15 11h.01M12 11h.01M9 11h.01M7 21h10a2 2 0 002-2V5a2 2 0 00-2-2H7a2 2 0 00-2 2v14a2 2 0 002 2z"></path>
				</svg>
				<p class="text-sm">No math calculations in this message</p>
			</div>
		{/if}
	</div>
</CollapsibleMessageRenderer>
</div>