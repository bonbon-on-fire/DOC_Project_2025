<script lang="ts">
	import type { MessageDto, TextMessageDto, ReasoningMessageDto } from '$lib/types/chat';
	import MessageRouter from './MessageRouter.svelte';
	export let messages: (MessageDto | TextMessageDto | ReasoningMessageDto)[] = [];

	let chatContainer: HTMLDivElement;
</script>

<div bind:this={chatContainer} class="mx-auto max-w-4xl space-y-6 px-4 py-6">
	{#if messages.length === 0}
		<!-- Empty State -->
		<div class="py-12 text-center">
			<svg
				class="mx-auto mb-4 h-12 w-12 text-gray-400 dark:text-gray-500"
				fill="none"
				stroke="currentColor"
				viewBox="0 0 24 24"
			>
				<path
					stroke-linecap="round"
					stroke-linejoin="round"
					stroke-width="2"
					d="M8 10h.01M12 10h.01M16 10h.01M9 16H5a2 2 0 01-2-2V6a2 2 0 012-2h14a2 2 0 012 2v8a2 2 0 01-2 2h-5l-5 5v-5z"
				></path>
			</svg>
			<p class="text-gray-500 dark:text-gray-400">Start the conversation</p>
		</div>
	{:else}
		<!-- Messages -->
		{#each messages as message, index (message.id)}
			<MessageRouter
				{message}
				isLatest={index === messages.length - 1}
			/>
		{/each}
	{/if}
</div>
