using System.Security.Cryptography;

namespace SysWlan.Core;

public static class PasswordGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@$%*-_";

    public static string Generate(int length = 24)
    {
        if (length is < 16 or > 128) throw new ArgumentOutOfRangeException(nameof(length), "Passwortlänge muss zwischen 16 und 128 Zeichen liegen.");
        var chars = new char[length];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }
}
