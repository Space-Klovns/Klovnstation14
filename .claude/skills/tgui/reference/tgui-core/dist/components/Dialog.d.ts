import type { ReactNode } from 'react';
type DialogProps = {
    /** The content of the dialog. */
    children: ReactNode;
    /** The function to call when close is clicked */
    onClose: () => void;
    /** The title of the dialog. */
    title: ReactNode;
} & Partial<{
    /** The height of the dialog. */
    height: string;
    /** The width of the dialog. */
    width: string;
}>;
/**
 * ## Dialog
 *
 * A themed dialog for user interaction.
 *
 * Example:
 *
 * ```tsx
 * <Dialog title="Dialog Title" onClose={() => {}}>
 *   <div>Dialog Content</div>
 * </Dialog>
 * ```
 *
 * - [View documentation on tgui core](https://tgstation.github.io/tgui-core/?path=/docs/components-dialog--docs)
 */
export declare function Dialog(props: DialogProps): import("react/jsx-runtime").JSX.Element;
export declare namespace Dialog {
    var Button: typeof DialogButton;
}
type DialogButtonProps = {
    children: ReactNode;
    onClick: () => void;
};
declare function DialogButton(props: DialogButtonProps): import("react/jsx-runtime").JSX.Element;
export {};
