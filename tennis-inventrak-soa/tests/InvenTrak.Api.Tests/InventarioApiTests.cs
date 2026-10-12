using System.Net;
using System.Net.Http.Json;
using System.Text;
using InvenTrak.Api.Models;

namespace InvenTrak.Api.Tests;

public class InventarioApiTests : IDisposable
{
    private readonly ApiFactory _app = new();
    private readonly HttpClient _cliente;

    public InventarioApiTests() => _cliente = _app.ClienteConClave();
    public void Dispose() => _app.Dispose();

    private async Task<ExistenciaDto> ExistenciaAsync(string referencia, string talla, string sede) =>
        (await _cliente.GetFromJsonAsync<List<ExistenciaDto>>(
            $"/api/v1/existencias?referencia={referencia}&talla={talla}&sede={sede}"))!.Single();

    private async Task<int> DisponibleEnLineaAsync(string referencia, string talla) =>
        (await _cliente.GetFromJsonAsync<DisponibilidadEnLineaDto>($"/api/v1/disponibilidad-en-linea/{referencia}"))!
            .Tallas.Single(t => t.Talla == talla).Disponibles;

    [Fact]
    public async Task Health_responde_OK()
    {
        var r = await _cliente.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Existencias_filtra_por_referencia_y_talla_sin_distinguir_mayusculas()
    {
        var lista = await _cliente.GetFromJsonAsync<List<ExistenciaDto>>("/api/v1/existencias?referencia=cal-021&talla=40");
        Assert.Equal(3, lista!.Count);
        Assert.All(lista, e => Assert.Equal(e.UnidadesFisicas - e.UnidadesReservadas, e.Disponibles));
    }

    [Fact]
    public async Task Productos_permite_buscar_por_categoria_y_texto()
    {
        var calzado = await _cliente.GetFromJsonAsync<List<ProductoDto>>("/api/v1/productos?categoria=calzado");
        Assert.Equal(2, calzado!.Count);
        var jean = await _cliente.GetFromJsonAsync<List<ProductoDto>>("/api/v1/productos?busqueda=jean");
        Assert.Equal("JEA-014", Assert.Single(jean!).Referencia);
    }

    [Fact]
    public async Task Venta_en_POS_se_refleja_de_inmediato_en_la_tienda_en_linea()
    {
        var antesTienda = await ExistenciaAsync("CAL-021", "41", "TIENDA-CENTRO");
        var antesOnline = await DisponibleEnLineaAsync("CAL-021", "41");

        var r = await _cliente.PostAsJsonAsync("/api/v1/ventas",
            new VentaRequest("CAL-021", "41", "TIENDA-CENTRO", 2, "POS"));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var op = await r.LeerAsync<OperacionInventarioDto>();
        Assert.Equal(antesTienda.UnidadesFisicas - 2, op.StockResultante.UnidadesFisicas);
        Assert.Equal(antesOnline - 2, await DisponibleEnLineaAsync("CAL-021", "41"));
    }

    [Fact]
    public async Task Venta_sin_stock_suficiente_responde_409()
    {
        // En los datos de ejemplo, SWT-001 talla L en TIENDA-NORTE tiene 0 unidades.
        var r = await _cliente.PostAsJsonAsync("/api/v1/ventas",
            new VentaRequest("SWT-001", "L", "TIENDA-NORTE", 1, null));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("STOCK_INSUFICIENTE", (await r.LeerAsync<ErrorDto>()).Codigo);
    }

    [Fact]
    public async Task Operaciones_de_escritura_sin_clave_responden_401()
    {
        var sinClave = _app.CreateClient();
        var r = await sinClave.PostAsJsonAsync("/api/v1/ventas", new VentaRequest("CAL-021", "40", "TIENDA-CENTRO", 1, null));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Datos_invalidos_o_JSON_mal_formado_responden_400()
    {
        var r1 = await _cliente.PostAsJsonAsync("/api/v1/ventas", new VentaRequest("CAL-021", "40", "TIENDA-CENTRO", 0, null));
        Assert.Equal(HttpStatusCode.BadRequest, r1.StatusCode);

        var r2 = await _cliente.PostAsync("/api/v1/ventas",
            new StringContent("{\"referencia\":", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, r2.StatusCode);
        Assert.Equal("SOLICITUD_INVALIDA", (await r2.LeerAsync<ErrorDto>()).Codigo);
    }

    [Fact]
    public async Task Referencia_inexistente_responde_404()
    {
        var r = await _cliente.GetAsync("/api/v1/disponibilidad-en-linea/NO-EXISTE");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Reserva_aparta_unidades_y_al_liberarla_vuelven_a_estar_disponibles()
    {
        var antes = await ExistenciaAsync("JEA-014", "M", "BODEGA-CENTRAL");

        var r = await _cliente.PostAsJsonAsync("/api/v1/reservas",
            new ReservaRequest("WEB-2001", "JEA-014", "M", "BODEGA-CENTRAL", 3, null));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var reserva = await r.LeerAsync<ReservaDto>();

        var durante = await ExistenciaAsync("JEA-014", "M", "BODEGA-CENTRAL");
        Assert.Equal(antes.Disponibles - 3, durante.Disponibles);
        Assert.Equal(antes.UnidadesFisicas, durante.UnidadesFisicas);

        var lib = await _cliente.PostAsync($"/api/v1/reservas/{reserva.Id}/liberar", null);
        Assert.Equal(EstadosReserva.Liberada, (await lib.LeerAsync<ReservaDto>()).Estado);
        Assert.Equal(antes.Disponibles, (await ExistenciaAsync("JEA-014", "M", "BODEGA-CENTRAL")).Disponibles);
    }

    [Fact]
    public async Task Confirmar_reserva_descuenta_unidades_y_no_se_puede_confirmar_dos_veces()
    {
        var antes = await ExistenciaAsync("CHQ-007", "L", "BODEGA-CENTRAL");
        var reserva = await (await _cliente.PostAsJsonAsync("/api/v1/reservas",
            new ReservaRequest("WEB-2002", "CHQ-007", "L", "BODEGA-CENTRAL", 1, 15))).LeerAsync<ReservaDto>();

        var ok = await _cliente.PostAsync($"/api/v1/reservas/{reserva.Id}/confirmar", null);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var despues = await ExistenciaAsync("CHQ-007", "L", "BODEGA-CENTRAL");
        Assert.Equal(antes.UnidadesFisicas - 1, despues.UnidadesFisicas);
        Assert.Equal(antes.UnidadesReservadas, despues.UnidadesReservadas);

        var otraVez = await _cliente.PostAsync($"/api/v1/reservas/{reserva.Id}/confirmar", null);
        Assert.Equal(HttpStatusCode.Conflict, otraVez.StatusCode);
    }

    [Fact]
    public async Task Reserva_no_confirmada_vence_y_libera_las_unidades()
    {
        var antes = await ExistenciaAsync("ACC-012", "UNICA", "BODEGA-CENTRAL");
        var reserva = await (await _cliente.PostAsJsonAsync("/api/v1/reservas",
            new ReservaRequest("WEB-2003", "ACC-012", "UNICA", "BODEGA-CENTRAL", 2, 10))).LeerAsync<ReservaDto>();

        _app.Reloj.Avanzar(TimeSpan.FromMinutes(11));

        var consultada = await _cliente.GetFromJsonAsync<ReservaDto>($"/api/v1/reservas/{reserva.Id}");
        Assert.Equal(EstadosReserva.Vencida, consultada!.Estado);
        Assert.Equal(antes.Disponibles, (await ExistenciaAsync("ACC-012", "UNICA", "BODEGA-CENTRAL")).Disponibles);
    }

    [Fact]
    public async Task Entrada_de_mercancia_suma_stock_y_crea_tallas_nuevas_en_la_sede()
    {
        var r = await _cliente.PostAsJsonAsync("/api/v1/entradas",
            new EntradaRequest("OC-2026-500", "CAL-033", "43", "TIENDA-CENTRO", 6, 2));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var op = await r.LeerAsync<OperacionInventarioDto>();
        Assert.Equal(6, op.StockResultante.UnidadesFisicas);
        Assert.Equal(2, op.StockResultante.StockMinimo);

        await _cliente.PostAsJsonAsync("/api/v1/entradas",
            new EntradaRequest("OC-2026-501", "CAL-033", "43", "TIENDA-CENTRO", 4, null));
        Assert.Equal(10, (await ExistenciaAsync("CAL-033", "43", "TIENDA-CENTRO")).UnidadesFisicas);
    }

    [Fact]
    public async Task Alertas_incluyen_referencias_en_o_bajo_el_minimo_y_desaparecen_al_reponer()
    {
        var alertas = await _cliente.GetFromJsonAsync<List<AlertaReposicionDto>>("/api/v1/alertas?sede=TIENDA-NORTE");
        Assert.Contains(alertas!, a => a.Referencia == "SWT-001" && a.Talla == "L");

        await _cliente.PostAsJsonAsync("/api/v1/entradas",
            new EntradaRequest("OC-2026-502", "SWT-001", "L", "TIENDA-NORTE", 10, null));

        alertas = await _cliente.GetFromJsonAsync<List<AlertaReposicionDto>>("/api/v1/alertas?sede=TIENDA-NORTE");
        Assert.DoesNotContain(alertas!, a => a.Referencia == "SWT-001" && a.Talla == "L");
    }

    [Fact]
    public async Task Cada_operacion_queda_registrada_en_la_auditoria()
    {
        await _cliente.PostAsJsonAsync("/api/v1/ventas", new VentaRequest("ACC-005", "UNICA", "TIENDA-CENTRO", 1, "POS"));
        await _cliente.PostAsJsonAsync("/api/v1/entradas", new EntradaRequest("OC-1", "ACC-005", "UNICA", "TIENDA-CENTRO", 5, null));

        var movs = await _cliente.GetFromJsonAsync<List<MovimientoDto>>("/api/v1/movimientos?referencia=ACC-005");
        Assert.Equal(new[] { TiposMovimiento.Entrada, TiposMovimiento.Venta }, movs!.Select(m => m.Tipo));
    }
}
