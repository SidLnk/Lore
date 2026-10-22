using System.Security.Claims;
using Dapper;
using Lore.Api.Data;
using Lore.Contracts;

namespace Lore.Api.Endpoints;

public static class DeviceEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/pair", async (PairRequest r, Db db) =>
        {
            var name = r.DeviceName?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 100) return Results.BadRequest("Device name required");

            using var c = db.Open();
            using var tx = c.BeginTransaction();
            var now = DateTimeOffset.UtcNow;
            var expires = await c.QuerySingleOrDefaultAsync<DateTimeOffset?>(
                "SELECT ExpiresAt FROM PairingCode WHERE Code = @Code AND UsedAt IS NULL", new { r.Code }, tx);
            if (expires == null || expires < now) return Results.Problem("Invalid or expired pairing code", statusCode: 403);

            var token = Tokens.New();
            var id = Guid.CreateVersion7();
            await c.ExecuteAsync("UPDATE PairingCode SET UsedAt = @now WHERE Code = @Code", new { now, r.Code }, tx);
            await c.ExecuteAsync("INSERT INTO Device(Id, Name, TokenHash, PairedAt) VALUES (@id, @name, @hash, @now)",
                new { id, name, hash = Tokens.Hash(token), now }, tx);
            tx.Commit();
            return Results.Ok(new PairResponse(id, token));
        }).AllowAnonymous().RequireRateLimiting("pair");

        api.MapGet("/family", async (Db db) =>
        {
            using var c = db.Open();
            return (await c.QueryAsync<FamilyMemberRow>("SELECT Id, Name FROM FamilyMember ORDER BY Id")).Select(f => f.ToDto());
        });

        api.MapGet("/device", async (ClaimsPrincipal user, Db db) =>
        {
            using var c = db.Open();
            return (await c.QuerySingleAsync<DeviceRow>("SELECT * FROM Device WHERE Id = @id", new { id = user.DeviceId() })).ToDto();
        });

        api.MapPut("/device", async (UpdateDeviceRequest r, ClaimsPrincipal user, Db db) =>
        {
            using var c = db.Open();
            if (!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM FamilyMember WHERE Id = @DefaultOwnerId)", r))
                return Results.BadRequest("Unknown family member");
            await c.ExecuteAsync("UPDATE Device SET DefaultOwnerId = @DefaultOwnerId WHERE Id = @id", new { r.DefaultOwnerId, id = user.DeviceId() });
            return Results.NoContent();
        });
    }
}
