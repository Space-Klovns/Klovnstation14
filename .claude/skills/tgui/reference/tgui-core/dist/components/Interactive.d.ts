/**
 * MIT License
 * https://github.com/omgovich/react-colorful/
 *
 * Copyright (c) 2020 Vlad Shilov <omgovich@ya.ru>
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */
import type { CSSProperties, ReactNode, RefObject } from 'react';
export type Interaction = {
    left: number;
    top: number;
};
export type InteractiveProps = {
    /** Callback when interaction moves */
    onMove: (interaction: Interaction) => void;
    /** Callback when key is pressed */
    onKey: (offset: Interaction) => void;
    /** Child elements */
    children: ReactNode;
    /** Ref to the container element */
    containerRef: RefObject<HTMLDivElement | null>;
    /** Optional styles */
    style?: CSSProperties;
};
/**
 * ## Interactive
 *
 * A low-level component that handles pointer and keyboard interactions.
 *
 * See an example of usage in the story:
 * - [View Colorpicker story](https://tgstation.github.io/tgui-core/?path=/story/interfaces-colorpickermodal--default)
 */
export declare function Interactive(props: InteractiveProps): import("react/jsx-runtime").JSX.Element;
