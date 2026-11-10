import { type BooleanLike } from '../common/react.ts';
import { type PropsWithChildren, type ReactNode } from 'react';
type MenuBarItemProps = {
    children: ReactNode;
    className?: string;
    disabled?: boolean;
    display: ReactNode;
    entry: string;
    openMenuBar: string | null;
    openOnHover: boolean;
    openWidth: string;
    setOpenMenuBar: (entry: string | null) => void;
    setOpenOnHover: (flag: boolean) => void;
};
declare function MenuDropdown(props: MenuBarItemProps): import("react/jsx-runtime").JSX.Element;
declare namespace MenuDropdown {
    var MenuItem: (props: MenuItemProps) => import("react/jsx-runtime").JSX.Element;
    var MenuItemToggle: (props: MenuItemToggleProps) => import("react/jsx-runtime").JSX.Element;
    var Submenu: typeof MenuItemSubmenu;
    var Separator: () => import("react/jsx-runtime").JSX.Element;
}
type MenuItemProps = Partial<{
    value: any;
    displayText: ReactNode;
    disabled: BooleanLike;
    onClick: (value: any) => void;
}>;
type MenuItemToggleProps = MenuItemProps & Partial<{
    checked: BooleanLike;
}>;
type MenuItemSubmenuProps = PropsWithChildren<Omit<MenuItemProps, 'value' | 'onClick'> & Partial<{
    openWidth: string;
}>>;
declare function MenuItemSubmenu(props: MenuItemSubmenuProps): import("react/jsx-runtime").JSX.Element;
type MenuBarProps = {
    children: ReactNode;
};
export declare function MenuBar(props: MenuBarProps): import("react/jsx-runtime").JSX.Element;
export declare namespace MenuBar {
    var Dropdown: typeof MenuDropdown;
}
export {};
