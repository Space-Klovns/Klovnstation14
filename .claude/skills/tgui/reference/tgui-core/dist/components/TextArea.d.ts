import type { TextInputProps } from './Input';
type Props = Partial<{
    /** Don't use tab for indent */
    dontUseTabForIndent: boolean;
    /**
     * Provides a Record with key: markupChar entries which can be used for
     * ctrl + key combinations to surround a selected text with the markup
     * character
     */
    userMarkup: Record<string, string>;
}> & TextInputProps<HTMLTextAreaElement>;
/**
 * ## Textarea
 *
 * An input for larger amounts of text. Use this when you want inputs larger
 * than one row.
 *
 * - [View documentation on tgui core](https://tgstation.github.io/tgui-core/?path=/docs/components-textarea--docs)
 * - [View inherited Box props](https://tgstation.github.io/tgui-core/?path=/docs/components-box--docs)
 */
export declare function TextArea(props: Props): import("react/jsx-runtime").JSX.Element;
export {};
