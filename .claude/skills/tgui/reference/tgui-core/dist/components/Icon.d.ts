import { type BooleanLike } from '../common/react.ts';
import type { BoxProps } from './Box';
type Props = {
    /** Icon name. @see https://fontawesome.com/v6/search?o=r&m=free */
    name: string;
} & Partial<{
    /** Icon rotation, in degrees. */
    rotation: number;
    /** Icon size. `1` is normal size, `2` is two times bigger. Fractional numbers are supported. */
    size: number;
    /** Whether an icon should be spinning. Good for load indicators. */
    spin: BooleanLike;
}> & Omit<BoxProps, 'children'>;
export declare function Icon(props: Props): import("react/jsx-runtime").JSX.Element;
type IconStackProps = {
    /** Works same as `Icon` size prop, but for all icons inside. */
    size?: number;
};
declare function IconStack(props: BoxProps & IconStackProps): import("react/jsx-runtime").JSX.Element;
/**
 * ## Icon
 *
 * Renders one of the FontAwesome icons of your choice.
 *
 * Example:
 *
 * ```tsx
 * <Icon name="plus" />
 * ```
 *
 * Icons: https://fontawesome.com/v6/search?o=r&m=free
 *
 * - [View documentation on tgui core](http://localhost:6006/?path=/docs/components-icon--docs)
 * - [View inherited Box props](https://tgstation.github.io/tgui-core/?path=/docs/components-box--docs)
 */
export declare namespace Icon {
    /**
     * ## Icon.Stack
     * Renders children icons on top of each other in order to make your own icon.
     *
     * Example:
   *
  
     * ```tsx
     * <Icon.Stack>
     *   <Icon name="pen" />
     *   <Icon name="slash" />
     * </Icon.Stack>
     * ```
     */
    const Stack: typeof IconStack;
}
export {};
