using System.Text.Json;
using Lore.Client;
using Lore.Contracts;

namespace Lore.App.Services;

/// <summary>Who this device is: server address, device token, default family member, cached family list.</summary>
public sealed class Session
{
	const string ServerKey = "server", TokenKey = "token", OwnerKey = "defaultOwner", FamilyKey = "family";

	public LoreApiClient? Api { get; private set; }
	public bool IsPaired => Api != null;
	public IReadOnlyList<FamilyMemberDto> Family { get; private set; } = [];

	public int DefaultOwnerId
	{
		get => Preferences.Get(OwnerKey, 0);
		private set => Preferences.Set(OwnerKey, value);
	}

	public string? MemberName(int id) => Family.FirstOrDefault(f => f.Id == id)?.Name;

	public async Task RestoreAsync()
	{
		var server = Preferences.Get(ServerKey, null);
		var token = await SecureStorage.GetAsync(TokenKey);
		if (server == null || token == null) return;
		Api = Create(server);
		Api.SetToken(token);
		Family = JsonSerializer.Deserialize<List<FamilyMemberDto>>(Preferences.Get(FamilyKey, "[]")) ?? [];
	}

	/// <summary>Pairs with a code from the server's admin page. Throws on a bad code or unreachable server.</summary>
	public async Task PairAsync(string server, string code, string deviceName)
	{
		server = NormalizeServer(server);
		var api = Create(server);
		var pair = await api.PairAsync(code.Trim(), deviceName.Trim());
		api.SetToken(pair.Token);
		var family = await api.GetFamilyAsync();

		Preferences.Set(ServerKey, server);
		await SecureStorage.SetAsync(TokenKey, pair.Token);
		Preferences.Set(FamilyKey, JsonSerializer.Serialize(family));
		Api = api;
		Family = family;
	}

	public async Task SetDefaultOwnerAsync(int ownerId)
	{
		await Api!.SetDefaultOwnerAsync(ownerId);
		DefaultOwnerId = ownerId;
	}

	public void Unpair()
	{
		SecureStorage.Remove(TokenKey);
		Preferences.Remove(OwnerKey);
		Api = null;
	}

	/// <summary>"lore.home.arpa" or "192.168.1.10" become "http://.../" (V1 is LAN HTTP).</summary>
	public static string NormalizeServer(string server)
	{
		server = server.Trim();
		if (!server.Contains("://")) server = "http://" + server;
		return server.TrimEnd('/') + "/";
	}

	static LoreApiClient Create(string server) =>
		new(new HttpClient { BaseAddress = new Uri(server), Timeout = TimeSpan.FromMinutes(10) });
}
