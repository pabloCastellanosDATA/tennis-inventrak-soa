namespace InvenTrak.Api.Models;

/// <summary>Producto del catálogo comercial de la tienda Tennis.</summary>
public class Producto
{
    public string Referencia { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Categoria { get; set; } = "";
}

/// <summary>
/// Existencias de una referencia en una talla y sede. Es la fuente autorizada del stock
/// (patrón Entity Abstraction): ningún canal guarda su propia copia.
/// </summary>
public class Existencia
{
    public int Id { get; set; }
    public string Referencia { get; set; } = "";
    public string Talla { get; set; } = "";
    public string Sede { get; set; } = "";
    public int UnidadesFisicas { get; set; }
    public int UnidadesReservadas { get; set; }
    public int StockMinimo { get; set; }
    public DateTime UltimaActualizacion { get; set; }

    /// <summary>Token de concurrencia optimista: evita que dos canales descuenten la misma unidad.</summary>
    public int VersionFila { get; set; }

    public int Disponibles => UnidadesFisicas - UnidadesReservadas;
}

/// <summary>Bitácora de todos los movimientos de inventario (auditoría, RF-06).</summary>
public class Movimiento
{
    public int Id { get; set; }
    public string Tipo { get; set; } = "";
    public string Referencia { get; set; } = "";
    public string Talla { get; set; } = "";
    public string Sede { get; set; } = "";
    public int Cantidad { get; set; }
    public string Canal { get; set; } = "";
    public string? Documento { get; set; }
    public int DisponiblesResultantes { get; set; }
    public DateTime Fecha { get; set; }
}

/// <summary>Reserva temporal de unidades mientras se completa un pedido en línea.</summary>
public class Reserva
{
    public int Id { get; set; }
    public string Pedido { get; set; } = "";
    public string Referencia { get; set; } = "";
    public string Talla { get; set; } = "";
    public string Sede { get; set; } = "";
    public int Cantidad { get; set; }
    public string Estado { get; set; } = EstadosReserva.Activa;
    public DateTime FechaCreacion { get; set; }
    public DateTime FechaVencimiento { get; set; }
}

/// <summary>
/// Entrada del registro central de servicios (patrón Metadata Centralization):
/// qué servicio existe, quién lo mantiene, en qué versión, con qué contrato y quién lo consume.
/// </summary>
public class ServicioRegistrado
{
    public string Nombre { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string Responsable { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Estado { get; set; } = EstadosServicio.Publicado;
    public List<string> Operaciones { get; set; } = new();
    public string Ubicacion { get; set; } = "";
    public List<string> Consumidores { get; set; } = new();
    public DateTime UltimaActualizacion { get; set; }
}

public static class TiposMovimiento
{
    public const string Venta = "VENTA";
    public const string Entrada = "ENTRADA";
    public const string Reserva = "RESERVA";
    public const string ConfirmacionReserva = "CONFIRMACION_RESERVA";
    public const string LiberacionReserva = "LIBERACION_RESERVA";
    public const string VencimientoReserva = "VENCIMIENTO_RESERVA";
}

public static class EstadosReserva
{
    public const string Activa = "ACTIVA";
    public const string Confirmada = "CONFIRMADA";
    public const string Liberada = "LIBERADA";
    public const string Vencida = "VENCIDA";
}

public static class EstadosServicio
{
    public const string EnDiseno = "EN_DISENO";
    public const string Publicado = "PUBLICADO";
    public const string Obsoleto = "OBSOLETO";

    public static readonly string[] Todos = { EnDiseno, Publicado, Obsoleto };
}
