type Props = {
    /** The target value to approach. */
    value: number;
} & Partial<{
    /**
     * If provided, a function that formats the inner string. By default,
     * attempts to match the numeric precision of `value`.
     */
    format: (value: number) => string;
    /**
     * If provided, the initial value displayed. By default, the same as `value`.
     * If `initial` and `value` are different, the component immediately starts
     * animating.
     */
    initial: number;
}>;
/**
 * ## AnimatedNumber
 *
 * An animated number label. Shows a number, formatted with an optionally
 * provided function, and animates it towards its target value.
 */
export declare function AnimatedNumber(props: Props): import("react/jsx-runtime").JSX.Element;
export {};
