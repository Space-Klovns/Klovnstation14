import { type RefObject } from 'react';
import { type FlexItemProps, type FlexProps } from './Flex';
type Props = Partial<{
    /** Fills available space. */
    fill: boolean;
    /** Reverses the stack. */
    reverse: boolean;
    /** Flex column */
    vertical: boolean;
    /** Adds zebra striping to the stack. */
    zebra: boolean;
}> & FlexProps;
/**
 * ## Stack
 *
 * A higher-level component that is based on
 * [Flex](https://tgstation.github.io/tgui-core/?path=/docs/components-flex--docs).
 *
 * The main difference from `Flex`, is that this component automatically adds
 * spacing between all stack items, reducing the boilerplate that you have to write!
 *
 * Consists of two elements: `<Stack>` and `<Stack.Item>`.
 *
 * Stacks can be vertical by adding a `vertical` property.
 *
 * Example:
 *
 * ```tsx
 * <Stack vertical>
 *   <Stack.Item grow>Button description</Stack.Item>
 *   <Stack.Item>
 *     <Button>Perform an action</Button>
 *   </Stack.Item>
 * </Stack>
 * ```
 *
 * **High level window layout**
 * Stacks can be used for high level window layout.
 * Make sure to use the `fill` property.
 *
 * Example:
 *
 * ```tsx
 * <Window>
 *   <Window.Content>
 *     <Stack fill>
 *       <Stack.Item>
 *         <Section fill>Sidebar</Section>
 *       </Stack.Item>
 *       <Stack.Item grow>
 *         <Stack fill vertical>
 *           <Stack.Item grow>
 *             <Section fill scrollable>
 *               Main content
 *             </Section>
 *           </Stack.Item>
 *           <Stack.Item>
 *             <Section>Bottom pane</Section>
 *           </Stack.Item>
 *         </Stack>
 *       </Stack.Item>
 *     </Stack>
 *   </Window.Content>
 * </Window>
 * ```
 *
 * - [View documentation on tgui core](https://tgstation.github.io/tgui-core/?path=/docs/components-stack--docs)
 * - [View inherited Box props](https://tgstation.github.io/tgui-core/?path=/docs/components-box--docs)
 */
export declare function Stack(props: Props): import("react/jsx-runtime").JSX.Element;
export declare namespace Stack {
    var Item: typeof StackItem;
    var Divider: typeof StackDivider;
}
type StackItemProps = FlexItemProps & Partial<{
    innerRef: RefObject<HTMLDivElement | null>;
}>;
declare function StackItem(props: StackItemProps): import("react/jsx-runtime").JSX.Element;
type StackDividerProps = FlexItemProps & Partial<{
    hidden: boolean;
}>;
declare function StackDivider(props: StackDividerProps): import("react/jsx-runtime").JSX.Element;
export {};
