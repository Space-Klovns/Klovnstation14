import type { BaseInputProps } from './Input';
type Props = Partial<{
    /** Restricted inputs round by default.  */
    allowFloats: boolean;
    /** Max value. 10,000 by default. */
    maxValue: number;
    /** Min value. 0 by default. */
    minValue: number;
    /** Fires on input validation change */
    onValidationChange: (isValid: boolean) => void;
}> & BaseInputProps<HTMLInputElement, number>;
/**
 * ## RestrictedInput
 *
 * Creates a numerical input which rejects improper keys.
 *
 * Has a special event for changes in validation states - `onValidationChange`.
 *
 * - [View documentation on tgui core](https://tgstation.github.io/tgui-core/?path=/docs/components-restrictedinput--docs)
 * - [View inherited Box props](https://tgstation.github.io/tgui-core/?path=/docs/components-box--docs)
 */
export declare function RestrictedInput(props: Props): import("react/jsx-runtime").JSX.Element;
export {};
