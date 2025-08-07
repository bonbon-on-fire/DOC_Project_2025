<script lang="ts">
  import { onMount, afterUpdate } from 'svelte';
  import { currentChat, currentChatId, currentChatMessages, chatActions, isStreaming } from '$lib/stores/chat';
  import MessageList from './MessageList.svelte';
  import MessageInput from './MessageInput.svelte';

  let messagesContainer: HTMLElement;
  let isSending = false;
  let userHasScrolled = false;
  let previousMessageCount = 0;
  let isAutoScrolling = false;
  let previousStreamingState = false;

  // Track total message count for detecting new messages
  $: totalMessages = $currentChatMessages.length;
  
  // Watch for messages array changes to trigger scroll
  $: if ($currentChatMessages && $currentChatMessages.length > 0) {
    // Trigger scroll when messages are loaded/updated with multiple attempts
    requestAnimationFrame(() => {
      setTimeout(() => {
        if (!userHasScrolled) {
          scrollToBottom(true);
        }
      }, 100);
      
      setTimeout(() => {
        if (!userHasScrolled) {
          scrollToBottom(true);
        }
      }, 300);
      
      setTimeout(() => {
        if (!userHasScrolled) {
          scrollToBottom(true);
        }
      }, 500);
    });
  }
  
  // Watch for streaming state changes
  $: {
    if ($isStreaming && !previousStreamingState) {
      // Streaming just started - force scroll to bottom and reset user scroll state
      userHasScrolled = false;
      setTimeout(() => scrollToBottom(true), 100);
    }
    previousStreamingState = $isStreaming;
  }

  // Watch for conversation changes to reset scroll
  $: if ($currentChatId) {
    userHasScrolled = false;
    previousMessageCount = 0;
    // Force scroll after conversation loads with multiple attempts
    setTimeout(() => scrollToBottom(true), 200);
    setTimeout(() => scrollToBottom(true), 500);
    setTimeout(() => scrollToBottom(true), 1000);
  }

  const scrollToBottom = (force = false) => {
    if (messagesContainer && messagesContainer.scrollHeight && (!userHasScrolled || force)) {
      isAutoScrolling = true;
      
      // Use requestAnimationFrame to ensure DOM is updated, then scroll
      requestAnimationFrame(() => {
        requestAnimationFrame(() => {
          messagesContainer.scrollTo({ top: messagesContainer.scrollHeight, behavior: 'smooth' });
        });
      });
      
      // Reset the flag after scrolling
      setTimeout(() => {
        isAutoScrolling = false;
      }, 300);
    }
  };

  const handleScroll = () => {
    if (!messagesContainer || !messagesContainer.scrollHeight || isAutoScrolling) return;
    
    // Check if user is near the bottom (within 100px)
    const isNearBottom = messagesContainer.scrollHeight - messagesContainer.scrollTop - messagesContainer.clientHeight < 100;
    
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
        // Use multiple attempts to ensure scroll works
        setTimeout(() => scrollToBottom(), 50);
        setTimeout(() => scrollToBottom(), 200);
        setTimeout(() => scrollToBottom(), 400);
      }
    }
    
    // If this is initial load (no previous messages), force scroll
    if (totalMessages > 0 && previousMessageCount === 0) {
      setTimeout(() => scrollToBottom(true), 300);
      setTimeout(() => scrollToBottom(true), 600);
    }
    
    // Update previous message count
    if (hasNewMessages) {
      previousMessageCount = totalMessages;
    }
  });

  onMount(() => {
    previousMessageCount = totalMessages;
    
    // Attach scroll listener to the messages container
    if (messagesContainer) {
      messagesContainer.addEventListener('scroll', handleScroll);
      
      // Force scroll to bottom after DOM is fully rendered
      requestAnimationFrame(() => {
        setTimeout(() => scrollToBottom(true), 100);
        setTimeout(() => scrollToBottom(true), 300);
        setTimeout(() => scrollToBottom(true), 500);
      });
      
      // Cleanup on component destroy
      return () => {
        messagesContainer.removeEventListener('scroll', handleScroll);
      };
    }
  });

  async function handleSend(event: CustomEvent<{ message: string }>) {
    if ($isStreaming) return;

    // Reset user scroll state when sending a new message
    userHasScrolled = false;

    if ($currentChatId) {
      await chatActions.streamReply(event.detail.message);
    } else {
      await chatActions.streamNewChat(event.detail.message);
    }
  }
</script>

<div class="flex flex-col h-full">
  {#if !$currentChat}
    <!-- No Chat Selected -->
    <div class="flex-1 flex items-center justify-center bg-gray-50 dark:bg-gray-800">
      <div class="text-center max-w-md">
        <svg class="w-16 h-16 mx-auto text-gray-400 dark:text-gray-500 mb-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" 
                d="M8 12h.01M12 12h.01M16 12h.01M21 12c0 4.418-3.582 8-8 8a8.991 8.991 0 01-4.436-1.176L3 20l1.176-5.564A8.991 8.991 0 013 12c0-4.418 3.582-8 8-8s8 3.582 8 8z"></path>
        </svg>
        <h2 class="text-xl font-semibold text-gray-900 dark:text-white mb-2">
          Welcome to AI Chat
        </h2>
        <p class="text-gray-600 dark:text-gray-300 mb-4">
          Select a conversation from the sidebar or start a new chat to begin.
        </p>
        <div class="space-y-2 text-sm text-gray-500 dark:text-gray-400">
          <p> Real-time AI conversations</p>
          <p> Streaming responses</p>
          <p> Chat history saved</p>
        </div>
      </div>
    </div>
  {:else}
    <!-- Chat Header -->
    <div class="bg-white dark:bg-gray-800 border-b border-gray-200 dark:border-gray-700 px-6 py-4">
      <div class="flex items-center justify-between">
        <div>
          <h1 class="text-lg font-semibold text-gray-900 dark:text-white">
            {$currentChat.title}
          </h1>
          <p class="text-sm text-gray-500 dark:text-gray-400">
            {$currentChatMessages.length} messages
          </p>
        </div>
        <div class="flex items-center space-x-2">
          <!-- Connection Status -->
          <div class="flex items-center space-x-2">
            <!-- Removed SignalR connection status -->
          </div>
        </div>
      </div>
    </div>

    <!-- Messages Area -->
    <div 
      bind:this={messagesContainer}
      class="flex-1 overflow-y-auto bg-gray-50 dark:bg-gray-900"
    >
      <MessageList messages={$currentChatMessages} />
    </div>

    <!-- Message Input -->
    <div class="bg-white dark:bg-gray-800 border-t border-gray-200 dark:border-gray-700">
      <MessageInput 
        on:send={handleSend}
        disabled={$isStreaming}
        placeholder={$isStreaming ? 'AI is thinking...' : 'Type your message...'}
      />
    </div>
  {/if}
</div>