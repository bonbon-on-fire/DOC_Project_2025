<script lang="ts">
  import { onMount } from 'svelte';
  import { currentChat, currentChatMessages, chatActions } from '$lib/stores/chat';
  import { chatHub, chatHubConnectionStatus, chatHubStreamingMessages } from '$lib/signalr/chat-hub';
  import MessageList from './MessageList.svelte';
  import MessageInput from './MessageInput.svelte';

  let messagesContainer: HTMLElement;
  let isSending = false;

  // Auto-scroll to bottom when new messages arrive
  $: if ($currentChatMessages && messagesContainer) {
    setTimeout(() => {
      messagesContainer.scrollTop = messagesContainer.scrollHeight;
    }, 50);
  }

  async function sendMessage(message: string) {
    if (isSending || !message.trim()) return;

    isSending = true;
    try {
      await chatActions.sendMessage(message.trim());
    } catch (err) {
      console.error('Failed to send message:', err);
    } finally {
      isSending = false;
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
          <p>✨ Real-time AI conversations</p>
          <p>🔄 Streaming responses</p>
          <p>💾 Chat history saved</p>
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
            <div class="w-2 h-2 rounded-full 
                       {$chatHubConnectionStatus === 'connected' ? 'bg-green-500' : 
                        $chatHubConnectionStatus === 'connecting' ? 'bg-yellow-500 animate-pulse' : 'bg-red-500'}">
            </div>
            <span class="text-xs text-gray-500 dark:text-gray-400 capitalize">
              {$chatHubConnectionStatus}
            </span>
          </div>
        </div>
      </div>
    </div>

    <!-- Messages Area -->
    <div 
      bind:this={messagesContainer}
      class="flex-1 overflow-y-auto bg-gray-50 dark:bg-gray-900"
    >
      <MessageList 
        messages={$currentChatMessages} 
        streamingMessages={$chatHubStreamingMessages}
      />
    </div>

    <!-- Message Input -->
    <div class="bg-white dark:bg-gray-800 border-t border-gray-200 dark:border-gray-700">
      <MessageInput 
        on:send={(e) => sendMessage(e.detail.message)}
        disabled={isSending || $chatHubConnectionStatus !== 'connected'}
        placeholder={isSending ? 'Sending...' : 
                    $chatHubConnectionStatus !== 'connected' ? 'Connecting...' : 
                    'Type your message...'}
      />
    </div>
  {/if}
</div>