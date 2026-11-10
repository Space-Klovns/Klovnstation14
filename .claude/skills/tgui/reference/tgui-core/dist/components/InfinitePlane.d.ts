import { type PropsWithChildren } from 'react';
import type { BoxProps } from './Box';
type Props = {
    /** The width of the image to display. */
    imageWidth: number;
} & Partial<{
    /** The background image to display. */
    backgroundImage: string;
    /** The initial left position of the image. */
    initialLeft: number;
    /** The initial top position of the image. */
    initialTop: number;
    /** Padding applied to the right of the zoom controls */
    zoomPadding: number;
    /**
     * Minimum level of zoom possible
     * @default 0.5
     */
    minimumZoom: number;
    /**
     * Maximum level of zoom possible
     * @default 1.5
     */
    maximumZoom: number;
    /**
     * Increments by which zoom level changes every scroll/button click
     * @default 0.1
     */
    zoomIncrement: number;
    /** X position to snap to. When this value is changed, the element will snap to said position */
    zoomToX: number;
    /** Y position to snap to. When this value is changed, the element will snap to said position */
    zoomToY: number;
    /** A callback function that is called when the background image is moved. */
    onBackgroundMoved: (newX: number, newY: number) => void;
    /** A callback function that is called when the zoom value changes. */
    onZoomChange: (newZoomValue: number) => void;
}> & BoxProps & PropsWithChildren;
/**
 * ## InfinitePlane
 *
 * Creates a scrolling infinite plane using a background image.
 *
 * Example:
 *
 * ```tsx
 * <InfinitePlane imageWidth={100} backgroundImage="https://example.com/image.png">
 *   <Box position="absolute" top={0} left={0}>
 *     Hello, world!
 *   </Box>
 *   <Box position="absolute" top={0} left={100}>
 *     Hello, world!
 *   </Box>
 *   <Box position="absolute" top={0} left={200}>
 *     Hello, world!
 *   </Box>
 * </InfinitePlane>
 * ```
 *
 * - [View documentation on tgui core](https://tgstation.github.io/tgui-core/?path=/docs/components-infiniteplane--docs)
 * - [View inherited Box props](https://tgstation.github.io/tgui-core/?path=/docs/components-box--docs)
 */
export declare function InfinitePlane(props: Props): import("react/jsx-runtime").JSX.Element;
export {};
