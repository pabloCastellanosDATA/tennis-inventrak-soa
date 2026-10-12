using InvenTrak.Api.Models;

namespace InvenTrak.Api.Services;

/// <summary>Resultado de una operación de negocio: un valor o un error con su código HTTP.</summary>
public class Resultado<T>
{
    public T? Valor { get; private init; }
    public int Status { get; private init; }
    public ErrorDto? Error { get; private init; }
    public bool Exito => Error is null;

    public static Resultado<T> Ok(T valor) => new() { Valor = valor, Status = 200 };

    public static Resultado<T> Falla(int status, string codigo, string mensaje) =>
        new() { Status = status, Error = new ErrorDto(codigo, mensaje) };

    public static Resultado<T> NoEncontrado(string mensaje) => Falla(404, "NO_ENCONTRADO", mensaje);
    public static Resultado<T> Invalido(string mensaje) => Falla(400, "VALIDACION", mensaje);
    public static Resultado<T> Conflicto(string codigo, string mensaje) => Falla(409, codigo, mensaje);
}
