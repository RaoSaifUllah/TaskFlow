using Npgsql;

namespace TaskFlow.Infrastructure.Persistence;

public static class DatabaseConnections
{
    public static string Normalize(string configured)
    {
        try
        {
            NpgsqlConnectionStringBuilder builder;
            if (configured.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
                configured.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(configured);
                var credentials = uri.UserInfo.Split(':', 2);
                if (credentials.Length != 2 || string.IsNullOrEmpty(uri.Host) || uri.AbsolutePath.Length <= 1 || uri.Fragment.Length != 0)
                    throw new FormatException();
                builder = new NpgsqlConnectionStringBuilder
                {
                    Host = uri.Host,
                    Port = uri.Port > 0 ? uri.Port : 5432,
                    Database = Uri.UnescapeDataString(uri.AbsolutePath[1..]),
                    Username = Uri.UnescapeDataString(credentials[0]),
                    Password = Uri.UnescapeDataString(credentials[1]),
                    SslMode = SslMode.VerifyFull
                };
                foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var pieces = pair.Split('=', 2);
                    if (pieces.Length != 2) throw new FormatException();
                    var value = Uri.UnescapeDataString(pieces[1]);
                    switch (pieces[0].ToLowerInvariant())
                    {
                        case "sslmode":
                            if (value is not ("require" or "verify-full")) throw new FormatException();
                            // Neon URL 'require' is strengthened to certificate + hostname verification.
                            break;
                        case "channel_binding":
                            builder.ChannelBinding = Enum.Parse<ChannelBinding>(value, true);
                            break;
                        case "connect_timeout": builder.Timeout = int.Parse(value); break;
                        default: throw new FormatException();
                    }
                }
            }
            else builder = new NpgsqlConnectionStringBuilder(configured);
            builder.IncludeErrorDetail = false;
            builder.LogParameters = false;
            builder.Timeout = Math.Clamp(builder.Timeout, 1, 30);
            return builder.ConnectionString;
        }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException or KeyNotFoundException)
        {
            // Never retain a provider parsing exception: it can contain the entire credential.
            throw new InvalidOperationException("Invalid database connection configuration. Use an Npgsql connection string or supported PostgreSQL URL.");
        }
    }
}
