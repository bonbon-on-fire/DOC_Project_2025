<script lang="ts">
  import type { MessageDto } from '$lib/types/chat';
  import { formatTime } from '$lib/utils/time';
  import { currentStreamingMessage, isStreaming } from '$lib/stores/chat';

  export let message: MessageDto;
  export let isLastAssistantMessage = false;

  function formatContent(content: string): string {
    // Basic markdown-like formatting
    return content
      .replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>')
      .replace(/\*(.*?)\*/g, '<em>$1</em>')
      .replace(/`(.*?)`/g, '<code class="bg-gray-200 dark:bg-gray-700 px-1 rounded text-sm">$1</code>')
      .replace(/\n/g, '<br>');
  }
</script>

<div class="flex {message.role === 'user' ? 'justify-end' : 'justify-start'}">
  <div class="flex items-start space-x-3 max-w-xs sm:max-w-md lg:max-w-lg xl:max-w-xl">
    <!-- Avatar -->
    {#if message.role !== 'user'}
      <div class="flex-shrink-0 w-8 h-8 bg-gradient-to-r from-blue-500 to-purple-600 rounded-full flex items-center justify-center">
        <svg class="w-5 h-5 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" 
                d="M9.663 17h4.673M12 3v1m6.364 1.636l-.707.707M21 12h-1M4 12H3m3.343-5.657l-.707-.707m2.828 9.9a5 5 0 117.072 0l-.548.547A3.374 3.374 0 0014 18.469V19a2 2 0 11-4 0v-.531c0-.895-.356-1.754-.988-2.386l-.548-.547z"></path>
        </svg>
      </div>
    {/if}

    <!-- Message Content -->
    <div class="{message.role === 'user' ? 'order-first' : 'order-last'} relative">
      <div class="px-4 py-3 rounded-2xl shadow-sm
                  {message.role === 'user' 
                    ? 'bg-blue-600 text-white ml-auto' 
                    : 'bg-white dark:bg-gray-800 text-gray-900 dark:text-white border border-gray-200 dark:border-gray-700'}
                  {message.role === 'system' 
                    ? 'bg-yellow-100 dark:bg-yellow-900 text-yellow-800 dark:text-yellow-200 border border-yellow-200 dark:border-yellow-800' 
                    : ''}">
        
        <!-- System message indicator -->
        {#if message.role === 'system'}
          <div class="flex items-center space-x-2 mb-2">
            <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" 
                    d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"></path>
            </svg>
            <span class="text-xs font-medium uppercase tracking-wide">System</span>
          </div>
        {/if}

        <!-- Message text with basic formatting -->
        <div class="prose prose-sm max-w-none dark:prose-invert
                    {message.role === 'user' ? 'prose-invert' : ''}
                    {message.role === 'system' ? 'text-yellow-800 dark:text-yellow-200' : 'dark:text-gray-300'}">
          {#if isLastAssistantMessage && $isStreaming && message.role === 'assistant'}
            {@html formatContent($currentStreamingMessage)}
            <span class="animate-pulse">▋</span>
          {:else}
            {@html formatContent(message.content)}
          {/if}
        </div>
      </div>

      <!-- Timestamp -->
      <div class="mt-1 text-xs text-gray-500 dark:text-gray-400 
                  {message.role === 'user' ? 'text-right' : 'text-left'}">
        {formatTime(message.timestamp)}
      </div>
    </div>

    <!-- User Avatar -->
    {#if message.role === 'user'}
      <div class="flex-shrink-0 w-8 h-8 bg-gray-600 rounded-full flex items-center justify-center">
        <svg class="w-5 h-5 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" 
                d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z"></path>
        </svg>
      </div>
    {/if}
  </div>
</div>