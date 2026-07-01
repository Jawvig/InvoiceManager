using System.Text.Json;
using Microsoft.Azure.Cosmos;

namespace InvoiceManager.Infrastructure.CosmosDb;

/// <summary>
/// A Cosmos DB serializer that uses System.Text.Json with camelCase property naming,
/// so that <see cref="System.Text.Json.Serialization.JsonPropertyNameAttribute"/> attributes
/// on document types are respected rather than the SDK's default Newtonsoft.Json serializer.
/// </summary>
internal sealed class CosmosStjSerializer : CosmosSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public override T FromStream<T>(Stream stream)
    {
        using (stream)
        {
            if (typeof(Stream).IsAssignableFrom(typeof(T)))
                return (T)(object)stream;

            return JsonSerializer.Deserialize<T>(stream, Options)!;
        }
    }

    public override Stream ToStream<T>(T input)
    {
        var stream = new MemoryStream();
        JsonSerializer.Serialize(stream, input, Options);
        stream.Position = 0;
        return stream;
    }
}
