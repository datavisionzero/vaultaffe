using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Vaultaffe.Api.Http;

/// <summary>
/// The document at <c>/openapi/v1.json</c>: captured into
/// <c>docs/api/openapi.json</c>, checked in, and compared against what a running
/// instance serves
/// (<see href="../../../docs/adr/0006-the-contract-is-checked-in-and-the-web-client-is-generated-from-it.md">ADR 0006</see>).
/// The web client is generated from it, so it has to describe the shape of the
/// API and nothing about the machine that happened to serve it.
/// </summary>
/// <remarks>
/// <c>info.version</c> is the version of the <i>contract</i>, not of the
/// instance. This API carries its version in the path (ADR 0005), which makes the
/// contract's version the honest answer here and keeps the checked-in document
/// from changing every time a release is cut. Which release an instance is, is
/// the handshake's answer and the <c>Vaultaffe-Version</c> header's.
/// </remarks>
public static class OpenApiDocument
{
    /// <summary>
    /// A CLR type ending in <c>Shape</c> is the contract's spelling of the
    /// concept of the same name — <c>TokenShape</c> is the <c>Token</c> of
    /// <c>docs/api.md</c> — so the suffix is dropped from the schema id and a
    /// generated client sees the word the specification uses.
    /// </summary>
    private const string ShapeSuffix = "Shape";

    public static IServiceCollection AddVaultaffeOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(ApiVersion.Current, options =>
        {
            options.CreateSchemaReferenceId = info =>
            {
                var id = OpenApiOptions.CreateDefaultSchemaReferenceId(info);

                return id is not null && id.EndsWith(ShapeSuffix, StringComparison.Ordinal)
                    ? id[..^ShapeSuffix.Length]
                    : id;
            };

            options.AddSchemaTransformer((schema, context, _) =>
            {
                // What ASP.NET returns as a refusal is a ProblemDetails plus the
                // members put beside it, and `code` is on every single one — it
                // is what a client switches on (docs/api.md). The generator
                // cannot see an extension, so it is said here rather than left
                // for each client to know by hand.
                if (context.JsonTypeInfo.Type == typeof(ProblemDetails))
                {
                    schema.Properties?.Add("code", new OpenApiSchema { Type = JsonSchemaType.String });
                    schema.Required = new HashSet<string>(StringComparer.Ordinal) { "code" };
                    schema.AdditionalPropertiesAllowed = true;
                }

                Plain(schema);

                return Task.CompletedTask;
            });

            options.AddDocumentTransformer((document, _, _) =>
            {
                foreach (var operation in document.Paths.Values
                    .Where(path => path.Operations is not null)
                    .SelectMany(path => path.Operations!.Values))
                {
                    foreach (var parameter in operation.Parameters ?? [])
                    {
                        Plain(parameter.Schema);
                    }
                }

                document.Info.Title = "vaultaffe";
                document.Info.Version = ApiVersion.Current;
                document.Info.Description =
                    "The HTTP surface of one vaultaffe instance, and the only way in or out of it "
                    + "(Specification §9). Every instance is at its own address. Everything but the "
                    + "handshake and the problem catalogue lives under /api/" + ApiVersion.Current
                    + "/, and every refusal is an application/problem+json document whose `code` "
                    + "names it.";

                // Whoever captured it was at some address; nobody else is.
                document.Servers?.Clear();

                return Task.CompletedTask;
            });
        });

    /// <summary>
    /// An integer is an integer and not "also a string": the string half is how
    /// ASP.NET reads a value off the wire, not what the value is. The pattern it
    /// adds for the same reason goes with it.
    /// </summary>
    private static void Plain(IOpenApiSchema? schema)
    {
        if (schema is not OpenApiSchema concrete || concrete.Type is not { } type)
        {
            return;
        }

        if (type.HasFlag(JsonSchemaType.String)
            && (type & (JsonSchemaType.Integer | JsonSchemaType.Number | JsonSchemaType.Boolean)) != 0)
        {
            concrete.Type = type & ~JsonSchemaType.String;
            concrete.Pattern = null;
        }

        foreach (var property in concrete.Properties?.Values ?? [])
        {
            Plain(property);
        }
    }
}
