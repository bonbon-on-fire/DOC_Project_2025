<script lang="ts">
  import { onMount, afterUpdate } from 'svelte';
  import type { MessageDto } from '$lib/types/chat';
  import { isStreaming } from '$lib/stores/chat';
  import MessageBubble from './MessageBubble.svelte';
  export let messages: MessageDto[] = [];

  let chatContainer: HTMLDivElement;
  let userHasScrolled = false;
  let previousMessageCount = 0;
  let isAutoScrolling = false;
  let previousStreamingState = false;

  // Track total message count for detecting new messages
  $: totalMessages = messages.length;
  
  // Watch for streaming state changes
  $: {
    if ($isStreaming && !previousStreamingState) {
      // Streaming just started - force scroll to bottom and reset user scroll state
      userHasScrolled = false;
      scrollToBottom(true);
    }
    previousStreamingState = $isStreaming;
  }

  const scrollToBottom = (force = false) => {
    // Find the actual scrollable parent container
    const scrollableParent = chatContainer?.closest('.overflow-y-auto') as HTMLElement;
    if (scrollableParent && scrollableParent.scrollHeight && (!userHasScrolled || force)) {
      isAutoScrolling = true;
      scrollableParent.scrollTo({ top: scrollableParent.scrollHeight, behavior: 'smooth' });
      // Reset the flag after scrolling
      setTimeout(() => {
        isAutoScrolling = false;
      }, 100);
    }
  };

  const handleScroll = () => {
    // Find the actual scrollable parent container
    const scrollableParent = chatContainer?.closest('.overflow-y-auto') as HTMLElement;
    if (!scrollableParent || !scrollableParent.scrollHeight || isAutoScrolling) return;
    
    // Check if user is near the bottom (within 100px)
    const isNearBottom = scrollableParent.scrollHeight - scrollableParent.scrollTop - scrollableParent.clientHeight < 100;
    
    // Only update userHasScrolled if this is a user-initiated scroll
    userHasScrolled = !isNearBottom;
  };

  // Auto-scroll when new messages arrive or during streaming
  afterUpdate(() => {
    // Auto-scroll in two scenarios:
    // 1. New messages arrived (message count increased)
    // 2. Currently streaming (content is being updated)
    const hasNewMessages = totalMessages > previousMessageCount;
    
    if (hasNewMessages || $isStreaming) {
      // Only auto-scroll if user hasn't manually scrolled away from the bottom
      if (!userHasScrolled) {
        scrollToBottom();
      }
    }
    
    // Update previous message count
    if (hasNewMessages) {
      previousMessageCount = totalMessages;
    }
  });

  onMount(() => {
    previousMessageCount = totalMessages;
    // Force scroll to bottom on initial mount
    scrollToBottom(true);
    
    // Attach scroll listener to the actual scrollable parent
    const scrollableParent = chatContainer?.closest('.overflow-y-auto') as HTMLElement;
    if (scrollableParent) {
      scrollableParent.addEventListener('scroll', handleScroll);
      
      // Cleanup on component destroy
      return () => {
        scrollableParent.removeEventListener('scroll', handleScroll);
      };
    }
  });
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