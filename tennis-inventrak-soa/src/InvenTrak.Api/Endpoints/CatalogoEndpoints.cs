using InvenTrak.Api.Models;
using InvenTrak.Api.Seguridad;
using InvenTrak.Api.Services;
using static InvenTrak.Api.Endpoints.InventarioEndpoints;

namespace InvenTrak.Api.Endpoints;

/// <summary>Contrato REST del registro central de servicios (patrón Metadata Centralization).</summary>
public static class CatalogoEndpoints
{
    public static RouteGroupBuilder MapCatalogoEndpoints(this RouteGroupBuilder api)
    {
        var catalogo = api.MapGroup("/catalogo").WithTags("8. Registro central de servicios");

        catalogo.MapGet("", async (string? estado, string? consumidor, CatalogoService s) =>
                Results.Ok(await s.ListarAsync(estado, consumidor)))
            .WithName("ListarServicios")
            .WithSummary("Lista los servicios publicados y sus metadatos")
            .WithDescription("Filtros opcionales: estado (EN_DISENO, PUBLICADO, OBSOLETO) y consumidor (ej.: POS).")
            .Produces<List<ServicioDto>>();

        catalogo.MapGet("/{nombre}", async (string nombre, CatalogoService s) => Responder(await s.ObtenerAsync(nombre)))
            .WithName("ObtenerServicio")
            .WithSummary("Consulta el contrato, responsable y versión de un servicio")
            .Produces<ServicioDto>()
            .Produces<ErrorDto>(StatusCodes.Status404NotFound);

        catalogo.MapPost("", async (ServicioRequest req, CatalogoService s) =>
                Responder(await s.RegistrarAsync(req), v => Results.Created($"/api/v1/catalogo/{v.Nombre}", v)))
            .WithName("RegistrarServicio")
            .WithSummary("Publica un servicio nuevo en el catálogo")
            .WithDescription("Si ya existe un servicio con ese nombre responde 409, para promover la reutilización en lugar de duplicar.")
            .AddEndpointFilter<ApiKeyFilter>()
            .Produces<ServicioDto>(StatusCodes.Status201Created)
            .Produces<ErrorDto>(StatusCodes.Status400BadRequest)
            .Produces<ErrorDto>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorDto>(StatusCodes.Status409Conflict);

        catalogo.MapPut("/{nombre}", async (string nombre, ActualizarServicioRequest req, CatalogoService s) =>
                Responder(await s.ActualizarAsync(nombre, req)))
            .WithName("ActualizarServicio")
            .WithSummary("Actualiza versión, estado, contrato, ubicación o consumidores de un servicio")
            .WithDescription("Solo se modifican los campos enviados.")
            .AddEndpointFilter<ApiKeyFilter>()
            .Produces<ServicioDto>()
            .Produces<ErrorDto>(StatusCodes.Status400BadRequest)
            .Produces<ErrorDto>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorDto>(StatusCodes.Status404NotFound);

        return api;
    }
}
