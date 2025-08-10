<script lang="ts">
  import { createEventDispatcher } from 'svelte';
  import type { MessageDto, TextMessageDto } from '$lib/types';
  import type { MessageRenderer } from '$lib/types/renderer';

  // Component props with proper TypeScript typing
  export let message: TextMessageDto;
  export let isLatest: boolean = false;

  // Create event dispatcher for custom events
  const dispatch = createEventDispatcher<{
    stateChange: { expanded: boolean };
  }>();

  // Markdown + sanitization (Phase 2 enhancement)
  import { parseMarkdown } from '$lib/markdown/parse';

  /**
   * Convert markdown content to sanitized HTML. Empty content yields ''.
   */
  function renderContent(content: string | null | undefined): string {
    if (!content) return '';
    return parseMarkdown(content, { devLogging: true });
  }

  $: safeContent = renderContent((message as TextMessageDto).text);

  // Component implements MessageRenderer interface
  const rendererInterface: MessageRenderer<TextMessageDto> = {
    messageType: 'text',
    // Text messages don't have expand functionality - they're always visible
    onExpand: undefined
  };

  // Text renderer configuration
  const supportsStreaming = false; // Phase 1 requirement
  const supportsCollapse = false;  // Text messages don't collapse
</script>

<!-- Text message container -->
<div 
  class="text-renderer"
  class:latest={isLatest}
  data-testid="text-renderer"
  data-message-type="text"
  role="article"
  aria-label="Text message"
>
  <!-- Message content display -->
  <div 
    class="text-content prose"
    class:empty={!safeContent}
    data-testid="text-content"
  >
    {#if safeContent}
      <!-- Display escaped text content -->
      {@html safeContent}
    {:else}
      <!-- Empty content fallback -->
      <span class="empty-content" aria-label="Empty message">
        <em>No content</em>
      </span>
    {/if}
  </div>
</div>

<style>
  .text-renderer {
    position: relative;
    width: 100%;
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
    line-height: 1.6;
    color: rgb(55 65 81);
    background-color: transparent;
  }

  .text-content {
    padding: 0.75rem 1rem;
    border-radius: 0.5rem;
    background-color: rgb(255 255 255);
    border: 1px solid rgb(229 231 235);
    
    /* Typography optimizations */
    font-size: 0.875rem;
    line-height: 1.7;
    
    /* Word wrapping for long text */
    word-wrap: break-word;
    overflow-wrap: break-word;
    hyphens: auto;
    
    /* Text spacing */
    letter-spacing: 0.01em;
    
    /* Ensure proper text flow */
    white-space: pre-wrap;
    
    /* Performance optimization */
    contain: layout style;
  }

  .text-content.empty {
    background-color: rgb(249 250 251);
    border-style: dashed;
  }

  .empty-content {
    color: rgb(156 163 175);
    font-style: italic;
    font-size: 0.8rem;
  }

  /* Latest message highlighting */
  .text-renderer.latest .text-content {
    border-color: rgb(59 130 246);
    box-shadow: 0 0 0 1px rgb(59 130 246 / 0.1);
  }

  /* Dark mode styles */
  :global(.dark) .text-renderer {
    color: rgb(209 213 219);
  }

  :global(.dark) .text-content {
    background-color: rgb(17 24 39);
    border-color: rgb(55 65 81);
  }

  :global(.dark) .text-content.empty {
    background-color: rgb(31 41 55);
  }

  :global(.dark) .empty-content {
    color: rgb(107 114 128);
  }

  :global(.dark) .text-renderer.latest .text-content {
    border-color: rgb(96 165 250);
    box-shadow: 0 0 0 1px rgb(96 165 250 / 0.1);
  }

  /* RTL support removed to avoid unused selector warning; re-add with a global wrapper when needed */

  /* High contrast mode support */
  @media (prefers-contrast: high) {
    .text-content {
      border-width: 2px;
    }
    
    .text-renderer.latest .text-content {
      border-width: 3px;
    }
  }

  /* Mobile optimizations */
  @media (max-width: 640px) {
    .text-content {
      padding: 0.5rem 0.75rem;
      font-size: 0.9rem;
    }
    
    /* Optimize line length for mobile readability */
    .text-content {
      max-width: none;
    }
  }

  /* Large screen optimizations */
  @media (min-width: 1024px) {
    .text-content {
      /* Optimal line length for readability (45-75 characters) */
      max-width: 65ch;
    }
  }

  /* Print styles */
  @media print {
    .text-renderer {
      break-inside: avoid;
    }
    
    .text-content {
      border: 1px solid #000;
      background-color: white !important;
      color: black !important;
    }
  }

  /* Focus styles for accessibility */
  .text-content:focus-within {
    outline: 2px solid rgb(59 130 246);
    outline-offset: 2px;
  }

  /* Selection styles */
  .text-content ::selection {
    background-color: rgb(59 130 246 / 0.2);
  }

  :global(.dark) .text-content ::selection {
    background-color: rgb(96 165 250 / 0.3);
  }
</style>
