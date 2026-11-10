import { type ReactElement } from 'react';
type Props = {
    /** The excluded element that CAN be clicked */
    children: ReactElement<HTMLElement>;
    /** Callback that fires whenever the user clicks something else */
    onOutsideClick: () => void;
};
/**
 * ## TrackOutsideClicks
 *
 * Allows you to track when the user clicks outside of a specific element.
 *
 * Example:
 *
 * ```tsx
 * import { TrackOutsideClicks } from 'tgui-core/components';
 *
 * function MyComponent() {
 *  const [isOpen, setIsOpen] = useState(false);
 *
 *  return (
 *    <TrackOutsideClicks onOutsideClick={() => setIsOpen(false)}>
 *     <div>
 *       Hello world!
 *    </div>
 *   </TrackOutsideClicks>
 *  );
 * }
 * ```
 *
 * - [View documentation on tgui core](https://tgstation.github.io/tgui-core/?path=/docs/components-trackoutsideclicks--docs)
 */
export declare function TrackOutsideClicks(props: Props): import("react/jsx-runtime").JSX.Element;
export {};
