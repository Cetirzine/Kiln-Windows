using System.Text.Json;
using System.Text.Json.Serialization;
namespace Kiln.Core;

// Swift Codable writes lowercase strings. Accept older Windows numeric values too.
public sealed class AccountKindConverter : JsonConverter<Account.AccountKind>
{
    public override Account.AccountKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number) && Enum.IsDefined(typeof(Account.AccountKind), number)) return (Account.AccountKind)number;
        if (reader.TokenType == JsonTokenType.String && Enum.TryParse<Account.AccountKind>(reader.GetString(), true, out var kind) && Enum.IsDefined(kind)) return kind;
        throw new JsonException("Unknown account kind.");
    }
    public override void Write(Utf8JsonWriter writer, Account.AccountKind value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString().ToLowerInvariant());
}
