import { describe, test, expect, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import TextRenderer from './TextRenderer.svelte';
import { streamingState } from '$lib/stores/chat';

function setSnapshot(messageId: string, data: any) {
  streamingState.update((s) => ({
    ...s,
    streamingSnapshots: { ...s.streamingSnapshots, [messageId]: data }
  }));
}

describe('TextRenderer - per-message streaming behavior', () => {
  const msgId = 'm-t1:text';
  const baseMessage = {
    id: msgId,
    chatId: 'c1',
    role: 'assistant',
    timestamp: new Date(),
    sequenceNumber: 2,
    messageType: 'text',
    text: ''
  } as any;

  beforeEach(() => {
    streamingState.set({
      isStreaming: false,
      currentMessageId: null,
      streamingSnapshots: {},
      error: null
    });
  });

  test('shows thinking indicator when streaming with empty delta', async () => {
    setSnapshot(msgId, { messageType: 'text', isStreaming: true, phase: 'streaming', textDelta: '' });
    render(TextRenderer, { props: { message: baseMessage } });

    // It renders the "AI is thinking" placeholder
    const el = await screen.findByText(/AI is thinking/i);
    expect(el).toBeTruthy();
  });

  test('renders streamed text delta when available', async () => {
    setSnapshot(msgId, { messageType: 'text', isStreaming: true, phase: 'streaming', textDelta: 'Hello' });
    render(TextRenderer, { props: { message: baseMessage } });

    const content = await screen.findByTestId('message-content');
    expect(content.innerHTML).toContain('Hello');
  });

  test('renders final DTO text when not streaming', async () => {
    const finalMsg = { ...baseMessage, text: 'Final text' };
    render(TextRenderer, { props: { message: finalMsg } });
    const content = await screen.findByTestId('message-content');
    expect(content.innerHTML).toContain('Final text');
  });
});

