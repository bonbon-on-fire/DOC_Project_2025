<script lang="ts">
  import { createEventDispatcher } from 'svelte';
  import { slide } from 'svelte/transition';
  import { quintOut } from 'svelte/easing';
  import type { ExpandableComponent } from '$lib/types/expandable';

  // Component props with proper TypeScript typing
  export let expanded: boolean = true;
  export let collapsible: boolean = true;
  export let title: string = '';
  export let isLatest: boolean = false;

  // Create event dispatcher for custom events
  const dispatch = createEventDispatcher<{
    stateChange: { expanded: boolean };
    toggleExpansion: { expanded: boolean };
  }>();

  // Internal state
  let headerElement: HTMLButtonElement;
  let contentElement: HTMLDivElement;
  let userHasManuallyExpanded = false;

  // Auto-collapse logic based on isLatest prop
  $: if (!isLatest && collapsible && !userHasManuallyExpanded) {
    if (expanded) {
      expanded = false;
      dispatchStateChange();
    }
  }

  /**
   * Toggles the expanded state when collapsible is enabled.
   * Tracks user manual expansion to prevent unwanted auto-collapse.
   */
  function toggle() {
    if (!collapsible) return;

    expanded = !expanded;
    
    // Mark as manually expanded if user expanded a collapsed container
    if (expanded && !isLatest) {
      userHasManuallyExpanded = true;
    }

    dispatchStateChange();
    dispatchToggleExpansion();
  }

  /**
   * Dispatches state change event to parent components.
   */
  function dispatchStateChange() {
    dispatch('stateChange', { expanded });
  }

  /**
   * Dispatches toggle expansion event to parent components.
   */
  function dispatchToggleExpansion() {
    dispatch('toggleExpansion', { expanded });
  }

  /**
   * Handles keyboard navigation for accessibility.
   * Supports Enter and Space keys for toggling.
   */
  function handleKeydown(event: KeyboardEvent) {
    if (!collapsible) return;

    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      toggle();
    }
  }

  /**
   * Prevents text selection during rapid taps on touch devices.
   */
  function handleTouchStart(event: TouchEvent) {
    if (!collapsible) return;
    
    // Prevent text selection during touch interaction
    event.preventDefault();
  }

  // Implement ExpandableComponent interface
  const componentInterface: ExpandableComponent = {
    isCollapsible: collapsible,
    onStateChange: (newExpanded: boolean) => {
      expanded = newExpanded;
      dispatchStateChange();
    }
  };

  // Reset user manual expansion flag when isLatest changes to true
  $: if (isLatest) {
    userHasManuallyExpanded = false;
  }
</script>

<div 
  class="expandable-container"
  class:expanded
  class:collapsible
  class:latest={isLatest}
  data-testid="expandable-container"
>
  {#if collapsible && title}
    <!-- Clickable header for collapsible containers -->
    <button
      bind:this={headerElement}
      class="expandable-header"
      on:click={toggle}
      on:keydown={handleKeydown}
      on:touchstart={handleTouchStart}
      aria-expanded={expanded}
      aria-controls="expandable-content"
      aria-label={expanded ? `Collapse ${title}` : `Expand ${title}`}
      data-testid="expandable-header"
    >
      <!-- Expand/Collapse icon with smooth rotation -->
      <span 
        class="expand-icon"
        class:expanded
        aria-hidden="true"
      >
        ▶
      </span>
      
      <!-- Title text -->
      <span class="header-title">
        {title}
      </span>
    </button>
  {:else if title && !collapsible}
    <!-- Non-collapsible header (just displays title) -->
    <div class="static-header" data-testid="static-header">
      <span class="header-title">
        {title}
      </span>
    </div>
  {/if}

  <!-- Content area with conditional rendering and transitions -->
  {#if expanded}
    <div
      bind:this={contentElement}
      id="expandable-content"
      class="expandable-content"
      transition:slide={{ duration: 200, easing: quintOut }}
      data-testid="expandable-content"
    >
      <slot />
    </div>
  {/if}
</div>

<style>
  .expandable-container {
    position: relative;
    border-radius: 0.5rem;
    overflow: hidden;
    transition: all 200ms ease-out;
  }

  .expandable-container.collapsible {
    border: 1px solid rgb(229 231 235);
    background-color: rgb(249 250 251);
  }

  .expandable-container.expanded {
    box-shadow: 0 1px 3px 0 rgb(0 0 0 / 0.1), 0 1px 2px -1px rgb(0 0 0 / 0.1);
  }

  .expandable-header {
    display: flex;
    align-items: center;
    width: 100%;
    padding: 0.75rem 1rem;
    background: transparent;
    border: none;
    cursor: pointer;
    font-size: 0.875rem;
    font-weight: 500;
    color: rgb(55 65 81);
    transition: all 200ms ease-out;
    
    /* Touch-optimized minimum target size */
    min-height: 44px;
    
    /* Prevent text selection */
    user-select: none;
    -webkit-user-select: none;
    -moz-user-select: none;
    -ms-user-select: none;
  }

  .expandable-header:hover {
    background-color: rgb(243 244 246);
  }

  .expandable-header:focus {
    outline: 2px solid rgb(59 130 246);
    outline-offset: -2px;
  }

  .expandable-header:active {
    background-color: rgb(229 231 235);
    transform: scale(0.98);
  }

  .static-header {
    padding: 0.75rem 1rem;
    font-size: 0.875rem;
    font-weight: 500;
    color: rgb(55 65 81);
    background-color: rgb(249 250 251);
    border-bottom: 1px solid rgb(229 231 235);
  }

  .expand-icon {
    display: inline-block;
    font-size: 0.75rem;
    margin-right: 0.5rem;
    transition: transform 200ms ease-out;
    transform-origin: center;
    width: 12px;
    text-align: center;
  }

  .expand-icon.expanded {
    transform: rotate(90deg);
  }

  .header-title {
    flex: 1;
    text-align: left;
    font-weight: 500;
  }

  .expandable-content {
    padding: 1rem;
    background-color: rgb(255 255 255);
    border-top: 1px solid rgb(229 231 235);
  }

  /* Dark mode styles */
  :global(.dark) .expandable-container.collapsible {
    border-color: rgb(55 65 81);
    background-color: rgb(31 41 55);
  }

  :global(.dark) .expandable-header {
    color: rgb(209 213 219);
  }

  :global(.dark) .expandable-header:hover {
    background-color: rgb(55 65 81);
  }

  :global(.dark) .expandable-header:active {
    background-color: rgb(75 85 99);
  }

  :global(.dark) .static-header {
    background-color: rgb(31 41 55);
    border-bottom-color: rgb(55 65 81);
    color: rgb(209 213 219);
  }

  :global(.dark) .expandable-content {
    background-color: rgb(17 24 39);
    border-top-color: rgb(55 65 81);
  }

  /* Latest message highlighting */
  .expandable-container.latest.collapsible {
    border-color: rgb(59 130 246);
    box-shadow: 0 0 0 1px rgb(59 130 246 / 0.2);
  }

  :global(.dark) .expandable-container.latest.collapsible {
    border-color: rgb(96 165 250);
    box-shadow: 0 0 0 1px rgb(96 165 250 / 0.2);
  }

  /* Performance optimizations */
  .expandable-container {
    contain: layout style;
  }

  .expandable-content {
    contain: layout;
  }

  /* High contrast mode support */
  @media (prefers-contrast: high) {
    .expandable-container.collapsible {
      border-width: 2px;
    }
    
    .expandable-header:focus {
      outline-width: 3px;
    }
  }

  /* Reduced motion support */
  @media (prefers-reduced-motion: reduce) {
    .expandable-container,
    .expandable-header,
    .expand-icon {
      transition: none;
    }
  }

  /* Mobile touch optimizations */
  @media (hover: none) and (pointer: coarse) {
    .expandable-header {
      min-height: 48px; /* Larger touch target on mobile */
      padding: 1rem;
    }

    .expandable-header:hover {
      background-color: transparent; /* Remove hover styles on touch devices */
    }

    .expandable-header:active {
      background-color: rgb(243 244 246);
    }

    :global(.dark) .expandable-header:active {
      background-color: rgb(55 65 81);
    }
  }
</style>
