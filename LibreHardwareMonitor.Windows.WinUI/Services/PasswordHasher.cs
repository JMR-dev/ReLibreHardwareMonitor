// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LibreHardwareMonitor.Windows.WinUI.Services;

/// <summary>
/// Hashes and verifies the remote web server's Basic-auth password.
/// <para>
/// New credentials use PBKDF2-HMAC-SHA256 with a per-credential random salt, stored in the self-describing format
/// <c>pbkdf2$&lt;iterations&gt;$&lt;base64 salt&gt;$&lt;base64 hash&gt;</c>. <see cref="Verify" /> also accepts the
/// legacy unsalted SHA-256 hex hash that older configurations stored, so existing <c>authenticationPassword</c>
/// settings keep working; callers can then opportunistically re-hash to upgrade them.
/// </para>
/// </summary>
internal static class PasswordHasher
{
    private const string Pbkdf2Prefix = "pbkdf2$";
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    /// <summary>Produces a salted PBKDF2 hash string for <paramref name="password" />.</summary>
    public static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Pbkdf2Prefix}{Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    /// <summary>
    /// Verifies <paramref name="password" /> against <paramref name="storedHash" /> in constant time.
    /// <paramref name="isLegacy" /> reports whether the stored hash used the old unsalted SHA-256 scheme, so the caller
    /// can transparently upgrade it.
    /// </summary>
    public static bool Verify(string password, string storedHash, out bool isLegacy)
    {
        isLegacy = false;
        if (string.IsNullOrEmpty(storedHash))
            return false;

        if (storedHash.StartsWith(Pbkdf2Prefix, StringComparison.Ordinal))
            return VerifyPbkdf2(password, storedHash);

        // Legacy unsalted SHA-256 hex hash.
        isLegacy = true;
        return CredentialComparer.FixedTimeEquals(ComputeLegacySha256(password), storedHash);
    }

    /// <summary>Lowercase hex SHA-256, matching the hash older versions stored. Kept only to verify/upgrade legacy values.</summary>
    public static string ComputeLegacySha256(string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return string.Concat(hash.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }

    private static bool VerifyPbkdf2(string password, string storedHash)
    {
        string[] parts = storedHash.Split('$');
        if (parts.Length != 4)
            return false;

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int iterations) || iterations <= 0)
            return false;

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        // Reject a missing salt or hash segment: an empty expected hash would make FixedTimeEquals(empty, empty) return
        // true and authenticate any password. Hash() never produces this, so it only arises from a corrupted value.
        if (salt.Length == 0 || expected.Length == 0)
            return false;

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
