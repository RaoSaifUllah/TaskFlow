using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace TaskFlow.Infrastructure.Identity;

public sealed class JwtIssuer : IDisposable
{
    private readonly RSA rsa;
    private readonly string issuer;
    private readonly string audience;
    private readonly List<RSA> validationRsa = [];
    private readonly List<RsaSecurityKey> validationKeys = [];
    public RsaSecurityKey Key { get; }
    public JwtIssuer(IConfiguration configuration)
    {
        var path = configuration["Auth:SigningKeyPath"] ?? throw new InvalidOperationException("Auth:SigningKeyPath is required.");
        rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(path));
        if (rsa.KeySize < 3072) throw new InvalidOperationException("RSA signing key must be at least 3072 bits.");
        Key = new RsaSecurityKey(rsa) { KeyId = Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()))[..16] };
        validationKeys.Add(Key);
        foreach (var publicPath in configuration.GetSection("Auth:ValidationKeyPaths").Get<string[]>() ?? [])
        {
            if (string.IsNullOrWhiteSpace(publicPath)) continue;
            var publicRsa = RSA.Create();
            publicRsa.ImportFromPem(File.ReadAllText(publicPath));
            if (publicRsa.KeySize < 3072) throw new InvalidOperationException("Validation key must be at least 3072 bits.");
            validationRsa.Add(publicRsa);
            validationKeys.Add(new RsaSecurityKey(publicRsa) { KeyId = Convert.ToHexString(SHA256.HashData(publicRsa.ExportSubjectPublicKeyInfo()))[..16] });
        }
        issuer = configuration["Auth:Issuer"] ?? "taskflow";
        audience = configuration["Auth:Audience"] ?? "taskflow.web";
    }
    public string Issue(TaskFlowUser user, RefreshSession session, DateTimeOffset now)
    {
        var token = new JwtSecurityToken(issuer, audience,
            [new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()), new Claim("sid", session.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())],
            now.UtcDateTime, now.AddMinutes(5).UtcDateTime, new SigningCredentials(Key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = issuer,
        ValidateAudience = true,
        ValidAudience = audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(15),
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKeys = validationKeys,
        TryAllIssuerSigningKeys = false,
        ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        NameClaimType = JwtRegisteredClaimNames.Sub
    };
    public void Dispose() { rsa.Dispose(); foreach (var publicRsa in validationRsa) publicRsa.Dispose(); }
}
