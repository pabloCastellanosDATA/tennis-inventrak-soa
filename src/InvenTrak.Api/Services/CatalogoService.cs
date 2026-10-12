using InvenTrak.Api.Data;
using InvenTrak.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace InvenTrak.Api.Services;

/// <summary>
/// Registro central de servicios (patrón Metadata Centralization, Erl, 2009).
/// Permite saber qué servicios existen, quién los mantiene, su versión, contrato y consumidores
/// antes de construir una integración nueva.
/// </summary>
public class CatalogoService(InventarioDbContext db, TimeProvider reloj)
{
    public async Task<List<ServicioDto>> ListarAsync(string? estado, string? consumidor)
    {
        var servicios = await db.Servicios.AsNoTracking().OrderBy(s => s.Nombre).ToListAsync();
        IEnumerable<ServicioRegistrado> q = servicios;
        if (!string.IsNullOrWhiteSpace(estado))
            q = q.Where(s => s.Estado.Equals(estado.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(consumidor))
            q = q.Where(s => s.Consumidores.Any(c => c.Contains(consumidor.Trim(), StringComparison.OrdinalIgnoreCase)));
        return q.Select(ADto).ToList();
    }

    public async Task<Resultado<ServicioDto>> ObtenerAsync(string nombre)
    {
        var s = await BuscarAsync(nombre);
        return s is null
            ? Resultado<ServicioDto>.NoEncontrado($"El servicio '{nombre}' no está registrado en el catálogo.")
            : Resultado<ServicioDto>.Ok(ADto(s));
    }

    public async Task<Resultado<ServicioDto>> RegistrarAsync(ServicioRequest req)
    {
        var error = Validar(req.Nombre, req.Descripcion, req.Responsable, req.Version, req.Ubicacion, req.Estado, req.Operaciones);
        if (error is not null) return Resultado<ServicioDto>.Invalido(error);
        if (await BuscarAsync(req.Nombre) is not null)
            return Resultado<ServicioDto>.Conflicto("SERVICIO_DUPLICADO",
                $"Ya existe un servicio llamado '{req.Nombre.Trim()}'. Reutilícelo o actualice su versión.");

        var s = new ServicioRegistrado
        {
            Nombre = req.Nombre.Trim(),
            Descripcion = req.Descripcion.Trim(),
            Responsable = req.Responsable.Trim(),
            Version = req.Version.Trim(),
            Estado = (req.Estado ?? EstadosServicio.EnDiseno).Trim().ToUpperInvariant(),
            Operaciones = Limpiar(req.Operaciones),
            Ubicacion = req.Ubicacion.Trim(),
            Consumidores = Limpiar(req.Consumidores),
            UltimaActualizacion = reloj.GetUtcNow().UtcDateTime,
        };
        db.Servicios.Add(s);
        await db.SaveChangesAsync();
        return Resultado<ServicioDto>.Ok(ADto(s));
    }

    public async Task<Resultado<ServicioDto>> ActualizarAsync(string nombre, ActualizarServicioRequest req)
    {
        var s = await BuscarAsync(nombre);
        if (s is null) return Resultado<ServicioDto>.NoEncontrado($"El servicio '{nombre}' no está registrado en el catálogo.");

        var error = Validar(s.Nombre, req.Descripcion ?? s.Descripcion, req.Responsable ?? s.Responsable,
            req.Version ?? s.Version, req.Ubicacion ?? s.Ubicacion, req.Estado, req.Operaciones ?? s.Operaciones);
        if (error is not null) return Resultado<ServicioDto>.Invalido(error);

        if (req.Descripcion is not null) s.Descripcion = req.Descripcion.Trim();
        if (req.Responsable is not null) s.Responsable = req.Responsable.Trim();
        if (req.Version is not null) s.Version = req.Version.Trim();
        if (req.Estado is not null) s.Estado = req.Estado.Trim().ToUpperInvariant();
        if (req.Operaciones is not null) s.Operaciones = Limpiar(req.Operaciones);
        if (req.Ubicacion is not null) s.Ubicacion = req.Ubicacion.Trim();
        if (req.Consumidores is not null) s.Consumidores = Limpiar(req.Consumidores);
        s.UltimaActualizacion = reloj.GetUtcNow().UtcDateTime;

        await db.SaveChangesAsync();
        return Resultado<ServicioDto>.Ok(ADto(s));
    }

    private async Task<ServicioRegistrado?> BuscarAsync(string nombre)
    {
        var n = nombre.Trim().ToLower();
        return await db.Servicios.FirstOrDefaultAsync(s => s.Nombre.ToLower() == n);
    }

    private static string? Validar(string? nombre, string? descripcion, string? responsable, string? version,
        string? ubicacion, string? estado, List<string>? operaciones)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return "El campo 'nombre' es obligatorio.";
        if (nombre.Contains(' ')) return "El nombre del servicio no debe tener espacios (ej.: ServicioExistencias).";
        if (string.IsNullOrWhiteSpace(descripcion)) return "El campo 'descripcion' es obligatorio.";
        if (string.IsNullOrWhiteSpace(responsable)) return "El campo 'responsable' es obligatorio.";
        if (string.IsNullOrWhiteSpace(version)) return "El campo 'version' es obligatorio.";
        if (string.IsNullOrWhiteSpace(ubicacion)) return "El campo 'ubicacion' es obligatorio.";
        if (operaciones is null || operaciones.All(string.IsNullOrWhiteSpace))
            return "El contrato debe declarar al menos una operación.";
        if (estado is not null && !EstadosServicio.Todos.Contains(estado.Trim().ToUpperInvariant()))
            return $"Estado inválido. Valores permitidos: {string.Join(", ", EstadosServicio.Todos)}.";
        return null;
    }

    private static List<string> Limpiar(List<string>? valores) =>
        (valores ?? new()).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim().Replace("|", "/")).ToList();

    private static ServicioDto ADto(ServicioRegistrado s) =>
        new(s.Nombre, s.Descripcion, s.Responsable, s.Version, s.Estado, s.Operaciones, s.Ubicacion,
            s.Consumidores, s.UltimaActualizacion);
}
