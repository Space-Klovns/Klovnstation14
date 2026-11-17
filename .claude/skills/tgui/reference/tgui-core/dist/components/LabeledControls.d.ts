import { type FlexProps } from './Flex';
/**
 * ## LabeledControls
 *
 * LabeledControls is a horizontal grid that is designed to hold various
 * controls, like [Knobs](https://github.com/tgstation/tgui-core/tree/main/lib/components/Knob.tsx)
 * or small [Buttons](https://github.com/tgstation/tgui-core/tree/main/lib/components/Button.tsx).
 *
 * Every item in this grid is labeled at the bottom.
 *
 * Example:
 *
 * ```tsx
 * <LabeledControls>
 *   <LabeledControls.Item label="Temperature"><Knob /></LabeledControls.Item>
 *   <LabeledControls.Item label="Submit"><Button /></LabeledControls.Item>
 * </LabeledControls>
 * ```
 *
 * - [View documentation on tgui core](https://tgstation.github.io/tgui-core/?path=/docs/components-labeledcontrols--docs)
 * - [View inherited Box props](https://tgstation.github.io/tgui-core/?path=/docs/components-box--docs)
 */
export declare function LabeledControls(props: FlexProps): import("react/jsx-runtime").JSX.Element;
export declare namespace LabeledControls {
    var Item: typeof LabeledControlsItem;
}
type ItemProps = {
    label: string;
} & FlexProps;
declare function LabeledControlsItem(props: ItemProps): import("react/jsx-runtime").JSX.Element;
export {};
