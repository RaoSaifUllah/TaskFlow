using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Microsoft.AspNetCore.Identity;

namespace TaskFlow.Infrastructure.Identity;

public sealed class Argon2PasswordHasher : IPasswordHasher<TaskFlowUser>
{
    public const int MemoryKiB = 65536;
    public const int Iterations = 3;
    public const int Parallelism = 2;
    public string HashPassword(TaskFlowUser user, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Derive(password, salt, MemoryKiB, Iterations, Parallelism);
        return $"$argon2id$v=19$m={MemoryKiB},t={Iterations},p={Parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }
    public PasswordVerificationResult VerifyHashedPassword(TaskFlowUser user, string hashedPassword, string providedPassword)
    {
        try
        {
            var parts = hashedPassword.Split('$');
            if (parts.Length != 6 || parts[1] != "argon2id" || parts[2] != "v=19") return PasswordVerificationResult.Failed;
            var costs = parts[3].Split(',');
            if (costs.Length != 3 || !costs[0].StartsWith("m=") || !costs[1].StartsWith("t=") || !costs[2].StartsWith("p="))
                return PasswordVerificationResult.Failed;
            var memory = int.Parse(costs[0][2..]);
            var iterations = int.Parse(costs[1][2..]);
            var lanes = int.Parse(costs[2][2..]);
            if (memory is < 19456 or > 131072 || iterations is < 2 or > 6 || lanes is < 1 or > 4) return PasswordVerificationResult.Failed;
            var salt = Convert.FromBase64String(parts[4]);
            var expected = Convert.FromBase64String(parts[5]);
            if (salt.Length != 16 || expected.Length != 32 || providedPassword.Length > 1024) return PasswordVerificationResult.Failed;
            var actual = Derive(providedPassword, salt, memory, iterations, lanes);
            if (!CryptographicOperations.FixedTimeEquals(expected, actual)) return PasswordVerificationResult.Failed;
            return memory < MemoryKiB || iterations < Iterations || lanes < Parallelism
                ? PasswordVerificationResult.SuccessRehashNeeded : PasswordVerificationResult.Success;
        }
        catch (Exception error) when (error is FormatException or OverflowException or ArgumentException)
        { return PasswordVerificationResult.Failed; }
    }
    private static byte[] Derive(string password, byte[] salt, int memory, int iterations, int lanes)
    {
        if (password.Length > 1024) throw new ArgumentException("Password is too long.", nameof(password));
        var bytes = Encoding.UTF8.GetBytes(password);
        try
        {
            using var argon = new Argon2id(bytes) { Salt = salt, MemorySize = memory, Iterations = iterations, DegreeOfParallelism = lanes };
            return argon.GetBytes(32);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
