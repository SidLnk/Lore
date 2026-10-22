using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Dapper;
using Lore.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Lore.Api;

public static class Tokens
{
    public static string New() => Base64Url(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static Guid DeviceId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

/// <summary>Paired device: "Authorization: Bearer token", or ?access_token= on media GETs (Image/video URLs can't send headers).</summary>
public sealed class DeviceAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, Db db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Device";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        string? token = header.StartsWith("Bearer ", StringComparison.Ordinal) ? header[7..]
            : HttpMethods.IsGet(Request.Method) && Request.Path.StartsWithSegments("/api/media") ? Request.Query["access_token"].ToString()
            : null;
        if (string.IsNullOrEmpty(token)) return AuthenticateResult.NoResult();

        using var c = db.Open();
        var id = await c.QuerySingleOrDefaultAsync<Guid?>(
            "SELECT Id FROM Device WHERE TokenHash = @h AND RevokedAt IS NULL", new { h = Tokens.Hash(token) });
        if (id == null) return AuthenticateResult.Fail("Unknown or revoked device");

        var now = DateTimeOffset.UtcNow;
        await c.ExecuteAsync("UPDATE Device SET LastSeenAt = @now WHERE Id = @id AND (LastSeenAt IS NULL OR LastSeenAt < @cutoff)",
            new { now, id, cutoff = now.AddMinutes(-5) });

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.Value.ToString())], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}

/// <summary>Admin page: HTTP Basic with the configured admin password (any username).</summary>
public sealed class AdminAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IConfiguration config)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Admin";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var expected = config["Lore:AdminPassword"];
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(expected) || !header.StartsWith("Basic ", StringComparison.Ordinal))
            return Task.FromResult(AuthenticateResult.NoResult());

        string decoded;
        try { decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[6..])); }
        catch (FormatException) { return Task.FromResult(AuthenticateResult.Fail("Bad header")); }

        var password = decoded[(decoded.IndexOf(':') + 1)..];
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(password)), SHA256.HashData(Encoding.UTF8.GetBytes(expected))))
            return Task.FromResult(AuthenticateResult.Fail("Wrong password"));

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.Headers.WWWAuthenticate = "Basic realm=\"Lore admin\", charset=\"UTF-8\"";
        return Task.CompletedTask;
    }
}
