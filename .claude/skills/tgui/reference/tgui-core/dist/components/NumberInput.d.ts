import { type BooleanLike } from '../common/react.ts';
import { Component, type FocusEventHandler, type KeyboardEventHandler } from 'react';
import { type BoxProps } from './Box';
type Props = Required<{
    /** Value itself. */
    value: number | string;
}> & Partial<{
    /** Highest possible value. */
    maxValue: number;
    /** Lowest possible value. */
    minValue: number;
    /** Adjust value by this amount when dragging the input. */
    step: number;
    /** Animates the value if it was changed externally. */
    animated: boolean;
    /** Makes the input field uneditable & non draggable to prevent user changes */
    disabled: boolean;
    /** onChange also fires about every 500ms when you drag the input up and down. */
    tickWhileDragging: boolean;
    /** Format value using this function before displaying it. */
    format: (value: number) => string;
    /** Adjusts the width to 100% of its parent container */
    fluid: boolean;
    /** An event which fires when you release the input or successfully enter a number. */
    onChange: (value: number) => void;
    /** Screen distance mouse needs to travel to adjust value by one `step`. */
    stepPixelSize: number;
    /** Unit to display to the right of value. */
    unit: string;
}> & BoxProps;
type State = {
    currentValue: number;
    dragging: BooleanLike;
    editing: BooleanLike;
    origin: number;
    previousValue: number;
};
/**
 * ## NumberInput
 *
 * A fancy, interactive number input, which you can either drag up and down
 * to fine tune the value, or single click it to manually type a number.
 *
 * - [View documentation on tgui core](https://tgstation.github.io/tgui-core/?path=/docs/components-numberinput--docs)
 * - [View inherited Box props](https://tgstation.github.io/tgui-core/?path=/docs/components-box--docs)
 */
export declare class NumberInput extends Component<Props, State> {
    inputRef: import("react").RefObject<HTMLInputElement | null>;
    dragTimeout: NodeJS.Timeout;
    dragInterval: NodeJS.Timeout;
    state: State;
    static defaultProps: Partial<Props>;
    componentDidMount(): void;
    handleDragStart: React.MouseEventHandler<HTMLDivElement>;
    handleDragMove: (event: MouseEvent) => void;
    handleDragEnd: (_event: MouseEvent) => void;
    handleBlur: FocusEventHandler<HTMLInputElement>;
    handleKeyDown: KeyboardEventHandler<HTMLInputElement>;
    render(): import("react/jsx-runtime").JSX.Element;
}
export {};
