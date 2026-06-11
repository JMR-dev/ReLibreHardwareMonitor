// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using LibreHardwareMonitor.Windows.WinUI.Services;
using Xunit;

namespace LibreHardwareMonitor.Windows.WinUI.Tests.Services;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_ThenVerify_Succeeds()
    {
        string hash = PasswordHasher.Hash("correct horse");
        Assert.True(PasswordHasher.Verify("correct horse", hash, out bool isLegacy));
        Assert.False(isLegacy);
    }

    [Fact]
    public void Verify_WrongPassword_Fails()
    {
        string hash = PasswordHasher.Hash("secret");
        Assert.False(PasswordHasher.Verify("guess", hash, out _));
    }

    [Fact]
    public void Hash_UsesSelfDescribingPbkdf2Format()
    {
        string hash = PasswordHasher.Hash("secret");
        Assert.StartsWith("pbkdf2$", hash);
        Assert.Equal(4, hash.Split('$').Length);
    }

    [Fact]
    public void Hash_UsesRandomSalt_SoTwoHashesOfSamePasswordDiffer()
    {
        Assert.NotEqual(PasswordHasher.Hash("secret"), PasswordHasher.Hash("secret"));
    }

    [Fact]
    public void Verify_AcceptsLegacySha256_AndReportsLegacy()
    {
        string legacy = PasswordHasher.ComputeLegacySha256("secret");
        Assert.True(PasswordHasher.Verify("secret", legacy, out bool isLegacy));
        Assert.True(isLegacy);
        Assert.False(PasswordHasher.Verify("wrong", legacy, out _));
    }

    [Fact]
    public void ComputeLegacySha256_KnownVector()
    {
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", PasswordHasher.ComputeLegacySha256("abc"));
    }

    [Fact]
    public void Verify_EmptyStoredHash_Fails()
    {
        Assert.False(PasswordHasher.Verify("anything", "", out _));
    }

    [Theory]
    [InlineData("pbkdf2$notanumber$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2$100000$not-base64$aGFzaA==")]
    [InlineData("pbkdf2$100000")]
    public void Verify_MalformedPbkdf2_Fails(string storedHash)
    {
        Assert.False(PasswordHasher.Verify("secret", storedHash, out _));
    }
}
