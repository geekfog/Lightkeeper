using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lightkeeper.Functions.Models;

namespace Lightkeeper.Functions.Tests.TestSupport;

/// <summary>Loads the payloads in the repository's samples folder, the same files used for manual verification.</summary>
internal static class Samples
{
    public const string BuildCompleted = "build.complete.json";
    public const string BuildCompletedMinimal = "build.complete.minimal.json";
    public const string PullRequestCreated = "git.pullrequest.created.json";
    public const string PullRequestUpdated = "git.pullrequest.updated.json";

    public static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", name));

    public static JsonObject ReadNode(string name) => JsonNode.Parse(Read(name))!.AsObject();

    public static ServiceHookEvent ReadEvent(string name) => Parse(ReadNode(name));

    public static ServiceHookEvent Parse(JsonNode node) => node.Deserialize<ServiceHookEvent>(LightkeeperJson.Options)!;

    public static MemoryStream ToStream(string json) => new(Encoding.UTF8.GetBytes(json));

    public static MemoryStream ToStream(JsonNode node) => ToStream(node.ToJsonString());
}
