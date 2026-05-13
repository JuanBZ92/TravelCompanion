using System.Security.Cryptography;

namespace TravelCompanion.Api.Services;

public static class TemporaryPasswordGenerator
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%";
    private const string All = Upper + Lower + Digits + Symbols;

    public static string Create()
    {
        Span<char> password = stackalloc char[14];
        password[0] = Pick(Upper);
        password[1] = Pick(Lower);
        password[2] = Pick(Digits);
        password[3] = Pick(Symbols);

        for (var index = 4; index < password.Length; index++)
        {
            password[index] = Pick(All);
        }

        RandomNumberGenerator.Shuffle(password);
        return new string(password);
    }

    private static char Pick(string chars)
    {
        return chars[RandomNumberGenerator.GetInt32(chars.Length)];
    }
}
