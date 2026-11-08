import { type BoxProps } from './Box';
type Props = {
    data: number[][];
} & Partial<{
    fillColor: string;
    rangeX: [number, number];
    rangeY: [number, number];
    strokeColor: string;
    strokeWidth: number;
}> & BoxProps;
/**
 * ## Chart
 *
 * A simple chart component that displays a polyline based on the provided data.
 *
 * It normalizes the data to fit within the viewBox dimensions and allows for custom fill and stroke colors.
 *
 * - [View inherited Box Props](https://tgstation.github.io/tgui-core/?path=/docs/components-box--docs)
 */
export declare function Chart(props: Props): import("react/jsx-runtime").JSX.Element;
export declare namespace Chart {
    var Line: typeof Chart;
}
export {};
