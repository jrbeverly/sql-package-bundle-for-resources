using System.Text.Json;
using System.Text.Json.Nodes;
using Forge.Contracts;

namespace Forge.Catalog;

// The trust store: publishers keyed by public key, persisted as trust.json at
// the state root (SPEC.md, Trust Model; TECHNICAL.md, State On Disk).
// Trust is an explicit operator act: nothing writes an entry except Trust(),
// and verification never does.
public sealed class TrustStore
{
    private readonly string stateRoot;
    private readonly Dictionary<string, TrustEntry> publishers;

    private TrustStore(string stateRoot, Dictionary<string, TrustEntry> publishers)
    {
        this.stateRoot = stateRoot;
        this.publishers = publishers;
    }

    // An absent trust.json is a fresh store, not an error.
    public static TrustStore Load(string stateRoot)
    {
        var path = Path.Combine(stateRoot, "trust.json");
        if (!File.Exists(path))
        {
            return new TrustStore(stateRoot, new Dictionary<string, TrustEntry>(StringComparer.Ordinal));
        }

        JsonNode node;
        try
        {
            node = JsonNode.Parse(File.ReadAllText(path))!;
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"Trust store at {path} is not valid JSON: {e.Message}");
        }

        if (node is not JsonObject root || root["publishers"] is not JsonObject publishersNode)
        {
            throw new InvalidDataException($"Trust store at {path} has no publishers object.");
        }

        var publishers = new Dictionary<string, TrustEntry>(StringComparer.Ordinal);
        foreach (var (key, entryNode) in publishersNode)
        {
            if (entryNode is not JsonObject entry
                || entry["algorithm"] is not JsonValue algorithm
                || !algorithm.TryGetValue<string>(out var algorithmName)
                || entry["displayName"] is not JsonValue displayName
                || !displayName.TryGetValue<string>(out var name)
                || entry["grantedAt"] is not JsonValue grantedAt
                || !grantedAt.TryGetValue<string>(out var grantedAtText)
                || !JsonTime.TryParse(grantedAtText, out var grantedAtValue))
            {
                throw new InvalidDataException($"Trust store at {path} has a malformed entry for publisher key {key}.");
            }

            publishers[key] = new TrustEntry(key, algorithmName, name, grantedAtValue);
        }

        return new TrustStore(stateRoot, publishers);
    }

    public bool IsTrusted(string publicKeyBase64) => publishers.ContainsKey(publicKeyBase64);

    // The explicit trust act. The publisher record — display name and full
    // key — is what the operator must be shown before this is called
    // (SPEC.md, Trust Model); the caller renders it, the store records it.
    public TrustEntry Trust(Publisher publisher)
    {
        var entry = new TrustEntry(publisher.PublicKey.Key, publisher.PublicKey.Type, publisher.DisplayName, DateTimeOffset.UtcNow);
        publishers[entry.Key] = entry;
        Save();
        return entry;
    }

    private void Save()
    {
        var root = new JsonObject
        {
            ["publishers"] = new JsonObject(publishers.ToDictionary(
                pair => pair.Key,
                pair => (JsonNode?)new JsonObject
                {
                    ["algorithm"] = pair.Value.Algorithm,
                    ["displayName"] = pair.Value.DisplayName,
                    ["grantedAt"] = JsonTime.Format(pair.Value.GrantedAt),
                })),
        };

        var path = Path.Combine(stateRoot, "trust.json");
        Directory.CreateDirectory(stateRoot);
        var temp = path + ".tmp";
        File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, overwrite: true);
    }
}
