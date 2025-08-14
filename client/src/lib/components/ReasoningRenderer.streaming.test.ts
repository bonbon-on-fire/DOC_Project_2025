import { describe, test, expect, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import ReasoningRenderer from './ReasoningRenderer.svelte';
import { streamingState } from '$lib/stores/chat';

function setSnapshot(messageId: string, data: any) {
  streamingState.update((s) => ({
    ...s,
    streamingSnapshots: { ...s.streamingSnapshots, [messageId]: data }
  }));
}

describe('ReasoningRenderer - per-message streaming behavior', () => {
  const msgId = 'm-r1:reasoning';
  const baseMessage = {
    id: msgId,
    chatId: 'c1',
    role: 'assistant',
    timestamp: new Date(),
    sequenceNumber: 1,
    messageType: 'reasoning',
    reasoning: ''
  } as any;

  beforeEach(() => {
    streamingState.set({
      isStreaming: false,
      currentMessageId: null,
      streamingSnapshots: {},
      error: null
    });
  });

  test('renders its own reasoning delta while streaming', async () => {
    setSnapshot(msgId, {
      messageType: 'reasoning',
      isStreaming: true,
      phase: 'streaming',
      reasoningDelta: 'thinking...'
    });

    render(ReasoningRenderer, { props: { message: baseMessage, isLatest: true, expanded: true } });

    const content = await screen.findByTestId('streaming-reasoning-content');
    expect(content.innerHTML).toContain('thinking...');
  });

  test('when not streaming, falls back to final DTO reasoning or its own snapshot delta', async () => {
    // Snapshot with non-streaming, no final DTO
    setSnapshot(msgId, {
      messageType: 'reasoning',
      isStreaming: false,
      phase: 'complete',
      reasoningDelta: 'finalized in snapshot'
    });

    render(ReasoningRenderer, { props: { message: baseMessage, isLatest: false, expanded: true } });

    const content = await screen.findByTestId('reasoning-content');
    expect(content.innerHTML).toContain('finalized in snapshot');

    // If DTO has reasoning, it takes precedence
    const withDto = { ...baseMessage, reasoning: 'DTO finalized' };
    render(ReasoningRenderer, { props: { message: withDto, isLatest: false, expanded: true } });
    const content2 = await screen.findAllByTestId('reasoning-content');
    expect(content2.pop()!.innerHTML).toContain('DTO finalized');
  });
});

