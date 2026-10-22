using System.Text.Json;
using System.Text.Json.Serialization;
using Einzel.Core.Errors;
using Einzel.Core.Model;

namespace Einzel.Io;

/// <summary>
/// Reads and writes model documents as JSON.
/// </summary>
/// <remarks>
/// <para>
/// AGT-1 requires the model to be declarative, schema-validated, diffable JSON.
/// The serialiser settings here serve the "diffable" half: indented output,
/// camel-case names, and property order fixed by declaration order, so that two
/// models differing in one parameter differ in one line.
/// </para>
/// <para>
/// Deserialisation failures are translated into AGT-3 error objects. A raw
/// <see cref="JsonException"/> names a .NET type and a byte offset, which is
/// useless to an agent; the translated form names a JSON Pointer and says what
/// was expected.
/// </para>
/// <para>
/// <strong>An unrecognised property is an error, not something to ignore.</strong>
/// This is the same rule as requiring a unit on every quantity, applied to the key
/// instead of the value, and for the same reason: section 5's whole argument is
/// that an agent building from prose is the actor most likely to introduce a
/// mistake, and a misspelled field name that is silently dropped is the purest
/// form of the section 22 headline risk. The model validates, solves, runs, and
/// answers a different question from the one the document appears to ask.
/// </para>
/// <para>
/// It was found by writing the example corpus. A cloud declaring
/// <c>transverseWidth</c> instead of <c>transverseSpread</c> parsed cleanly, gave
/// a packet with no spatial extent, and produced an emittance of 7.1e-8 um where
/// the closed form says 1.798 - a plausible number, from a model that read as
/// though it said something else.
/// </para>
/// </remarks>
public static class ModelJson
{
    /// <summary>The serialiser options used for every model document.</summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        NewLine = "\n",
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Skip,

        // A property the format does not have is a mistake worth stopping for. See
        // the remarks above: the alternative is a model that validates and answers
        // a question nobody asked.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Parses a model document.</summary>
    /// <param name="json">The document text.</param>
    /// <returns>The parsed document.</returns>
    /// <exception cref="EinzelException">The text is not a valid model document.</exception>
    public static ModelDocument Parse(string json)
    {
        try
        {
            var document = JsonSerializer.Deserialize<ModelDocument>(json, Options);

            if (document is null)
            {
                throw new EinzelException(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = "/",
                    Constraint = "a model document must be a JSON object",
                    Suggestion = "the file appears to contain only 'null'",
                });
            }

            return document;
        }
        catch (JsonException failure)
        {
            var unmapped = failure.Message.Contains(
                "could not be mapped", StringComparison.OrdinalIgnoreCase);

            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = PointerFrom(failure.Path),
                Constraint = failure.Message,
                Suggestion = unmapped
                    ? "this property is not part of the model format. Check the spelling against "
                        + "'einzel schema', which is generated from the document types and so "
                        + "cannot be out of date. An unrecognised property is refused rather than "
                        + "ignored, because a misspelled field that is silently dropped gives a "
                        + "model that validates and answers a different question"
                    : "check that quantities are written as {\"value\": ..., \"unit\": \"...\"}",
            }, failure);
        }
    }

    /// <summary>Serialises a model document.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The document text, newline terminated.</returns>
    public static string Write(ModelDocument document) =>
        JsonSerializer.Serialize(document, Options) + "\n";

    /// <summary>
    /// Converts a System.Text.Json path such as <c>$.source.position</c> into the
    /// JSON Pointer form AGT-3 uses.
    /// </summary>
    internal static string PointerFrom(string? jsonPath)
    {
        if (string.IsNullOrEmpty(jsonPath) || jsonPath == "$")
        {
            return "/";
        }

        var pointer = jsonPath
            .Replace("$", string.Empty, StringComparison.Ordinal)
            .Replace('.', '/')
            .Replace("[", "/", StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal);

        return pointer.StartsWith('/') ? pointer : "/" + pointer;
    }
}
