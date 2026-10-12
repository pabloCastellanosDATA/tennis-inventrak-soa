using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace InvenTrak.Api.Seguridad;

/// <summary>En Swagger, marca con candado solo las operaciones que exigen X-Api-Key (POST y PUT).</summary>
public class SoloEscrituraRequiereClave : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metodo = context.ApiDescription.HttpMethod;
        if (metodo is not ("POST" or "PUT")) return;

        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" } }]
                = Array.Empty<string>(),
        });
    }
}
