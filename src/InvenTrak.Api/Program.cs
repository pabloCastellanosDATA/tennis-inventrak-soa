using InvenTrak.Api.Data;
using InvenTrak.Api.Endpoints;
using InvenTrak.Api.Models;
using InvenTrak.Api.Seguridad;
using InvenTrak.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ---------------- Servicios ----------------
builder.Services.AddDbContext<InventarioDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Inventario") ?? "Data Source=inventrak.db"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<InventarioService>();
builder.Services.AddScoped<CatalogoService>();

// Los JSON mal formados lanzan excepción en cualquier entorno, para responder siempre con un ErrorDto.
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "InvenTrak Connect – Servicios de inventario de la tienda Tennis",
        Version = "v1",
        Description =
            "Prototipo académico (Arquitectura del Software – Politécnico Grancolombiano). " +
            "Expone el inventario como servicios REST con una única fuente autorizada del stock " +
            "y un registro central de servicios (Metadata Centralization). " +
            "Las operaciones POST/PUT requieren la cabecera X-Api-Key (clave de demostración: tennis-2026).",
    });
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = ApiKeyFilter.Cabecera,
        Description = "Clave de API del sistema consumidor. Demostración: tennis-2026",
    });
    c.OperationFilter<SoloEscrituraRequiereClave>();
});

var app = builder.Build();

// ---------------- Base de datos (se crea y se llena con datos de ejemplo al iniciar) ----------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<InventarioDbContext>();
    db.Database.EnsureCreated();
    DatosSemilla.Cargar(db, TimeProvider.System.GetUtcNow().UtcDateTime);
}

// ---------------- Errores de formato en la solicitud (JSON mal formado, tipos inválidos) ----------------
app.Use(async (ctx, next) =>
{
    try
    {
        await next(ctx);
    }
    catch (BadHttpRequestException ex)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsJsonAsync(new ErrorDto("SOLICITUD_INVALIDA",
            "El cuerpo de la solicitud no es un JSON válido o tiene tipos de dato incorrectos. " + ex.Message));
    }
});

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "InvenTrak Connect v1");
    c.DocumentTitle = "InvenTrak Connect – API";
});

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.MapGet("/health", () => Results.Ok(new { estado = "OK", servicio = "InvenTrak Connect", fecha = DateTime.UtcNow }))
    .WithTags("0. Salud")
    .WithSummary("Verifica que el servicio está en línea");

app.MapGroup("/api/v1")
    .MapInventarioEndpoints()
    .MapCatalogoEndpoints();

app.Run();

// Necesario para las pruebas de integración (WebApplicationFactory).
public partial class Program;
