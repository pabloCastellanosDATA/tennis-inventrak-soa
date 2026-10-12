using System.Net;
using System.Net.Http.Json;
using InvenTrak.Api.Models;

namespace InvenTrak.Api.Tests;

public class CatalogoApiTests : IDisposable
{
    private readonly ApiFactory _app = new();
    private readonly HttpClient _cliente;

    public CatalogoApiTests() => _cliente = _app.ClienteConClave();
    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Catalogo_publica_los_seis_servicios_del_inventario_y_el_propio_registro()
    {
        var servicios = await _cliente.GetFromJsonAsync<List<ServicioDto>>("/api/v1/catalogo");
        Assert.Equal(7, servicios!.Count);
        Assert.All(servicios, s => Assert.NotEmpty(s.Operaciones));
    }

    [Fact]
    public async Task Catalogo_filtra_por_consumidor()
    {
        var delPos = await _cliente.GetFromJsonAsync<List<ServicioDto>>("/api/v1/catalogo?consumidor=POS");
        Assert.Contains(delPos!, s => s.Nombre == "ServicioVentasPresenciales");
        Assert.DoesNotContain(delPos!, s => s.Nombre == "ServicioReservasPedido");
    }

    [Fact]
    public async Task Registrar_servicio_nuevo_y_evitar_duplicados()
    {
        var nuevo = new ServicioRequest("ServicioDevoluciones", "Reingresa unidades devueltas por clientes.",
            "InvenTrak", "0.1.0", "EN_DISENO", new() { "POST /api/v1/devoluciones" }, "/api/v1/devoluciones",
            new() { "POS", "Tienda en línea" });

        var r = await _cliente.PostAsJsonAsync("/api/v1/catalogo", nuevo);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);

        var dup = await _cliente.PostAsJsonAsync("/api/v1/catalogo", nuevo with { Nombre = "serviciodevoluciones" });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task Actualizar_version_y_estado_de_un_servicio()
    {
        var r = await _cliente.PutAsJsonAsync("/api/v1/catalogo/ServicioExistencias",
            new ActualizarServicioRequest(null, null, "1.1.0", "PUBLICADO", null, null, null));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var s = await _cliente.GetFromJsonAsync<ServicioDto>("/api/v1/catalogo/ServicioExistencias");
        Assert.Equal("1.1.0", s!.Version);
        Assert.Equal("InvenTrak", s.Responsable); // los campos no enviados se conservan
    }

    [Fact]
    public async Task Estado_invalido_responde_400_y_servicio_inexistente_404()
    {
        var invalido = await _cliente.PutAsJsonAsync("/api/v1/catalogo/ServicioExistencias",
            new ActualizarServicioRequest(null, null, null, "BORRADOR", null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);

        var noExiste = await _cliente.GetAsync("/api/v1/catalogo/ServicioQueNoExiste");
        Assert.Equal(HttpStatusCode.NotFound, noExiste.StatusCode);
    }
}
