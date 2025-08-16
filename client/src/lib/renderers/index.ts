/**
 * Barrel export file for the renderers module.
 * Provides convenient imports for the Rich Message Rendering system.
 */

// Export the main RendererRegistry class and utilities
export {
	RendererRegistry,
	rendererRegistry,
	getRenderer,
	registerRenderer
} from './RendererRegistry.js';

// Re-export types for convenience
export type { MessageRenderer, CustomRenderer } from '../types/index.js';

// Import and register built-in renderers
import type { MessageDto } from '../types/index.js';
import type { MessageRenderer } from '../types/renderer.js';
import { registerRenderer } from './RendererRegistry.js';

/**
 * Registers all built-in message renderers with the global registry.
 * This function should be called during application initialization.
 */
export function registerBuiltInRenderers(): void {
	// Register TextRenderer for text message types
	const textRenderer: MessageRenderer<MessageDto> = {
		messageType: 'text'
	};

	// Register ReasoningRenderer for reasoning message types
	const reasoningRenderer: MessageRenderer<MessageDto> = {
		messageType: 'reasoning'
	};

	// Register ToolCallRenderer for tool call message types
	const toolCallRenderer: MessageRenderer<MessageDto> = {
		messageType: 'tool_call'
	};

	// Register MathToolRenderer for math-specific tool calls
	const mathToolRenderer: MessageRenderer<MessageDto> = {
		messageType: 'math_tool'
	};

	// Register ToolResultRenderer for tool result message types
	const toolResultRenderer: MessageRenderer<MessageDto> = {
		messageType: 'tool_result'
	};
	
	// Register ToolsCallAggregateRenderer for aggregate tool messages
	const toolsAggregateRenderer: MessageRenderer<MessageDto> = {
		messageType: 'tools_aggregate'
	};

	registerRenderer('text', textRenderer, true);
	registerRenderer('reasoning', reasoningRenderer, true);
	registerRenderer('tool_call', toolCallRenderer, true);
	registerRenderer('math_tool', mathToolRenderer, true);
	registerRenderer('tool_result', toolResultRenderer, true);
	registerRenderer('tools_aggregate', toolsAggregateRenderer, true);

	console.info('Built-in message renderers (including tools) registered successfully');
}

/**
 * Gets the Svelte component for a specific renderer type.
 * This is used by MessageRouter to dynamically load renderer components.
 *
 * @param messageType - The message type to get component for
 * @returns Promise that resolves to the Svelte component
 */
export async function getRendererComponent(messageType: string): Promise<any> {
	switch (messageType) {
		case 'text':
			return (await import('../components/TextRenderer.svelte')).default;
		case 'reasoning':
			return (await import('../components/ReasoningRenderer.svelte')).default;
		case 'tool_call':
			return (await import('../components/ToolCallRenderer.svelte')).default;
		case 'math_tool':
			return (await import('../components/MathToolRenderer.svelte')).default;
		case 'tool_result':
			return (await import('../components/ToolResultRenderer.svelte')).default;
		case 'tools_aggregate':
			return (await import('../components/ToolsCallAggregateRenderer.svelte')).default;
		default:
			console.warn(`No component found for message type '${messageType}', using fallback`);
			return null;
	}
}
