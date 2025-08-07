/**
 * Interface for components that support expand/collapse behavior.
 * Provides consistent interaction patterns for collapsible content.
 */
export interface ExpandableComponent {
  /**
   * Whether this component supports collapse functionality.
   * When false, the component should always remain fully visible.
   * 
   * @example Text messages typically are not collapsible, while reasoning messages are
   */
  readonly isCollapsible: boolean;

  /**
   * Optional callback triggered when the component's expanded state changes.
   * Allows parent components to track and respond to expand/collapse events.
   * 
   * @param expanded - true when expanded, false when collapsed
   */
  onStateChange?(expanded: boolean): void;
}
