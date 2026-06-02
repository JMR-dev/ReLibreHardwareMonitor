// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using LibreHardwareMonitor.Windows.WinUI.Services;
using Xunit;

namespace LibreHardwareMonitor.Windows.WinUI.Tests.Services;

public class CredentialComparerTests
{
    [Fact]
    public void FixedTimeEquals_EqualStrings_ReturnsTrue()
    {
        Assert.True(CredentialComparer.FixedTimeEquals("admin", "admin"));
    }

    [Theory]
    [InlineData("admin", "Admin")]
    [InlineData("admin", "root")]
    [InlineData("admin", "administrator")]
    [InlineData("", "x")]
    public void FixedTimeEquals_DifferentStrings_ReturnsFalse(string left, string right)
    {
        Assert.False(CredentialComparer.FixedTimeEquals(left, right));
    }
}
