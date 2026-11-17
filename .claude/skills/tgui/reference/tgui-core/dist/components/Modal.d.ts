import type { BoxProps } from './Box';
export type ModalProps = Partial<{
    /** Fires once the enter key is pressed */
    onEnter: (e: React.KeyboardEvent<HTMLInputElement>) => void;
    /** Fires once the escape key is pressed */
    onEscape: (e: React.KeyboardEvent<HTMLInputElement>) => void;
}> & BoxProps;
/**
 * ## Modal
 *
 * A modal window. Uses a
 * [Dimmer](https://tgstation.github.io/tgui-core/?path=/docs/components-dimmer--docs)
 * under the hood, and dynamically adjusts its own size to fit the content
 * you're trying to display.
 *
 * Must be a direct child of a layout component (e.g. `Window`).
 *
 * - [View documentation on tgui core](https://tgstation.github.io/tgui-core/?path=/docs/components-modal--docs)
 * - [View inherited Box props](https://tgstation.github.io/tgui-core/?path=/docs/components-box--docs)
 */
export declare function Modal(props: ModalProps): import("react/jsx-runtime").JSX.Element;
