<script lang="ts">
  import { goto } from '$app/navigation';
  import { onMount } from 'svelte';

  onMount(() => {
    // Redirect to chat page
    goto('/chat');
  });
</script>

<svelte:head>
  <title>AI Chat App</title>
  <meta name="description" content="AI-powered chat application with real-time streaming" />
</svelte:head>

<main class="h-screen flex items-center justify-center bg-gray-100 dark:bg-gray-900">
  <div class="text-center">
    <div class="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600 mx-auto mb-4"></div>
    <p class="text-gray-600 dark:text-gray-300">Loading AI Chat...</p>
  </div>
</main>
