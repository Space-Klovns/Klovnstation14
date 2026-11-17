import type { KeyEvent } from '../common/events.ts';
type Props = Partial<{
    onKey: (key: KeyEvent) => void;
    onKeyDown: (key: KeyEvent) => void;
    onKeyUp: (key: KeyEvent) => void;
}>;
/**
 * ## KeyListener
 *
 * A component that listens for keyboard events and calls the provided
 * callbacks.
 *
 * - [View documentation on tgui core](https://tgstation.github.io/tgui-core/?path=/docs/components-keylistener--docs)
 */
export declare function KeyListener(props: Props): null;
export {};
