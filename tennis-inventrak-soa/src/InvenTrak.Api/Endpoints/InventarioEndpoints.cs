using InvenTrak.Api.Models;
using InvenTrak.Api.Seguridad;
using InvenTrak.Api.Services;

namespace InvenTrak.Api.Endpoints;

/// <summary>Contrato REST de los servicios de inventario (Tabla 3 del documento).</summary>
public static class InventarioEndpoints
{
    public static RouteGroupBuilder MapInventarioEndpoints(this RouteGroupBuilder api)
    {
        // ---------------- ServicioExistencias ----------------
        var existencias = api.MapGroup("").WithTags("1. Existencias");

        existencias.MapGet("/productos", async (string? categoria, string? busqueda, InventarioService s) =>
                Results.Ok(await s.ListarProductosAsync(categoria, busqueda)))
            .WithName("ListarProductos")
            .WithSummary("Lista los productos de la tienda")
            .WithDescription("Filtros opcionales: categoria (Prendas, Calzado, Accesorios) y busqueda (texto en nombre o referencia).")
            .Produces<List<ProductoDto>>();

        existencias.MapGet("/existencias", async (string? referencia, string? talla, string? sede, InventarioService s) =>
                Results.Ok(await s.ConsultarExistenciasAsync(referencia, talla, sede)))
            .WithName("ConsultarExistencias")
            .WithSummary("Consulta el stock por referencia, talla y sede (fuente autorizada)")
            .WithDescription("Todos los filtros son opcionales. Devuelve unidades físicas, reservadas, disponibles y la fecha de la última actualización.")
            .Produces<List<ExistenciaDto>>();

        // ---------------- ServicioDisponibilidadEnLinea ----------------
        api.MapGet("/disponibilidad-en-linea/{referencia}", async (string referencia, InventarioService s) =>
                Responder(await s.DisponibilidadEnLineaAsync(referencia)))
            .WithTags("2. Disponibilidad en línea")
            .WithName("DisponibilidadEnLinea")
            .WithSummary("Unidades que la tienda en línea puede vender, por talla")
            .WithDescription("Se calcula desde la misma fuente que usa el POS: disponibles = físicas - reservadas, sumando todas las sedes.")
            .Produces<DisponibilidadEnLineaDto>()
            .Produces<ErrorDto>(StatusCodes.Status404NotFound);

        // ---------------- ServicioVentasPresenciales ----------------
        api.MapPost("/ventas", async (VentaRequest req, InventarioService s) =>
                Responder(await s.RegistrarVentaAsync(req), v => Results.Created($"/api/v1/movimientos?referencia={v.Referencia}", v)))
            .WithTags("3. Ventas presenciales")
            .WithName("RegistrarVenta")
            .WithSummary("Registra una venta y descuenta las unidades al instante")
            .WithDescription("Lo invoca el POS (a través de su adaptador) después de cada venta. Si no hay unidades suficientes responde 409.")
            .AddEndpointFilter<ApiKeyFilter>()
            .Produces<OperacionInventarioDto>(StatusCodes.Status201Created)
            .Produces<ErrorDto>(StatusCodes.Status400BadRequest)
            .Produces<ErrorDto>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorDto>(StatusCodes.Status404NotFound)
            .Produces<ErrorDto>(StatusCodes.Status409Conflict);

        // ---------------- ServicioReservasPedido ----------------
        var reservas = api.MapGroup("/reservas").WithTags("4. Reservas de pedido");

        reservas.MapPost("", async (ReservaRequest req, InventarioService s) =>
                Responder(await s.CrearReservaAsync(req), r => Results.Created($"/api/v1/reservas/{r.Id}", r)))
            .WithName("CrearReserva")
            .WithSummary("Aparta unidades mientras se completa un pedido en línea")
            .WithDescription("minutosVigencia es opcional (30 por defecto). Si la reserva no se confirma a tiempo, vence y las unidades vuelven a estar disponibles.")
            .AddEndpointFilter<ApiKeyFilter>()
            .Produces<ReservaDto>(StatusCodes.Status201Created)
            .Produces<ErrorDto>(StatusCodes.Status400BadRequest)
            .Produces<ErrorDto>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorDto>(StatusCodes.Status404NotFound)
            .Produces<ErrorDto>(StatusCodes.Status409Conflict);

        reservas.MapGet("/{id:int}", async (int id, InventarioService s) => Responder(await s.ObtenerReservaAsync(id)))
            .WithName("ObtenerReserva")
            .WithSummary("Consulta el estado de una reserva")
            .Produces<ReservaDto>()
            .Produces<ErrorDto>(StatusCodes.Status404NotFound);

        reservas.MapPost("/{id:int}/confirmar", async (int id, InventarioService s) => Responder(await s.ConfirmarReservaAsync(id)))
            .WithName("ConfirmarReserva")
            .WithSummary("Confirma el pedido: las unidades reservadas se descuentan como venta")
            .AddEndpointFilter<ApiKeyFilter>()
            .Produces<ReservaDto>()
            .Produces<ErrorDto>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorDto>(StatusCodes.Status404NotFound)
            .Produces<ErrorDto>(StatusCodes.Status409Conflict);

        reservas.MapPost("/{id:int}/liberar", async (int id, InventarioService s) => Responder(await s.LiberarReservaAsync(id)))
            .WithName("LiberarReserva")
            .WithSummary("Cancela la reserva y devuelve las unidades al stock disponible")
            .AddEndpointFilter<ApiKeyFilter>()
            .Produces<ReservaDto>()
            .Produces<ErrorDto>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorDto>(StatusCodes.Status404NotFound)
            .Produces<ErrorDto>(StatusCodes.Status409Conflict);

        // ---------------- ServicioEntradasMercancia ----------------
        api.MapPost("/entradas", async (EntradaRequest req, InventarioService s) =>
                Responder(await s.RegistrarEntradaAsync(req), v => Results.Created($"/api/v1/movimientos?referencia={v.Referencia}", v)))
            .WithTags("5. Entradas de mercancía")
            .WithName("RegistrarEntrada")
            .WithSummary("Registra mercancía recibida de un proveedor")
            .WithDescription("Reemplaza la actualización manual de la hoja de cálculo. Si la talla no existía en la sede, crea su registro de existencias.")
            .AddEndpointFilter<ApiKeyFilter>()
            .Produces<OperacionInventarioDto>(StatusCodes.Status201Created)
            .Produces<ErrorDto>(StatusCodes.Status400BadRequest)
            .Produces<ErrorDto>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorDto>(StatusCodes.Status404NotFound);

        // ---------------- ServicioAlertasReposicion ----------------
        api.MapGet("/alertas", async (string? sede, InventarioService s) => Results.Ok(await s.ConsultarAlertasAsync(sede)))
            .WithTags("6. Alertas de reposición")
            .WithName("ConsultarAlertas")
            .WithSummary("Referencias en o por debajo del stock mínimo")
            .WithDescription("unidadesSugeridas = (2 × stock mínimo) − disponibles.")
            .Produces<List<AlertaReposicionDto>>();

        // ---------------- Auditoría ----------------
        api.MapGet("/movimientos", async (string? referencia, string? tipo, int? limite, InventarioService s) =>
                Results.Ok(await s.ConsultarMovimientosAsync(referencia, tipo, limite ?? 50)))
            .WithTags("7. Auditoría")
            .WithName("ConsultarMovimientos")
            .WithSummary("Historial de movimientos de inventario (más recientes primero)")
            .WithDescription("tipo: VENTA, ENTRADA, RESERVA, CONFIRMACION_RESERVA, LIBERACION_RESERVA, VENCIMIENTO_RESERVA.")
            .Produces<List<MovimientoDto>>();

        return api;
    }

    public static IResult Responder<T>(Resultado<T> r, Func<T, IResult>? siExito = null) =>
        r.Exito
            ? siExito?.Invoke(r.Valor!) ?? Results.Ok(r.Valor)
            : Results.Json(r.Error, statusCode: r.Status);
}
