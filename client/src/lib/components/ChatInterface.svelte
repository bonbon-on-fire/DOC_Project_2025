<script lang="ts">
  import { onMount, onDestroy } from 'svelte';
  import ChatSidebar from './ChatSidebar.svelte';
  import ChatWindow from './ChatWindow.svelte';
  import { chatActions, error, currentChat, chats } from '$lib/stores/chat';

  let isInitialized = false;

  onMount(async () => {
    try {
      await chatActions.initialize();
      isInitialized = true;
    } catch (err) {
      console.error('Failed to initialize chat:', err);
    }
  });

  // Auto-select the most recent chat if none is selected
  $: if (isInitialized && !$currentChat && $chats.length > 0) {
    chatActions.selectChat($chats[0].id);
  }

  onDestroy(async () => {
    await chatActions.cleanup();
  });

  function dismissError() {
    chatActions.clearError();
  }
</script>

<div class="flex h-screen bg-gray-100 dark:bg-gray-900">
  <!-- Sidebar -->
  <div class="w-80 bg-white dark:bg-gray-800 border-r border-gray-200 dark:border-gray-700 flex-shrink-0">
    <ChatSidebar />
  </div>

  <!-- Main Chat Area -->
  <div class="flex-1 flex flex-col">
    {#if !isInitialized && !$currentChat}
      <div class="flex-1 flex items-center justify-center">
        <div class="text-center">
          <div class="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600 mx-auto mb-4"></div>
          <p class="text-gray-600 dark:text-gray-300">Connecting to chat...</p>
        </div>
      </div>
    {:else}
      <ChatWindow />
    {/if}
  </div>

  <!-- Error Toast -->
  {#if $error}
    <div class="fixed top-4 right-4 z-50 max-w-md">
      <div class="bg-red-500 text-white px-6 py-4 rounded-lg shadow-lg flex items-center justify-between">
        <div class="flex items-center">
          <svg class="w-6 h-6 mr-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" 
                  d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"></path>
          </svg>
          <span>{$error}</span>
        </div>
        <button 
          on:click={dismissError}
          class="ml-4 text-white hover:text-gray-200 focus:outline-none"
          aria-label="Dismiss error"
        >
          <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12"></path>
          </svg>
        </button>
      </div>
    </div>
  {/if}
</div>

<style>
  /* Add any additional custom styles here */
</style>