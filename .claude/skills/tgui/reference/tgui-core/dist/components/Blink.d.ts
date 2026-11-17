import { type ReactNode } from 'react';
type Props = {
    /** Things that blink! */
    children: ReactNode;
} & Partial<{
    /** The interval between blinks, in milliseconds. */
    interval: number;
    /** The time to wait before blinking again, in milliseconds.  */
    time: number;
}>;
/**
 * ## Blink
 *
 * A component that blinks its children at a specified interval.
 *
 * - [View documentation on tgui core](https://tgstation.github.io/tgui-core/?path=/docs/components-blink--docs)
 */
export declare function Blink(props: Props): import("react/jsx-runtime").JSX.Element;
export {};
