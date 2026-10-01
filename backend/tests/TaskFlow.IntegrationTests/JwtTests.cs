using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.IntegrationTests;

public sealed class JwtTests
{
    [Fact]
    public void JwtRejectsWrongIssuerAudienceExpiryAlgorithmAndSupportsExplicitKeyRotation()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, ".local", "jwt-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var first = Path.Combine(directory, "first.pem");
        var second = Path.Combine(directory, "second.pem");
        var oldPublic = Path.Combine(directory, "first-public.pem");
        using var rsa1 = RSA.Create(3072);
        using var rsa2 = RSA.Create(3072);
        File.WriteAllText(first, rsa1.ExportRSAPrivateKeyPem());
        File.WriteAllText(second, rsa2.ExportRSAPrivateKeyPem());
        File.WriteAllText(oldPublic, rsa1.ExportSubjectPublicKeyInfoPem());
        try
        {
            IConfiguration Configuration(string path, string? validation = null) => new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Auth:SigningKeyPath"] = path, ["Auth:Issuer"] = "taskflow-test", ["Auth:Audience"] = "taskflow-web-test", ["Auth:ValidationKeyPaths:0"] = validation }).Build();
            using var old = new JwtIssuer(Configuration(first));
            using var rotated = new JwtIssuer(Configuration(second, oldPublic));
            var user = new TaskFlowUser { Id = Guid.NewGuid() };
            var session = new RefreshSession { Id = Guid.NewGuid() };
            var handler = new JwtSecurityTokenHandler();
            var token = old.Issue(user, session, DateTimeOffset.UtcNow);
            handler.ValidateToken(token, old.ValidationParameters(), out _);
            handler.ValidateToken(token, rotated.ValidationParameters(), out _);
            using var withoutOld = new JwtIssuer(Configuration(second));
            Assert.ThrowsAny<SecurityTokenException>(() => handler.ValidateToken(token, withoutOld.ValidationParameters(), out _));
            var wrongIssuer = old.ValidationParameters(); wrongIssuer.ValidIssuer = "wrong";
            Assert.Throws<SecurityTokenInvalidIssuerException>(() => handler.ValidateToken(token, wrongIssuer, out _));
            var wrongAudience = old.ValidationParameters(); wrongAudience.ValidAudience = "wrong";
            Assert.Throws<SecurityTokenInvalidAudienceException>(() => handler.ValidateToken(token, wrongAudience, out _));
            var expired = old.Issue(user, session, DateTimeOffset.UtcNow.AddMinutes(-10));
            Assert.Throws<SecurityTokenExpiredException>(() => handler.ValidateToken(expired, old.ValidationParameters(), out _));
            var hmac = new JwtSecurityToken("taskflow-test", "taskflow-web-test", expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)), SecurityAlgorithms.HmacSha256));
            Assert.ThrowsAny<SecurityTokenException>(() => handler.ValidateToken(handler.WriteToken(hmac), old.ValidationParameters(), out _));
        }
        finally { File.Delete(first); File.Delete(second); File.Delete(oldPublic); Directory.Delete(directory); }
    }
}
