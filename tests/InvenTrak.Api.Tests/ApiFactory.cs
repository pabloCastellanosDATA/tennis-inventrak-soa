using System.Net.Http.Json;
using InvenTrak.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InvenTrak.Api.Tests;

/// <summary>Reloj controlable para probar el vencimiento de reservas sin esperar en tiempo real.</summary>
public class RelojDePrueba : TimeProvider
{
    public DateTimeOffset Ahora { get; private set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Ahora;
    public void Avanzar(TimeSpan tiempo) => Ahora += tiempo;
}

/// <summary>Levanta la API completa en memoria con una base SQLite temporal y aislada por prueba.</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _rutaDb = Path.Combine(Path.GetTempPath(), $"inventrak-test-{Guid.NewGuid():N}.db");
    public RelojDePrueba Reloj { get; } = new();
    public const string ApiKey = "tennis-2026";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<InventarioDbContext>>();
            services.AddDbContext<InventarioDbContext>(o => o.UseSqlite($"Data Source={_rutaDb}"));
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Reloj);
        });
    }

    public HttpClient ClienteConClave()
    {
        var c = CreateClient();
        c.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        return c;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        if (File.Exists(_rutaDb)) File.Delete(_rutaDb);
    }
}

public static class HttpExtensiones
{
    public static async Task<T> LeerAsync<T>(this HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<T>())!;
}
