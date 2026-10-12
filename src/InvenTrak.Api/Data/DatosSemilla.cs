using InvenTrak.Api.Models;

namespace InvenTrak.Api.Data;

/// <summary>
/// Datos de ejemplo de la tienda Tennis. Reemplazan la carga manual desde la hoja de cálculo:
/// en el escenario real, esta carga inicial se haría una sola vez (Figura A1 del documento).
/// </summary>
public static class DatosSemilla
{
    public const string TiendaCentro = "TIENDA-CENTRO";
    public const string TiendaNorte = "TIENDA-NORTE";
    public const string BodegaCentral = "BODEGA-CENTRAL";

    public static void Cargar(InventarioDbContext db, DateTime ahora)
    {
        if (db.Productos.Any()) return;

        var productos = new List<Producto>
        {
            new() { Referencia = "SWT-001", Nombre = "Sweater tejido cuello redondo", Categoria = "Prendas" },
            new() { Referencia = "JEA-014", Nombre = "Jean slim fit azul", Categoria = "Prendas" },
            new() { Referencia = "CHQ-007", Nombre = "Chaqueta en denim", Categoria = "Prendas" },
            new() { Referencia = "CAL-021", Nombre = "Tenis urbanos blancos", Categoria = "Calzado" },
            new() { Referencia = "CAL-033", Nombre = "Botas de cuero café", Categoria = "Calzado" },
            new() { Referencia = "ACC-005", Nombre = "Gorra bordada", Categoria = "Accesorios" },
            new() { Referencia = "ACC-012", Nombre = "Morral urbano", Categoria = "Accesorios" },
        };
        db.Productos.AddRange(productos);

        var tallasPorCategoria = new Dictionary<string, string[]>
        {
            ["Prendas"] = new[] { "S", "M", "L", "XL" },
            ["Calzado"] = new[] { "38", "39", "40", "41", "42" },
            ["Accesorios"] = new[] { "UNICA" },
        };
        var sedes = new[] { TiendaCentro, TiendaNorte, BodegaCentral };

        var semilla = 7;
        foreach (var p in productos)
        {
            foreach (var talla in tallasPorCategoria[p.Categoria])
            {
                foreach (var sede in sedes)
                {
                    // Cantidades deterministas (no aleatorias) para que las pruebas sean repetibles.
                    semilla = (semilla * 31 + 17) % 97;
                    var esBodega = sede == BodegaCentral;
                    db.Existencias.Add(new Existencia
                    {
                        Referencia = p.Referencia,
                        Talla = talla,
                        Sede = sede,
                        UnidadesFisicas = esBodega ? 10 + semilla % 20 : 3 + semilla % 9,
                        StockMinimo = esBodega ? 8 : 3,
                        UltimaActualizacion = ahora,
                    });
                }
            }
        }

        // Casos puntuales del documento: tallas de calzado y chaquetas con poco stock.
        db.SaveChanges();
        AjustarStock(db, "CAL-021", "40", TiendaNorte, 1, ahora);
        AjustarStock(db, "CHQ-007", "M", TiendaCentro, 2, ahora);
        AjustarStock(db, "SWT-001", "L", TiendaNorte, 0, ahora);

        db.Servicios.AddRange(CatalogoInicial(ahora));
        db.SaveChanges();
    }

    private static void AjustarStock(InventarioDbContext db, string referencia, string talla, string sede, int unidades, DateTime ahora)
    {
        var e = db.Existencias.Single(x => x.Referencia == referencia && x.Talla == talla && x.Sede == sede);
        e.UnidadesFisicas = unidades;
        e.UltimaActualizacion = ahora;
    }

    /// <summary>Registro central de servicios (Tablas 3, 4 y 5 del documento).</summary>
    public static IEnumerable<ServicioRegistrado> CatalogoInicial(DateTime ahora) => new[]
    {
        new ServicioRegistrado
        {
            Nombre = "ServicioExistencias",
            Descripcion = "Consulta del stock por referencia, talla y sede. Fuente autorizada del inventario.",
            Responsable = "InvenTrak",
            Operaciones = new() { "GET /api/v1/existencias", "GET /api/v1/productos" },
            Ubicacion = "/api/v1/existencias",
            Consumidores = new() { "POS", "Tienda en línea", "Bodega", "Gerencia" },
            UltimaActualizacion = ahora,
        },
        new ServicioRegistrado
        {
            Nombre = "ServicioVentasPresenciales",
            Descripcion = "Descuenta unidades del inventario después de cada venta en la tienda física.",
            Responsable = "POS (adaptador Legacy Wrapper)",
            Operaciones = new() { "POST /api/v1/ventas" },
            Ubicacion = "/api/v1/ventas",
            Consumidores = new() { "POS" },
            UltimaActualizacion = ahora,
        },
        new ServicioRegistrado
        {
            Nombre = "ServicioDisponibilidadEnLinea",
            Descripcion = "Publica las unidades que pueden venderse por el canal digital, calculadas desde la fuente autorizada.",
            Responsable = "Plataforma de comercio electrónico",
            Operaciones = new() { "GET /api/v1/disponibilidad-en-linea/{referencia}" },
            Ubicacion = "/api/v1/disponibilidad-en-linea",
            Consumidores = new() { "Tienda en línea", "Clientes" },
            UltimaActualizacion = ahora,
        },
        new ServicioRegistrado
        {
            Nombre = "ServicioReservasPedido",
            Descripcion = "Aparta unidades mientras se procesa un pedido en línea; la reserva vence si no se confirma.",
            Responsable = "Plataforma de comercio electrónico",
            Operaciones = new()
            {
                "POST /api/v1/reservas",
                "GET /api/v1/reservas/{id}",
                "POST /api/v1/reservas/{id}/confirmar",
                "POST /api/v1/reservas/{id}/liberar",
            },
            Ubicacion = "/api/v1/reservas",
            Consumidores = new() { "Tienda en línea" },
            UltimaActualizacion = ahora,
        },
        new ServicioRegistrado
        {
            Nombre = "ServicioEntradasMercancia",
            Descripcion = "Registra la mercancía recibida de los proveedores y actualiza el stock al instante.",
            Responsable = "InvenTrak",
            Operaciones = new() { "POST /api/v1/entradas" },
            Ubicacion = "/api/v1/entradas",
            Consumidores = new() { "Bodega", "Módulo de pedidos a proveedores" },
            UltimaActualizacion = ahora,
        },
        new ServicioRegistrado
        {
            Nombre = "ServicioAlertasReposicion",
            Descripcion = "Lista las referencias que están en o por debajo del stock mínimo.",
            Responsable = "InvenTrak",
            Operaciones = new() { "GET /api/v1/alertas" },
            Ubicacion = "/api/v1/alertas",
            Consumidores = new() { "Gerencia", "Módulo de pedidos a proveedores" },
            UltimaActualizacion = ahora,
        },
        new ServicioRegistrado
        {
            Nombre = "RegistroCentralServicios",
            Descripcion = "Catálogo de servicios y sus metadatos (patrón Metadata Centralization).",
            Responsable = "Equipo de arquitectura de Tennis",
            Operaciones = new()
            {
                "GET /api/v1/catalogo",
                "GET /api/v1/catalogo/{nombre}",
                "POST /api/v1/catalogo",
                "PUT /api/v1/catalogo/{nombre}",
            },
            Ubicacion = "/api/v1/catalogo",
            Consumidores = new() { "Equipos de TI", "Integradores", "Nuevos canales de venta" },
            UltimaActualizacion = ahora,
        },
    };
}
