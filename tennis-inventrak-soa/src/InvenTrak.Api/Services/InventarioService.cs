using InvenTrak.Api.Data;
using InvenTrak.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace InvenTrak.Api.Services;

/// <summary>
/// Implementación de los servicios de inventario. Toda la lógica que modifica el stock vive aquí,
/// en un solo lugar (patrón Logic Centralization); los canales solo conocen el contrato REST.
/// </summary>
public class InventarioService(InventarioDbContext db, TimeProvider reloj)
{
    public const int MinutosVigenciaPorDefecto = 30;
    private DateTime Ahora => reloj.GetUtcNow().UtcDateTime;

    // ------------------------------------------------------------------ Consultas

    public async Task<List<ProductoDto>> ListarProductosAsync(string? categoria, string? busqueda)
    {
        var q = db.Productos.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(categoria))
            q = q.Where(p => p.Categoria.ToLower() == categoria.Trim().ToLower());
        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            var t = busqueda.Trim().ToLower();
            q = q.Where(p => p.Nombre.ToLower().Contains(t) || p.Referencia.ToLower().Contains(t));
        }
        return await q.OrderBy(p => p.Referencia)
            .Select(p => new ProductoDto(p.Referencia, p.Nombre, p.Categoria))
            .ToListAsync();
    }

    public async Task<List<ExistenciaDto>> ConsultarExistenciasAsync(string? referencia, string? talla, string? sede)
    {
        await VencerReservasAsync();
        var q = ConsultaExistenciasConProducto();
        if (!string.IsNullOrWhiteSpace(referencia)) { var r = Normalizar(referencia); q = q.Where(x => x.E.Referencia == r); }
        if (!string.IsNullOrWhiteSpace(talla)) { var t = Normalizar(talla); q = q.Where(x => x.E.Talla == t); }
        if (!string.IsNullOrWhiteSpace(sede)) { var s = Normalizar(sede); q = q.Where(x => x.E.Sede == s); }

        var filas = await q.OrderBy(x => x.E.Referencia).ThenBy(x => x.E.Sede).ThenBy(x => x.E.Id).ToListAsync();
        return filas.Select(x => ADto(x.E, x.Nombre)).ToList();
    }

    public async Task<Resultado<DisponibilidadEnLineaDto>> DisponibilidadEnLineaAsync(string referencia)
    {
        await VencerReservasAsync();
        var r = Normalizar(referencia);
        var producto = await db.Productos.AsNoTracking().FirstOrDefaultAsync(p => p.Referencia == r);
        if (producto is null)
            return Resultado<DisponibilidadEnLineaDto>.NoEncontrado($"No existe la referencia {r}.");

        var existencias = await db.Existencias.AsNoTracking()
            .Where(e => e.Referencia == r).OrderBy(e => e.Id).ToListAsync();

        var tallas = existencias
            .GroupBy(e => e.Talla)
            .Select(g => new DisponibilidadTallaDto(g.Key, g.Sum(e => Math.Max(0, e.Disponibles))))
            .ToList();

        return Resultado<DisponibilidadEnLineaDto>.Ok(new DisponibilidadEnLineaDto(
            producto.Referencia, producto.Nombre, tallas.Sum(t => t.Disponibles), tallas, Ahora));
    }

    public async Task<List<AlertaReposicionDto>> ConsultarAlertasAsync(string? sede)
    {
        await VencerReservasAsync();
        var q = ConsultaExistenciasConProducto();
        if (!string.IsNullOrWhiteSpace(sede)) { var s = Normalizar(sede); q = q.Where(x => x.E.Sede == s); }

        var filas = await q
            .Where(x => x.E.UnidadesFisicas - x.E.UnidadesReservadas <= x.E.StockMinimo)
            .OrderBy(x => x.E.Sede).ThenBy(x => x.E.Referencia).ThenBy(x => x.E.Id)
            .ToListAsync();

        return filas.Select(x => new AlertaReposicionDto(
            x.E.Referencia, x.Nombre, x.E.Talla, x.E.Sede, x.E.Disponibles, x.E.StockMinimo,
            // Sugerencia simple: reponer hasta el doble del stock mínimo.
            x.E.StockMinimo * 2 - x.E.Disponibles)).ToList();
    }

    public async Task<List<MovimientoDto>> ConsultarMovimientosAsync(string? referencia, string? tipo, int limite)
    {
        var q = db.Movimientos.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(referencia)) { var r = Normalizar(referencia); q = q.Where(m => m.Referencia == r); }
        if (!string.IsNullOrWhiteSpace(tipo)) { var t = Normalizar(tipo); q = q.Where(m => m.Tipo == t); }

        return await q.OrderByDescending(m => m.Id).Take(Math.Clamp(limite, 1, 500))
            .Select(m => new MovimientoDto(m.Id, m.Tipo, m.Referencia, m.Talla, m.Sede, m.Cantidad,
                m.Canal, m.Documento, m.DisponiblesResultantes, m.Fecha))
            .ToListAsync();
    }

    public async Task<Resultado<ReservaDto>> ObtenerReservaAsync(int id)
    {
        await VencerReservasAsync();
        var reserva = await db.Reservas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return reserva is null
            ? Resultado<ReservaDto>.NoEncontrado($"No existe la reserva {id}.")
            : Resultado<ReservaDto>.Ok(ADto(reserva));
    }

    // ------------------------------------------------------------------ Operaciones

    public async Task<Resultado<OperacionInventarioDto>> RegistrarVentaAsync(VentaRequest req)
    {
        var error = ValidarCampos(("referencia", req.Referencia), ("talla", req.Talla), ("sede", req.Sede))
                    ?? ValidarCantidad(req.Cantidad);
        if (error is not null) return Resultado<OperacionInventarioDto>.Invalido(error);

        await VencerReservasAsync();
        var existencia = await BuscarExistenciaAsync(req.Referencia, req.Talla, req.Sede);
        if (existencia is null)
            return Resultado<OperacionInventarioDto>.NoEncontrado(NoExiste(req.Referencia, req.Talla, req.Sede));

        if (existencia.Disponibles < req.Cantidad)
            return Resultado<OperacionInventarioDto>.Conflicto("STOCK_INSUFICIENTE",
                $"Solo hay {existencia.Disponibles} unidad(es) disponibles de {existencia.Referencia} talla {existencia.Talla} en {existencia.Sede}.");

        existencia.UnidadesFisicas -= req.Cantidad;
        var canal = string.IsNullOrWhiteSpace(req.Canal) ? "POS" : Normalizar(req.Canal);
        var mov = RegistrarMovimiento(TiposMovimiento.Venta, existencia, req.Cantidad, canal, null);

        return await GuardarOperacionAsync(existencia, mov);
    }

    public async Task<Resultado<OperacionInventarioDto>> RegistrarEntradaAsync(EntradaRequest req)
    {
        var error = ValidarCampos(("ordenCompra", req.OrdenCompra), ("referencia", req.Referencia),
                        ("talla", req.Talla), ("sede", req.Sede))
                    ?? ValidarCantidad(req.Cantidad)
                    ?? (req.StockMinimo is < 0 ? "El stock mínimo no puede ser negativo." : null);
        if (error is not null) return Resultado<OperacionInventarioDto>.Invalido(error);

        var r = Normalizar(req.Referencia);
        if (!await db.Productos.AnyAsync(p => p.Referencia == r))
            return Resultado<OperacionInventarioDto>.NoEncontrado($"No existe la referencia {r}.");

        var existencia = await BuscarExistenciaAsync(req.Referencia, req.Talla, req.Sede);
        if (existencia is null)
        {
            // Primera vez que llega esta talla a esta sede: se crea el registro de existencias.
            existencia = new Existencia
            {
                Referencia = r,
                Talla = Normalizar(req.Talla),
                Sede = Normalizar(req.Sede),
                StockMinimo = req.StockMinimo ?? 3,
            };
            db.Existencias.Add(existencia);
        }
        else if (req.StockMinimo is not null)
        {
            existencia.StockMinimo = req.StockMinimo.Value;
        }

        existencia.UnidadesFisicas += req.Cantidad;
        var mov = RegistrarMovimiento(TiposMovimiento.Entrada, existencia, req.Cantidad, "PROVEEDORES", req.OrdenCompra.Trim());

        return await GuardarOperacionAsync(existencia, mov);
    }

    public async Task<Resultado<ReservaDto>> CrearReservaAsync(ReservaRequest req)
    {
        var error = ValidarCampos(("pedido", req.Pedido), ("referencia", req.Referencia), ("talla", req.Talla), ("sede", req.Sede))
                    ?? ValidarCantidad(req.Cantidad)
                    ?? (req.MinutosVigencia is < 1 or > 1440 ? "minutosVigencia debe estar entre 1 y 1440." : null);
        if (error is not null) return Resultado<ReservaDto>.Invalido(error);

        await VencerReservasAsync();
        var existencia = await BuscarExistenciaAsync(req.Referencia, req.Talla, req.Sede);
        if (existencia is null)
            return Resultado<ReservaDto>.NoEncontrado(NoExiste(req.Referencia, req.Talla, req.Sede));

        if (existencia.Disponibles < req.Cantidad)
            return Resultado<ReservaDto>.Conflicto("STOCK_INSUFICIENTE",
                $"No se puede reservar: solo hay {existencia.Disponibles} unidad(es) disponibles.");

        existencia.UnidadesReservadas += req.Cantidad;
        var reserva = new Reserva
        {
            Pedido = req.Pedido.Trim(),
            Referencia = existencia.Referencia,
            Talla = existencia.Talla,
            Sede = existencia.Sede,
            Cantidad = req.Cantidad,
            Estado = EstadosReserva.Activa,
            FechaCreacion = Ahora,
            FechaVencimiento = Ahora.AddMinutes(req.MinutosVigencia ?? MinutosVigenciaPorDefecto),
        };
        db.Reservas.Add(reserva);
        RegistrarMovimiento(TiposMovimiento.Reserva, existencia, req.Cantidad, "TIENDA_EN_LINEA", reserva.Pedido);

        var guardado = await GuardarAsync();
        return guardado ?? Resultado<ReservaDto>.Ok(ADto(reserva));
    }

    public Task<Resultado<ReservaDto>> ConfirmarReservaAsync(int id) =>
        CerrarReservaAsync(id, EstadosReserva.Confirmada, TiposMovimiento.ConfirmacionReserva);

    public Task<Resultado<ReservaDto>> LiberarReservaAsync(int id) =>
        CerrarReservaAsync(id, EstadosReserva.Liberada, TiposMovimiento.LiberacionReserva);

    private async Task<Resultado<ReservaDto>> CerrarReservaAsync(int id, string nuevoEstado, string tipoMovimiento)
    {
        await VencerReservasAsync();
        var reserva = await db.Reservas.FirstOrDefaultAsync(x => x.Id == id);
        if (reserva is null) return Resultado<ReservaDto>.NoEncontrado($"No existe la reserva {id}.");
        if (reserva.Estado != EstadosReserva.Activa)
            return Resultado<ReservaDto>.Conflicto("RESERVA_NO_ACTIVA",
                $"La reserva {id} está en estado {reserva.Estado} y ya no se puede modificar.");

        var existencia = (await BuscarExistenciaAsync(reserva.Referencia, reserva.Talla, reserva.Sede))!;
        existencia.UnidadesReservadas -= reserva.Cantidad;
        if (nuevoEstado == EstadosReserva.Confirmada)
            existencia.UnidadesFisicas -= reserva.Cantidad; // la venta en línea se concreta

        reserva.Estado = nuevoEstado;
        RegistrarMovimiento(tipoMovimiento, existencia, reserva.Cantidad, "TIENDA_EN_LINEA", reserva.Pedido);

        var guardado = await GuardarAsync();
        return guardado ?? Resultado<ReservaDto>.Ok(ADto(reserva));
    }

    /// <summary>Libera las unidades de las reservas cuyo plazo venció sin confirmarse.</summary>
    public async Task<int> VencerReservasAsync()
    {
        var ahora = Ahora;
        var vencidas = await db.Reservas
            .Where(r => r.Estado == EstadosReserva.Activa && r.FechaVencimiento <= ahora)
            .ToListAsync();
        if (vencidas.Count == 0) return 0;

        foreach (var r in vencidas)
        {
            var e = await BuscarExistenciaAsync(r.Referencia, r.Talla, r.Sede);
            if (e is null) continue;
            e.UnidadesReservadas -= r.Cantidad;
            r.Estado = EstadosReserva.Vencida;
            RegistrarMovimiento(TiposMovimiento.VencimientoReserva, e, r.Cantidad, "SISTEMA", r.Pedido);
        }
        await db.SaveChangesAsync();
        return vencidas.Count;
    }

    // ------------------------------------------------------------------ Apoyo

    private IQueryable<ExistenciaConNombre> ConsultaExistenciasConProducto() =>
        db.Existencias.AsNoTracking()
            .Join(db.Productos, e => e.Referencia, p => p.Referencia, (e, p) => new ExistenciaConNombre { E = e, Nombre = p.Nombre });

    private sealed class ExistenciaConNombre
    {
        public Existencia E { get; init; } = null!;
        public string Nombre { get; init; } = "";
    }

    private Task<Existencia?> BuscarExistenciaAsync(string referencia, string talla, string sede)
    {
        var (r, t, s) = (Normalizar(referencia), Normalizar(talla), Normalizar(sede));
        // Primero se busca en las entidades ya cargadas en esta operación.
        var local = db.Existencias.Local.FirstOrDefault(e => e.Referencia == r && e.Talla == t && e.Sede == s);
        return local is not null
            ? Task.FromResult<Existencia?>(local)
            : db.Existencias.FirstOrDefaultAsync(e => e.Referencia == r && e.Talla == t && e.Sede == s);
    }

    private Movimiento RegistrarMovimiento(string tipo, Existencia e, int cantidad, string canal, string? documento)
    {
        e.UltimaActualizacion = Ahora;
        e.VersionFila++;
        var mov = new Movimiento
        {
            Tipo = tipo,
            Referencia = e.Referencia,
            Talla = e.Talla,
            Sede = e.Sede,
            Cantidad = cantidad,
            Canal = canal,
            Documento = documento,
            DisponiblesResultantes = e.Disponibles,
            Fecha = Ahora,
        };
        db.Movimientos.Add(mov);
        return mov;
    }

    private async Task<Resultado<OperacionInventarioDto>> GuardarOperacionAsync(Existencia e, Movimiento mov)
    {
        var guardado = await GuardarAsync();
        if (guardado is not null)
            return Resultado<OperacionInventarioDto>.Falla(guardado.Status, guardado.Error!.Codigo, guardado.Error.Mensaje);

        var nombre = await db.Productos.Where(p => p.Referencia == e.Referencia).Select(p => p.Nombre).FirstAsync();
        var r = Resultado<OperacionInventarioDto>.Ok(new OperacionInventarioDto(
            mov.Id, mov.Tipo, mov.Referencia, mov.Talla, mov.Sede, mov.Cantidad, mov.Canal, mov.Documento, ADto(e, nombre)));
        return r;
    }

    /// <summary>Guarda los cambios. Devuelve null si todo salió bien o el error de concurrencia.</summary>
    private async Task<Resultado<ReservaDto>?> GuardarAsync()
    {
        try
        {
            await db.SaveChangesAsync();
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Resultado<ReservaDto>.Conflicto("CONFLICTO_CONCURRENCIA",
                "Otro canal modificó estas existencias al mismo tiempo. Vuelva a intentar la operación.");
        }
    }

    private static string Normalizar(string valor) => valor.Trim().ToUpperInvariant();

    private static string NoExiste(string referencia, string talla, string sede) =>
        $"No hay existencias registradas para {Normalizar(referencia)} talla {Normalizar(talla)} en {Normalizar(sede)}.";

    private static string? ValidarCampos(params (string Nombre, string? Valor)[] campos) =>
        campos.Where(c => string.IsNullOrWhiteSpace(c.Valor))
              .Select(c => $"El campo '{c.Nombre}' es obligatorio.")
              .FirstOrDefault();

    private static string? ValidarCantidad(int cantidad) =>
        cantidad is < 1 or > 1000 ? "La cantidad debe estar entre 1 y 1000." : null;

    private static ExistenciaDto ADto(Existencia e, string nombre) =>
        new(e.Referencia, nombre, e.Talla, e.Sede, e.UnidadesFisicas, e.UnidadesReservadas,
            e.Disponibles, e.StockMinimo, e.UltimaActualizacion);

    private static ReservaDto ADto(Reserva r) =>
        new(r.Id, r.Pedido, r.Referencia, r.Talla, r.Sede, r.Cantidad, r.Estado, r.FechaCreacion, r.FechaVencimiento);
}
