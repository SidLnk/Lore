using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Lore.Client;
using Lore.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace Lore.Api.Tests;

/// <summary>A real Lore server on throwaway DB + storage dirs.</summary>
public sealed class LoreFactory : WebApplicationFactory<Program>
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "lore-test-" + Guid.NewGuid().ToString("N"));
    public string MediaRoot => Path.Combine(Root, "media");

    protected override void ConfigureWebHost(IWebHostBuilder b)
    {
        b.UseEnvironment("Testing");
        b.UseSetting("Lore:DbPath", Path.Combine(Root, "db", "lore.db"));
        b.UseSetting("Lore:DataRoot", Root);
        b.UseSetting("Lore:AdminPassword", "test-admin");
        b.UseSetting("Storage:Volumes:0:Id", "hdd1");
        b.UseSetting("Storage:Volumes:0:Root", MediaRoot);
    }

    public HttpClient Admin()
    {
        var c = CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:test-admin")));
        return c;
    }

    public async Task<string> NewPairingCodeAsync()
    {
        var r = await Admin().PostAsync("/admin/api/pairing-codes", null);
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<PairingCodeDto>())!.Code;
    }

    /// <summary>Pairs a new device and returns a client for it. Optional handler wraps the in-memory server (fault injection).</summary>
    public async Task<LoreApiClient> PairAsync(string name = "Test phone", int defaultOwner = 1, Func<HttpMessageHandler, HttpMessageHandler>? wrap = null)
    {
        var inner = Server.CreateHandler();
        var http = new HttpClient(wrap?.Invoke(inner) ?? inner) { BaseAddress = Server.BaseAddress, Timeout = TimeSpan.FromMinutes(2) };
        var api = new LoreApiClient(http);
        var pair = await api.PairAsync(await NewPairingCodeAsync(), name);
        api.SetToken(pair.Token);
        await api.SetDefaultOwnerAsync(defaultOwner);
        return api;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
    }
}

/// <summary>Lets a test break specific requests: throw before sending, or after the server already handled it.</summary>
public sealed class FaultHandler(HttpMessageHandler inner, Func<HttpRequestMessage, int, Fault> decide) : DelegatingHandler(inner)
{
    int _calls;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var fault = decide(request, Interlocked.Increment(ref _calls));
        if (fault == Fault.DropBeforeSend) throw new HttpRequestException("simulated drop before send");
        if (fault == Fault.ServiceUnavailable) return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable) { Content = new StringContent("down") };
        var response = await base.SendAsync(request, ct);
        if (fault == Fault.DropAfterSend) throw new HttpRequestException("simulated drop after server handled request");
        return response;
    }
}

public enum Fault { None, DropBeforeSend, DropAfterSend, ServiceUnavailable }
