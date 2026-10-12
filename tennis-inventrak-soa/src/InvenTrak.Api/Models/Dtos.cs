namespace InvenTrak.Api.Models;

// ---------- Respuestas ----------

public record ProductoDto(string Referencia, string Nombre, string Categoria);

public record ExistenciaDto(
    string Referencia,
    string Producto,
    string Talla,
    string Sede,
    int UnidadesFisicas,
    int UnidadesReservadas,
    int Disponibles,
    int StockMinimo,
    DateTime UltimaActualizacion);

public record DisponibilidadTallaDto(string Talla, int Disponibles);

public record DisponibilidadEnLineaDto(
    string Referencia,
    string Producto,
    int TotalDisponible,
    IReadOnlyList<DisponibilidadTallaDto> Tallas,
    DateTime ConsultadoEn);

public record OperacionInventarioDto(
    int MovimientoId,
    string Tipo,
    string Referencia,
    string Talla,
    string Sede,
    int Cantidad,
    string Canal,
    string? Documento,
    ExistenciaDto StockResultante);

public record ReservaDto(
    int Id,
    string Pedido,
    string Referencia,
    string Talla,
    string Sede,
    int Cantidad,
    string Estado,
    DateTime FechaCreacion,
    DateTime FechaVencimiento);

public record AlertaReposicionDto(
    string Referencia,
    string Producto,
    string Talla,
    string Sede,
    int Disponibles,
    int StockMinimo,
    int UnidadesSugeridas);

public record MovimientoDto(
    int Id,
    string Tipo,
    string Referencia,
    string Talla,
    string Sede,
    int Cantidad,
    string Canal,
    string? Documento,
    int DisponiblesResultantes,
    DateTime Fecha);

public record ServicioDto(
    string Nombre,
    string Descripcion,
    string Responsable,
    string Version,
    string Estado,
    IReadOnlyList<string> Operaciones,
    string Ubicacion,
    IReadOnlyList<string> Consumidores,
    DateTime UltimaActualizacion);

public record ErrorDto(string Codigo, string Mensaje);

// ---------- Solicitudes ----------

public record VentaRequest(string Referencia, string Talla, string Sede, int Cantidad, string? Canal);

public record ReservaRequest(string Pedido, string Referencia, string Talla, string Sede, int Cantidad, int? MinutosVigencia);

public record EntradaRequest(string OrdenCompra, string Referencia, string Talla, string Sede, int Cantidad, int? StockMinimo);

public record ServicioRequest(
    string Nombre,
    string Descripcion,
    string Responsable,
    string Version,
    string? Estado,
    List<string> Operaciones,
    string Ubicacion,
    List<string> Consumidores);

public record ActualizarServicioRequest(
    string? Descripcion,
    string? Responsable,
    string? Version,
    string? Estado,
    List<string>? Operaciones,
    string? Ubicacion,
    List<string>? Consumidores);
