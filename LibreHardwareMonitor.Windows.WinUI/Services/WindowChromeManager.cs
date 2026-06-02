// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Runtime.InteropServices;

namespace LibreHardwareMonitor.Windows.WinUI.Services;

internal sealed class WindowChromeManager
{
    private const int ShowWindowHide = 0;
    private const int ShowWindowShow = 5;
    private const int ShowWindowMinimize = 6;
    private const int ShowWindowRestore = 9;

    private readonly IntPtr _hwnd;

    public WindowChromeManager(IntPtr hwnd)
    {
        _hwnd = hwnd;
    }

    public bool IsHidden { get; private set; }

    public bool IsHiddenOrMinimizedOrInvisible => IsHidden || !IsWindowVisible(_hwnd) || IsIconic(_hwnd);

    public void HideToTray()
    {
        IsHidden = true;
        ShowWindow(_hwnd, ShowWindowHide);
    }

    public void Minimize()
    {
        IsHidden = false;
        ShowWindow(_hwnd, ShowWindowMinimize);
    }

    public void Restore()
    {
        IsHidden = false;
        ShowWindow(_hwnd, ShowWindowShow);
        // Only un-minimize. The window is hidden to the tray with SW_HIDE while keeping its maximized state, so
        // SW_SHOW alone brings it back as it was; an unconditional SW_RESTORE would also un-maximize it.
        if (IsIconic(_hwnd))
            ShowWindow(_hwnd, ShowWindowRestore);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr windowHandle);
}
