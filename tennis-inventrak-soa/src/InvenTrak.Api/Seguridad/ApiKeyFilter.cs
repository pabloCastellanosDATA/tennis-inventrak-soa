using InvenTrak.Api.Models;

namespace InvenTrak.Api.Seguridad;

/// <summary>
/// Autenticación sencilla por clave de API (RNF-03) para las operaciones que modifican datos.
/// Cada sistema consumidor (POS, tienda en línea, bodega) envía su clave en la cabecera X-Api-Key.
/// Las consultas (GET) quedan abiertas para facilitar la revisión del prototipo.
/// </summary>
public class ApiKeyFilter(IConfiguration config) : IEndpointFilter
{
    public const string Cabecera = "X-Api-Key";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var esperada = config["Seguridad:ApiKey"];
        var recibida = context.HttpContext.Request.Headers[Cabecera].ToString();

        if (string.IsNullOrEmpty(esperada) || recibida != esperada)
        {
            return Results.Json(
                new ErrorDto("NO_AUTORIZADO", $"Falta la cabecera {Cabecera} o la clave no es válida."),
                statusCode: StatusCodes.Status401Unauthorized);
        }
        return await next(context);
    }
}
