import type { BoxProps } from './Box';
type Props = Partial<{
    /** True is default, this fixes DM icon rendering issues */
    fixBlur: boolean;
    /**
     * False by default. Good if you're fetching images on UIs that do not auto
     * update. This will attempt to fix the 'x' icon 5 times.
     */
    fixErrors: boolean;
    /** Fill is default. */
    objectFit: 'contain' | 'cover';
    /**
     * The image source.
     *
     * Use transparent base64 pixel if there is no src so we don't get a broken
     * image icon when using assets.
     */
    src: string;
}> & BoxProps;
/**
 * ## Image
 *
 * A wrapper for the `<img>` element.
 *
 * It can attempt to fix broken images by fetching them again with `fixErrors`.
 *
 * It will also try to fix blurry images by rendering them pixelated.
 */
export declare function Image(props: Props): import("react/jsx-runtime").JSX.Element;
export {};
