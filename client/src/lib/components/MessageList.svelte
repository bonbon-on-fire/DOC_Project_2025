<script lang="ts">
  import type { MessageDto } from '$lib/types/chat';
  import MessageBubble from './MessageBubble.svelte';
  export let messages: MessageDto[] = [];

  let chatContainer: HTMLDivElement;
</script>

<div bind:this={chatContainer} class="max-w-4xl mx-auto px-4 py-6 space-y-6 overflow-y-auto">
  {#if messages.length === 0}
    <!-- Empty State -->
    <div class="text-center py-12">
      <svg class="w-12 h-12 mx-auto text-gray-400 dark:text-gray-500 mb-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" 
              d="M8 10h.01M12 10h.01M16 10h.01M9 16H5a2 2 0 01-2-2V6a2 2 0 012-2h14a2 2 0 012 2v8a2 2 0 01-2 2h-5l-5 5v-5z"></path>
      </svg>
      <p class="text-gray-500 dark:text-gray-400">Start the conversation</p>
    </div>
  {:else}
    <!-- Messages -->
    {#each messages as message, index (message.id)}
      <MessageBubble 
        {message} 
        isLastAssistantMessage={message.role === 'assistant' && index === messages.length - 1}
        on:message 
      />
    {/each}


  {/if}
</div>