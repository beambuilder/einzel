using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Einzel.Core.Model;

/// <summary>
/// Emits the model format as JSON Schema, generated from the document types.
/// </summary>
/// <remarks>
/// <para>
/// AGT-7 asks that schema descriptions and CLI help come from the same metadata
/// rather than being maintained beside it. This is the mechanism: the schema is
/// walked out of <see cref="ModelDocument"/> by reflection, and the descriptions
/// are the XML documentation comments the build already requires on every public
/// member. A property added to the format appears in the schema without anyone
/// remembering to add it, and one whose meaning changes carries its new
/// description automatically.
/// </para>
/// <para>
/// That property is the whole point rather than a convenience. An agent building a
/// model from prose has the schema and the error messages and nothing else - no
/// forum posts, no worked examples accumulated over decades. A schema that has
/// quietly drifted from the code is worse than none, because it is trusted.
/// </para>
/// <para>
/// The XML file sits beside the assembly and may be absent in some deployments.
/// When it is, the schema is still emitted, structurally complete and without
/// descriptions, and says so in its own <c>$comment</c>. Silently emitting an
/// undocumented schema that looks identical to a documented one would be the
/// wrong failure.
/// </para>
/// </remarks>
public static class ModelSchemaWriter
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, NewLine = "\n" };

    /// <summary>Writes the model format as a JSON Schema document.</summary>
    /// <returns>The schema, as indented JSON text with a trailing newline.</returns>
    public static string Write() =>
        Write<ModelDocument>("Einzel model", "model", ModelSchema.CurrentVersion, null, new Dictionary<string, IReadOnlyList<string>>
        {
            ["WaveformDocument.type"] = WaveformDocument.Types,
            ["WaveformDocument.interpolation"] = ["linear", "cubic"],
        });

    /// <summary>Writes any document type as a JSON Schema document.</summary>
    /// <typeparam name="T">The document type to describe.</typeparam>
    /// <param name="title">The schema title.</param>
    /// <param name="slug">The kind, for the schema identifier.</param>
    /// <param name="version">The format version this build writes.</param>
    /// <param name="description">What the document is, when the type's own summary is not enough.</param>
    /// <param name="allowed">
    /// Permitted values for named properties, by camel-case property name, or as
    /// <c>Type.property</c> for a property of a nested document record. Some
    /// fields take a string out of a set the type system does not express - which
    /// figure of merit, which algorithm, which unit of memory - and leaving that to
    /// prose is how invalid documents get written.
    /// </param>
    /// <returns>The schema, as indented JSON text with a trailing newline.</returns>
    public static string Write<T>(
        string title,
        string slug,
        string version,
        string? description,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? allowed)
    {
        // Documentation from the described type's own assembly as well as the
        // core one: a study document does not live beside the model it studies,
        // and a schema missing half its descriptions is the failure this whole
        // mechanism exists to avoid.
        var documentation = LoadDocumentation(typeof(T).Assembly);

        foreach (var (key, text) in LoadDocumentation(typeof(ModelDocument).Assembly))
        {
            documentation.TryAdd(key, text);
        }
        var definitions = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal);

        var root = Describe(typeof(T), documentation, definitions);

        if (allowed is not null)
        {
            foreach (var (name, values) in allowed)
            {
                // "Type.property" names a property of a nested record, described once in $defs.
                var dot = name.IndexOf('.', StringComparison.Ordinal);
                var owner = dot < 0 ? root : definitions.GetValueOrDefault(name[..dot]);

                if (owner?["properties"]?[name[(dot + 1)..]] is JsonObject property)
                {
                    property["enum"] = new JsonArray([.. values.Select(v => (JsonNode)v)]);
                }
            }
        }

        var schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = $"https://einzel.dev/schema/{slug}/{version}",
            ["title"] = title,
            ["$comment"] = documentation.Count > 0
                ? "Generated from the document types. Descriptions are their XML documentation comments."
                : "Generated from the document types. Descriptions are UNAVAILABLE: the XML "
                    + "documentation file was not found beside the assembly.",
        };

        if (!string.IsNullOrEmpty(description))
        {
            schema["description"] = description;
        }

        schema["x-schemaVersion"] = version;

        if (typeof(T) == typeof(ModelDocument))
        {
            schema["x-supportedVersions"] =
                new JsonArray([.. ModelSchema.SupportedVersions.Select(v => (JsonNode)v)]);
        }

        // Detach each property from the fragment before adopting it: a JsonNode
        // knows its parent and refuses to have two.
        foreach (var key in root.Select(pair => pair.Key).ToArray())
        {
            root.Remove(key, out var value);
            schema[key] = value;
        }

        if (definitions.Count > 0)
        {
            var defs = new JsonObject();

            foreach (var (name, node) in definitions)
            {
                defs[name] = node;
            }

            schema["$defs"] = defs;
        }

        return JsonSerializer.Serialize(schema, Indented) + "\n";
    }

    /// <summary>
    /// The JSON Schema fragment for one type, adding any nested record types to
    /// the shared definitions as it goes.
    /// </summary>
    private static JsonObject Describe(
        Type type, IReadOnlyDictionary<string, string> documentation, IDictionary<string, JsonNode> definitions)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var property in Ordered(type))
        {
            var name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            var node = Property(property, documentation, definitions);

            if (documentation.TryGetValue(Key(property), out var text))
            {
                node["description"] = text;
            }

            properties[name] = node;

            // Required means the format cannot be understood without it, which is
            // what a non-nullable reference property with no default expresses.
            if (property.GetCustomAttribute<System.Runtime.CompilerServices.RequiredMemberAttribute>() is not null)
            {
                required.Add(name);
            }
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties,
        };

        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        return schema;
    }

    /// <summary>The schema for one property's type.</summary>
    private static JsonObject Property(
        PropertyInfo property, IReadOnlyDictionary<string, string> documentation, IDictionary<string, JsonNode> definitions)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        // The one property that reads a name or an object: say both, or a document
        // written from the schema could only ever use one of the spellings.
        if (type == typeof(WaveformDocument))
        {
            return new JsonObject
            {
                ["anyOf"] = new JsonArray(
                    new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray([.. WaveformDocument.Names.Select(v => (JsonNode)v)]),
                    },
                    ForType(type, documentation, definitions)),
            };
        }

        return ForType(type, documentation, definitions);
    }

    private static JsonObject ForType(
        Type type, IReadOnlyDictionary<string, string> documentation, IDictionary<string, JsonNode> definitions)
    {
        if (type == typeof(string))
        {
            return new JsonObject { ["type"] = "string" };
        }

        if (type == typeof(bool))
        {
            return new JsonObject { ["type"] = "boolean" };
        }

        if (type == typeof(int) || type == typeof(long))
        {
            return new JsonObject { ["type"] = "integer" };
        }

        if (type == typeof(double) || type == typeof(float) || type == typeof(decimal))
        {
            return new JsonObject { ["type"] = "number" };
        }

        // A dictionary keyed by name: the parameter surface, and nothing else so
        // far. Its values get the same treatment as anything else.
        if (Dictionary(type) is { } valueType)
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = ForType(valueType, documentation, definitions),
            };
        }

        if (Enumerable(type) is { } elementType)
        {
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = ForType(elementType, documentation, definitions),
            };
        }

        // Anything else in this graph is one of the document records. Emitting it
        // once into $defs and referring to it keeps the schema readable and stops
        // a self-referential type from recursing forever.
        var name = type.Name;

        if (!definitions.ContainsKey(name))
        {
            // Reserve the slot before recursing, or a type that reaches itself
            // would be described twice.
            definitions[name] = new JsonObject();
            var described = Describe(type, documentation, definitions);

            if (documentation.TryGetValue($"T:{type.FullName}", out var text))
            {
                described["description"] = text;
            }

            definitions[name] = described;
        }

        return new JsonObject { ["$ref"] = $"#/$defs/{name}" };
    }

    /// <summary>
    /// Properties in declaration order.
    /// </summary>
    /// <remarks>
    /// CLI-5 asks for deterministic output ordering, and reflection does not
    /// promise any. Declaration order is used rather than alphabetical because the
    /// document types are written in the order a person fills a model in, and a
    /// schema that reads the same way is easier to work from.
    /// </remarks>
    private static IEnumerable<PropertyInfo> Ordered(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0)
            .Where(p => p.Name != "EqualityContract")
            .OrderBy(p => p.MetadataToken);

    private static Type? Dictionary(Type type)
    {
        foreach (var candidate in Interfaces(type))
        {
            if (candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)
                && candidate.GetGenericArguments()[0] == typeof(string))
            {
                return candidate.GetGenericArguments()[1];
            }
        }

        return null;
    }

    private static Type? Enumerable(Type type)
    {
        if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type))
        {
            return null;
        }

        foreach (var candidate in Interfaces(type))
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return candidate.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static Type[] Interfaces(Type type) =>
        type.IsInterface ? [type, .. type.GetInterfaces()] : type.GetInterfaces();

    private static string Key(PropertyInfo property) =>
        $"P:{property.DeclaringType!.FullName}.{property.Name}";

    /// <summary>
    /// Reads the XML documentation file the build produces beside the assembly.
    /// </summary>
    /// <remarks>
    /// The summaries are reflowed onto one line. They are written as prose wrapped
    /// to a source column width, and a schema is read by machines and by people in
    /// editors, neither of which wants the original line breaks.
    /// </remarks>
    private static Dictionary<string, string> LoadDocumentation(Assembly assembly)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        var path = Path.ChangeExtension(assembly.Location, ".xml");

        if (string.IsNullOrEmpty(assembly.Location) || !File.Exists(path))
        {
            return found;
        }

        XDocument xml;

        try
        {
            xml = XDocument.Load(path);
        }
        catch (System.Xml.XmlException)
        {
            return found;
        }

        foreach (var member in xml.Descendants("member"))
        {
            var name = member.Attribute("name")?.Value;
            var summary = member.Element("summary");

            if (name is null || summary is null)
            {
                continue;
            }

            var reflowed = Reflow(Render(summary));

            if (reflowed.Length > 0)
            {
                found[name] = reflowed;
            }
        }

        return found;
    }

    /// <summary>
    /// Flattens a documentation element to text, keeping what the cross-reference
    /// tags name.
    /// </summary>
    /// <remarks>
    /// Taking an element's Value concatenates its text nodes and drops everything
    /// an empty element carried in its attributes - so a summary reading "See
    /// <c>&lt;see cref="FiguresOfMerit"/&gt;</c>" comes out as "See ." That is not
    /// a missing description, which is worse: it is a description that has been
    /// silently hollowed out, and it reads as one the author wrote badly.
    /// </remarks>
    private static string Render(XElement element)
    {
        var builder = new StringBuilder();

        foreach (var node in element.DescendantNodes())
        {
            if (node is XText text)
            {
                builder.Append(text.Value);
                continue;
            }

            if (node is not XElement child || child.HasElements || !child.IsEmpty)
            {
                // A non-empty element's text arrives through its own text nodes.
                continue;
            }

            // cref for a type or member, langword for a keyword, and name for a
            // paramref or typeparamref. The last one was missing, and it is the one
            // the model format uses most: every quantity's description says its
            // magnitude is "expressed in <paramref name="Unit"/>", which rendered as
            // "expressed in ." - the hollowed-out sentence this method's own remarks
            // warn about, in the one place that tells an author what a unit is for.
            //
            // Two independent agents attempting the acceptance suite reported it
            // before any test did, which is the argument for running that suite.
            var reference = child.Attribute("cref")?.Value
                ?? child.Attribute("langword")?.Value
                ?? child.Attribute("name")?.Value;

            if (reference is null)
            {
                continue;
            }

            // "T:Einzel.Commands.FiguresOfMerit" is a compiler-generated
            // identifier; the last segment is the part a reader wants.
            var trimmed = reference.Contains(':', StringComparison.Ordinal)
                ? reference[(reference.IndexOf(':', StringComparison.Ordinal) + 1)..]
                : reference;

            var lastDot = trimmed.LastIndexOf('.');
            builder.Append(lastDot >= 0 ? trimmed[(lastDot + 1)..] : trimmed);
        }

        return builder.ToString();
    }

    private static string Reflow(string text)
    {
        var builder = new StringBuilder(text.Length);
        var space = true;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!space)
                {
                    builder.Append(' ');
                    space = true;
                }

                continue;
            }

            builder.Append(character);
            space = false;
        }

        return builder.ToString().Trim();
    }
}
