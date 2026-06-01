// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LibreHardwareMonitor.Windows.WinUI.ViewModels;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    // Reuse one PropertyChangedEventArgs per property name. WinUI's binding engine handles PropertyChanged natively, so
    // each raised event marshals its args across the managed/native boundary and creates a COM-callable wrapper that the
    // interop layer retains. Allocating a fresh PropertyChangedEventArgs on every raise — RefreshValues fires four per
    // sensor on every update tick — therefore leaked a wrapper per raise (managed-heap growth ~28 MB/min → multi-GB).
    private static readonly ConcurrentDictionary<string, PropertyChangedEventArgs> EventArgsByName = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChangedEventHandler? handler = PropertyChanged;
        if (handler == null)
            return;

        PropertyChangedEventArgs args = EventArgsByName.GetOrAdd(propertyName ?? string.Empty, static name => new PropertyChangedEventArgs(name));
        handler(this, args);
    }
}
