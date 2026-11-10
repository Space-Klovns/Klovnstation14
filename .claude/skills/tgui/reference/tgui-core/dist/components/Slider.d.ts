import type { PropsWithChildren } from 'react';
import type { BoxProps } from './Box';
type Props = {
    /** Highest possible value. */
    maxValue: number;
    /** Lowest possible value. */
    minValue: number;
    /** Value itself, controls the position of the cursor. */
    value: number;
} & Partial<{
    /** Animates the value if it was changed externally. */
    animated: boolean;
    /** Custom css */
    className: string;
    /** Color of the slider. */
    color: string;
    /** Disables the slider. */
    disabled: boolean;
    /** onChange also fires when you drag the input. */
    tickWhileDragging: boolean;
    /**
     * If set, this value will be used to set the fill percentage of the
     * progress bar filler independently of the main value.
     */
    fillValue: number;
    /** Format value using this function before displaying it. */
    format: (value: number) => string;
    /**
     * An event which fires when you release the slider or enter a number. This is
     * the default value event for controls.
     */
    onChange: (event: Event, value: number) => void;
    /**
     * Applies a `color` to the slider based on whether the value lands in the
     * range between `from` and `to`.
     */
    ranges: Record<string, [number, number]>;
    /** Screen distance mouse needs to travel to adjust value by one `step`. */
    step: number;
    /**
     * The sensitivity of the slider. Adjust this if it feels too slow/fast
     * while dragging. Use higher for larger sliders. Default is 1.
     */
    stepPixelSize: number;
    /** Unit to display to the right of value. */
    unit: string;
}> & BoxProps & PropsWithChildren;
/**
 * ## Slider
 *
 * A horizontal,
 * [ProgressBar](https://tgstation.github.io/tgui-core/?path=/docs/components-progressbar--docs)-like
 * control which allows dialing * in precise values by dragging it left and right.
 *
 * Single click opens an input box to manually type in a number.
 *
 * - [View documentation on tgui core](https://tgstation.github.io/tgui-core/?path=/docs/components-slider--docs)
 * - [View inherited Box props](https://tgstation.github.io/tgui-core/?path=/docs/components-box--docs)
 */
export declare function Slider(props: Props): import("react/jsx-runtime").JSX.Element;
export {};
