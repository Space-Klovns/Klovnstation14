import { type RefObject } from 'react';
import type { BoxProps } from './Box';
/** Takes two optional params: The dom element type & the input type */
export type BaseInputProps<TElement = HTMLInputElement, TInput = string> = Partial<{
    /** Automatically focuses the input on mount */
    autoFocus: boolean;
    /** Automatically selects the input value on focus */
    autoSelect: boolean;
    /** Custom css classes */
    className: string;
    /** Disables the input. Outlined in gray */
    disabled: boolean;
    /**
     * Whether to debounce the onChange event.
     *
     * Do this if it's performing expensive ops on each input, like filtering or
     * sending the value immediate to Byond (via act).
     *
     * It will only fire once every 250ms by default. Pass in a number in ms
     * for a custom fire rate
     */
    expensive: boolean | number;
    /** Fills the parent container */
    fluid: boolean;
    /** Mark this if you want to use a monospace font */
    monospace: boolean;
    /** Will force an update when value changes even if the input isn't currently highlighted */
    alwaysUpdate: boolean;
    /** Fires each time focus leaves the input, including if Esc or Enter are pressed */
    onBlur: (value: TInput) => void;
    /**
     * Fires each time the input has been changed. You do not need to enter the second param unless you're using it. All of these are valid:
     *
     * @example
     * ```tsx
     * <Input onChange={(value) => console.log(value)} />
     * <Input onChange={(value, event) => console.log(value, event)} />
     * <Input onChange={console.log} /> // This will log the value and the event
     * <Input onChange={setValue} /> // This will just change the value state
     * ```
     */
    onChange: (value: TInput, event?: React.ChangeEvent<TElement>) => void;
    /** Fires once the enter key is pressed */
    onEnter: (value: TInput, event?: React.KeyboardEvent<TElement>) => void;
    /** Fires once the escape key is pressed */
    onEscape: (value: TInput, event?: React.KeyboardEvent<TElement>) => void;
    /**
     * Generally, input can handle its own state value. You might not NEED this.
     *
     * Use this if you want to hold the value in the parent for external
     * manipulation. For instance:
     *
     * Clearing the input
     *
     * ```tsx
     * const [value, setValue] = useState('');
     *
     * return (
     *  <>
     *    <Button onClick={() => act('inputVal', {inputVal: value})}>
     *      Submit
     *    </Button>
     *    <Input
     *      value={value}
     *      onChange={setValue} />
     *    <Button onClick={() => setValue('')}>
     *      Clear
     *    </Button>
     *  </>
     * )
     * ```
     *
     * Updating the value from the backend
     *
     * ```tsx
     * const { data } = useBackend<Data>();
     * const { valveSetting } = data;
     *
     * return (
     *  <Input
     *    value={valveSetting}
     *    onEnter={(value) => act('submit', { valveSetting: value })}
     *  />
     * )
     * ```
     */
    value: TInput;
}> & BoxProps<TElement>;
export type TextInputProps<TElement = HTMLInputElement> = Partial<{
    /** The maximum length of the input value */
    maxLength: number;
    /** The placeholder text when everything is cleared */
    placeholder: string;
    /** Ref of the input element */
    ref: RefObject<TElement | null>;
    /** Clears the input value on enter */
    selfClear: boolean;
    /** Allows to toggle on spellcheck on inputs */
    spellcheck: boolean;
}> & BaseInputProps<TElement>;
/**
 * ## Input
 *
 * A basic text input which allow users to enter text into a UI.
 *
 * - [View documentation on tgui core](https://tgstation.github.io/tgui-core/?path=/docs/components-input--docs)
 * - [View inherited Box props](https://tgstation.github.io/tgui-core/?path=/docs/components-box--docs)
 */
export declare function Input(props: TextInputProps): import("react/jsx-runtime").JSX.Element;
