<script lang="ts">
  import { onMount, onDestroy } from 'svelte';
  import { getRenderer, getRendererComponent } from '$lib/renderers';
  import { 
    initializeMessageState, 
    updateMessageState, 
    messageStates,
    type MessageState 
  } from '$lib/stores/messageState';
  import type { RichMessageDto } from '$lib/types';
  import MessageBubble from './MessageBubble.svelte';

  // Component props with proper TypeScript typing
  export let message: RichMessageDto;
  export let isLatest: boolean = false;
  export let onStateChange: ((messageId: string, state: MessageState) => void) | undefined = undefined;

  // Internal component state
  let mounted = false;
  let renderError: Error | null = null;
  let RendererComponent: any = null;
  let fallbackToMessageBubble = false;

  // Current message state from store
  $: currentMessageState = $messageStates[message.id];

  /**
   * Attempts to resolve the appropriate renderer for the message type.
   * Falls back to MessageBubble on any error.
   */
  async function resolveRenderer() {
    try {
      renderError = null;
      fallbackToMessageBubble = false;

      // Get renderer from registry
      const renderer = getRenderer(message.messageType || 'text');
      
      // Try to get the actual Svelte component for this renderer
      const RendererComponentModule = await getRendererComponent(message.messageType || 'text');
      
      if (RendererComponentModule && renderer.messageType !== 'fallback') {
        console.info(`Using ${renderer.messageType} renderer for message type '${message.messageType}'`);
        RendererComponent = RendererComponentModule;
        fallbackToMessageBubble = false;
      } else {
        console.warn(`No specific renderer found for message type '${message.messageType}', using MessageBubble fallback`);
        fallbackToMessageBubble = true;
        RendererComponent = MessageBubble;
      }
    } catch (error) {
      console.error('Error resolving renderer for message:', message.id, error);
      renderError = error instanceof Error ? error : new Error('Unknown renderer error');
      fallbackToMessageBubble = true;
      RendererComponent = MessageBubble;
    }
  }

  /**
   * Handles state changes and notifies parent component.
   */
  function handleStateChange(newState: Partial<MessageState>) {
    if (!message.id) return;

    updateMessageState(message.id, newState);
    
    // Notify parent component if callback provided
    if (onStateChange && currentMessageState) {
      onStateChange(message.id, { ...currentMessageState, ...newState });
    }
  }

  /**
   * Handles expand/collapse toggle for the message.
   */
  function handleToggleExpansion() {
    if (!currentMessageState) return;

    const newExpanded = !currentMessageState.expanded;
    handleStateChange({ expanded: newExpanded });
  }

  // Initialize message state when component mounts
  onMount(() => {
    mounted = true;
    
    // Initialize message state if not already present
    if (message.id && !currentMessageState) {
      initializeMessageState(message.id, isLatest);
    }

    // Resolve the appropriate renderer
    resolveRenderer();
  });

  // Clean up when component unmounts
  onDestroy(() => {
    mounted = false;
  });

  // Reactive statements for prop changes
  $: if (mounted && message) {
    resolveRenderer();
  }

  $: if (mounted && message.id && isLatest !== undefined) {
    // Update expansion state when isLatest changes
    handleStateChange({ expanded: isLatest });
  }

  // Reactive statement for render phase tracking
  $: if (mounted && currentMessageState) {
    // Update render phase based on message streaming state
    // Support different message payload shapes (text/reasoning), fallback to empty
    const getMessageContent = (m: any): string => {
      if (typeof m?.text === 'string') return m.text;
      if (typeof m?.reasoning === 'string') return m.reasoning;
      if (typeof m?.content === 'string') return m.content; // legacy fallback
      return '';
    };

    const isStreaming = getMessageContent(message).includes('▋'); // Simple streaming detection
    const newPhase = isStreaming ? 'streaming' : 'complete';
    
    if (currentMessageState.renderPhase !== newPhase) {
      handleStateChange({ renderPhase: newPhase });
    }
  }
</script>

<!-- Main message container with error boundary -->
<div 
  class="message-router" 
  data-message-id={message.id}
  data-message-type={message.messageType || 'text'}
  data-testid={fallbackToMessageBubble ? 'fallback-renderer' : `${message.messageType || 'text'}-renderer`}
>
  {#if renderError}
    <!-- Error state with fallback rendering -->
    <div class="message-error border-l-4 border-red-500 bg-red-50 dark:bg-red-900/20 p-4 mb-4">
      <div class="flex items-start">
        <div class="flex-shrink-0">
          <svg class="h-5 w-5 text-red-400" viewBox="0 0 20 20" fill="currentColor">
            <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clip-rule="evenodd" />
          </svg>
        </div>
        <div class="ml-3">
          <h3 class="text-sm font-medium text-red-800 dark:text-red-200">
            Message Rendering Error
          </h3>
          <div class="mt-2 text-sm text-red-700 dark:text-red-300">
            <p>Failed to render message of type "{message.messageType || 'unknown'}". Using fallback renderer.</p>
            {#if renderError.message}
              <p class="mt-1 text-xs text-red-600 dark:text-red-400 font-mono">
                {renderError.message}
              </p>
            {/if}
          </div>
        </div>
      </div>
    </div>
  {/if}

  <!-- Dynamic component rendering -->
  {#if RendererComponent}
    <svelte:component 
      this={RendererComponent} 
      {message}
      isLatest={isLatest}
      expanded={currentMessageState?.expanded ?? isLatest}
      renderPhase={currentMessageState?.renderPhase ?? 'initial'}
      isLastAssistantMessage={isLatest && message.role === 'assistant'}
      on:toggleExpansion={handleToggleExpansion}
      on:stateChange={(event: CustomEvent) => handleStateChange(event.detail)}
    />
  {:else}
    <!-- Loading state while resolver works -->
    <div class="message-loading flex items-center space-x-2 p-4">
      <div class="animate-spin rounded-full h-4 w-4 border-b-2 border-blue-600"></div>
      <span class="text-sm text-gray-600 dark:text-gray-400">Loading message renderer...</span>
    </div>
  {/if}
</div>

<style>
  .message-router {
    position: relative;
  }

  .message-error {
    border-radius: 0.375rem;
  }

  .message-loading {
    background-color: rgb(249 250 251);
    border-radius: 0.5rem;
  }

  /* Dark mode styles */
  :global(.dark) .message-loading {
    background-color: rgb(31 41 55);
  }

  /* Ensure proper z-index for error states */
  .message-error {
    z-index: 10;
  }
</style>
