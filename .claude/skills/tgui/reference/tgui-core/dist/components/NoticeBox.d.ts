import { type BoxProps } from './Box';
type Props = ExclusiveProps & BoxProps;
/** You MUST use only one or none */
type NoticeType = 'info' | 'success' | 'danger';
type None = {
    [K in NoticeType]?: undefined;
};
type ExclusiveProps = None | (Omit<None, 'info'> & {
    /** Blue notice */
    info: boolean;
}) | (Omit<None, 'success'> & {
    /** Green notice */
    success: boolean;
}) | (Omit<None, 'danger'> & {
    /** Red notice */
    danger: boolean;
});
/**
 * ## NoticeBox
 *
 * A notice box which warns you about something very important.
 *
 * - [View documentation on tgui core](https://tgstation.github.io/tgui-core/?path=/docs/components-noticebox--docs)
 * - [View inherited Box props](https://tgstation.github.io/tgui-core/?path=/docs/components-box--docs)
 */
export declare function NoticeBox(props: Props): import("react/jsx-runtime").JSX.Element;
export {};
