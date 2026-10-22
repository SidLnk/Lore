using System.Security.Cryptography;
using Dapper;
using Lore.Api.Data;
using Lore.Contracts;

namespace Lore.Api.Endpoints;

/// <summary>Admin page and its API share the /admin prefix so the browser reuses the Basic credentials.</summary>
public static class AdminEndpoints
{
    public static void Map(RouteGroupBuilder admin, IWebHostEnvironment env)
    {
        admin.MapGet("/", () => Results.File(Path.Combine(env.ContentRootPath, "admin.html"), "text/html"));

        admin.MapPost("/api/pairing-codes", async (Db db) =>
        {
            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
            using var c = db.Open();
            await c.ExecuteAsync("DELETE FROM PairingCode WHERE ExpiresAt < @now", new { now = DateTimeOffset.UtcNow });
            await c.ExecuteAsync("INSERT OR REPLACE INTO PairingCode(Code, ExpiresAt) VALUES (@code, @expiresAt)", new { code, expiresAt });
            return new PairingCodeDto(code, expiresAt);
        });

        admin.MapGet("/api/devices", async (Db db) =>
        {
            using var c = db.Open();
            return (await c.QueryAsync<DeviceRow>("SELECT * FROM Device ORDER BY PairedAt")).Select(d => d.ToDto());
        });

        admin.MapPost("/api/devices/{id:guid}/revoke", async (Guid id, Db db) =>
        {
            using var c = db.Open();
            var n = await c.ExecuteAsync("UPDATE Device SET RevokedAt = @now WHERE Id = @id AND RevokedAt IS NULL", new { id, now = DateTimeOffset.UtcNow });
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });
    }
}
