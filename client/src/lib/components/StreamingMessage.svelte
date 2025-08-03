<script lang="ts">
  export let content: string;

  function formatContent(content: string): string {
    // Basic markdown-like formatting
    return content
      .replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>')
      .replace(/\*(.*?)\*/g, '<em>$1</em>')
      .replace(/`(.*?)`/g, '<code class="bg-gray-200 dark:bg-gray-700 px-1 rounded text-sm">$1</code>')
      .replace(/\n/g, '<br>');
  }
</script>

<div class="flex justify-start">
  <div class="flex items-start space-x-3 max-w-xs sm:max-w-md lg:max-w-lg xl:max-w-xl">
    <!-- AI Avatar -->
    <div class="flex-shrink-0 w-8 h-8 bg-gradient-to-r from-blue-500 to-purple-600 rounded-full flex items-center justify-center">
      <svg class="w-5 h-5 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" 
              d="M9.663 17h4.673M12 3v1m6.364 1.636l-.707.707M21 12h-1M4 12H3m3.343-5.657l-.707-.707m2.828 9.9a5 5 0 117.072 0l-.548.547A3.374 3.374 0 0014 18.469V19a2 2 0 11-4 0v-.531c0-.895-.356-1.754-.988-2.386l-.548-.547z"></path>
      </svg>
    </div>

    <!-- Streaming Message Content -->
    <div class="relative">
      <div class="px-4 py-3 rounded-2xl shadow-sm bg-white dark:bg-gray-800 text-gray-900 dark:text-white border border-gray-200 dark:border-gray-700">
        {#if content.trim()}
          <!-- Message text with basic formatting -->
          <div class="prose prose-sm max-w-none">
            {@html formatContent(content)}
          </div>
        {:else}
          <!-- Thinking indicator -->
          <div class="flex items-center space-x-1 text-gray-500 dark:text-gray-400">
            <span class="text-sm">AI is thinking</span>
            <div class="flex space-x-1">
              <div class="w-2 h-2 bg-gray-400 rounded-full animate-bounce" style="animation-delay: 0ms"></div>
              <div class="w-2 h-2 bg-gray-400 rounded-full animate-bounce" style="animation-delay: 150ms"></div>
              <div class="w-2 h-2 bg-gray-400 rounded-full animate-bounce" style="animation-delay: 300ms"></div>
            </div>
          </div>
        {/if}
        
        <!-- Cursor indicator for streaming -->
        <span class="inline-block w-2 h-4 bg-blue-500 animate-pulse ml-1"></span>
      </div>

      <!-- "Streaming..." indicator -->
      <div class="mt-1 text-xs text-blue-500 dark:text-blue-400 flex items-center space-x-1">
        <svg class="w-3 h-3 animate-spin" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" 
                d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"></path>
        </svg>
        <span>Streaming...</span>
      </div>
    </div>
  </div>
</div>