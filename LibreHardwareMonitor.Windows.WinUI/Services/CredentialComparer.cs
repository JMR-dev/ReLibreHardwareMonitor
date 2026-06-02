// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System.Security.Cryptography;
using System.Text;

namespace LibreHardwareMonitor.Windows.WinUI.Services;

internal static class CredentialComparer
{
    /// <summary>
    /// Compares two strings for equality in time that does not depend on where they first differ, so an attacker cannot
    /// learn a correct prefix from response timing. Used for the user name and password-hash comparisons.
    /// </summary>
    public static bool FixedTimeEquals(string left, string right)
    {
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
    }
}
