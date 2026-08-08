#!/usr/bin/env dotnet
// create-backlog.cs
// .NET 10 file-based program — create an Azure DevOps backlog from backlog_input.json.
//
// Built-in libraries only (HttpClient + System.Text.Json). No Azure DevOps SDK, no NuGet.
//
// If an item carries an "estimate.task" object, a child Task (holding the hour estimate in
// Original/Remaining Work) is created under that item. See references/data-contracts.md.
//
// Run (PowerShell):
//   az login                                   # Entra token (recommended)
//   $env:AZDO_ORG     = "Cartagena365"         # optional; overrides org in the JSON
//   $env:AZDO_PROJECT = "GlassHull"            # optional; overrides project in the JSON
//   # DRY RUN (validates everything, creates NOTHING) — this is the default:
//   $env:AZDO_DRY_RUN = "true"
//   dotnet run "<plugin>/scripts/create-backlog.cs" -- "<path>/backlog_input.json"
//   # REAL RUN (creates parent + children, links them):
//   $env:AZDO_DRY_RUN = "false"
//   dotnet run "<plugin>/scripts/create-backlog.cs" -- "<path>/backlog_input.json"
//
// Optional env:
//   AZDO_PAT          PAT (Work Items Read & Write) instead of the az token
//   AZDO_AREA_PATH    set System.AreaPath on every created item
//   AZDO_ASSIGNED_TO  assign every created item to this user (UPN/email),
//                     unless the item already sets its own System.AssignedTo
//   BACKLOG_INPUT     input path (alternative to the CLI arg)
//   BACKLOG_RESULT    output path (default: ./backlog_result.json)

#:property JsonSerializerIsReflectionEnabledByDefault=true

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

const string ApiVersion = "7.1";

// ---- 1) config + input ------------------------------------------------------
string inputPath = args.Length > 0 ? args[0]
    : Environment.GetEnvironmentVariable("BACKLOG_INPUT")
      ?? Path.Combine(Directory.GetCurrentDirectory(), "backlog_input.json");
if (!File.Exists(inputPath))
    throw new FileNotFoundException($"input not found: {inputPath}");

using var inputDoc = JsonDocument.Parse(await File.ReadAllTextAsync(inputPath));
var root = inputDoc.RootElement;

string org = Environment.GetEnvironmentVariable("AZDO_ORG")
    ?? (root.TryGetProperty("org", out var o) ? o.GetString() : null)
    ?? throw new InvalidOperationException("no org: set AZDO_ORG or include \"org\" in the JSON");
string project = Environment.GetEnvironmentVariable("AZDO_PROJECT")
    ?? (root.TryGetProperty("project", out var p) ? p.GetString() : null)
    ?? throw new InvalidOperationException("no project: set AZDO_PROJECT or include \"project\" in the JSON");
string? pat = Environment.GetEnvironmentVariable("AZDO_PAT");
string? areaPath = Environment.GetEnvironmentVariable("AZDO_AREA_PATH");
string? assignedTo = Environment.GetEnvironmentVariable("AZDO_ASSIGNED_TO");
bool dryRun = !string.Equals(
    Environment.GetEnvironmentVariable("AZDO_DRY_RUN"), "false",
    StringComparison.OrdinalIgnoreCase); // default = DRY RUN unless explicitly "false"

string baseUrl = $"https://dev.azure.com/{org}";
Console.WriteLine($"org={org}  project={project}  mode={(dryRun ? "DRY RUN (validateOnly)" : "REAL - will create work items")}");

bool hasParent = root.TryGetProperty("parent", out var parentNode)
                 && parentNode.ValueKind == JsonValueKind.Object;

// ---- 2) http + auth ---------------------------------------------------------
using var http = new HttpClient();
if (!string.IsNullOrEmpty(pat))
{
    string basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
    Console.WriteLine("auth: PAT (Basic)");
}
else
{
    string token = await GetEntraTokenAsync();
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    Console.WriteLine("auth: Entra token via az (Bearer)");
}

// ---- 3) DRY RUN: validate parent + items, create nothing --------------------
if (dryRun)
{
    int ok = 0, fail = 0;
    if (hasParent)
    {
        Console.WriteLine("\n--- validating parent ---");
        if (await ValidateOnly(parentNode, "parent")) ok++; else fail++;
    }
    Console.WriteLine("\n--- validating items ---");
    foreach (var item in root.GetProperty("items").EnumerateArray())
    {
        if (await ValidateOnly(item, GetKey(item))) ok++; else fail++;
        if (TryGetTaskFields(item, out var tf))
            { if (await ValidateFields("Task", tf, GetKey(item) + " task")) ok++; else fail++; }
    }
    Console.WriteLine($"\nDRY RUN done. valid={ok} invalid={fail}. No work items were created.");
    return;
}

// ---- 4) REAL RUN ------------------------------------------------------------
string? parentUrl = null;
int? parentId = null;
if (hasParent)
{
    Console.WriteLine("\n--- creating parent ---");
    (int pid, string purl) = await CreateWorkItem(parentNode.GetProperty("type").GetString()!,
                                                  parentNode.GetProperty("fields"), parentUrl: null);
    parentId = pid; parentUrl = purl;
    Console.WriteLine($"parent #{pid} created.");
}

var results = new List<object>();
foreach (var item in root.GetProperty("items").EnumerateArray())
{
    string key = GetKey(item);
    string type = item.GetProperty("type").GetString()!;
    try
    {
        (int id, string url) = await CreateWorkItem(type, item.GetProperty("fields"), parentUrl);
        string title = item.GetProperty("fields").GetProperty("System.Title").GetString() ?? "";
        Console.WriteLine($"  key {key,-5} -> {type,-13} #{id}");

        int? taskId = null; string? taskUrl = null;
        if (TryGetTaskFields(item, out var tf))
        {
            try
            {
                (int tid, string turl) = await CreateWorkItem("Task", tf, url); // child of this item
                taskId = tid; taskUrl = turl;
                Console.WriteLine($"           +- Task #{tid} (estimate)");
            }
            catch (Exception tex) { Console.WriteLine($"           +- Task FAILED: {tex.Message}"); }
        }
        results.Add(new { key, id, url, type, title, taskId, taskUrl });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  key {key,-5} -> FAILED: {ex.Message}");
        results.Add(new { key, id = (int?)null, url = (string?)null, type, error = ex.Message });
    }
}

string resultPath = Environment.GetEnvironmentVariable("BACKLOG_RESULT")
    ?? Path.Combine(Directory.GetCurrentDirectory(), "backlog_result.json");
var outObj = new
{
    org,
    project,
    parent = parentId is null ? null : new { id = parentId, url = parentUrl },
    items = results
};
await File.WriteAllTextAsync(resultPath,
    JsonSerializer.Serialize(outObj, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"\nWrote {resultPath}. Next: tracking.py writeback to fill the source's tracking columns.");

// ---- helpers ----------------------------------------------------------------

static string GetKey(JsonElement item)
{
    if (!item.TryGetProperty("key", out var k)) return "";
    return k.ValueKind == JsonValueKind.String ? (k.GetString() ?? "") : k.GetRawText();
}

// An item may carry estimate.task.fields — the child Task that holds the hour estimate.
static bool TryGetTaskFields(JsonElement item, out JsonElement fields)
{
    fields = default;
    return item.TryGetProperty("estimate", out var est) && est.ValueKind == JsonValueKind.Object
        && est.TryGetProperty("task", out var task) && task.ValueKind == JsonValueKind.Object
        && task.TryGetProperty("fields", out fields);
}

// Build a JSON-Patch body from a "fields" object; optionally add area path, assignee, parent link.
static StringContent BuildPatch(JsonElement fields, string? parentUrl, string? areaPath, string? assignedTo)
{
    var ops = new List<object>();
    bool hasAssigned = false;
    foreach (var f in fields.EnumerateObject())
    {
        if (f.Name == "System.AssignedTo") hasAssigned = true;
        object value = f.Value.ValueKind == JsonValueKind.Number
            ? (f.Value.TryGetInt32(out int iv) ? iv : f.Value.GetDouble())
            : f.Value.GetString() ?? "";
        ops.Add(new { op = "add", path = $"/fields/{f.Name}", value });
    }
    if (!string.IsNullOrEmpty(areaPath))
        ops.Add(new { op = "add", path = "/fields/System.AreaPath", value = areaPath });
    if (!string.IsNullOrEmpty(assignedTo) && !hasAssigned)
        ops.Add(new { op = "add", path = "/fields/System.AssignedTo", value = assignedTo });
    if (!string.IsNullOrEmpty(parentUrl))
        ops.Add(new
        {
            op = "add",
            path = "/relations/-",
            value = new { rel = "System.LinkTypes.Hierarchy-Reverse", url = parentUrl }
        });
    return new StringContent(JsonSerializer.Serialize(ops), Encoding.UTF8, "application/json-patch+json");
}

async Task<(int id, string url)> CreateWorkItem(string type, JsonElement fields, string? parentUrl)
{
    string uri = $"{baseUrl}/{project}/_apis/wit/workitems/${Uri.EscapeDataString(type)}?api-version={ApiVersion}";
    var resp = await http.PatchAsync(uri, BuildPatch(fields, parentUrl, areaPath, assignedTo));
    string body = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode)
        throw new HttpRequestException($"create {type} failed ({(int)resp.StatusCode}): {body}");
    using var doc = JsonDocument.Parse(body);
    return (doc.RootElement.GetProperty("id").GetInt32(), doc.RootElement.GetProperty("url").GetString()!);
}

async Task<bool> ValidateOnly(JsonElement node, string tag)
    => await ValidateFields(node.GetProperty("type").GetString()!, node.GetProperty("fields"), tag);

async Task<bool> ValidateFields(string type, JsonElement fields, string tag)
{
    string title = fields.TryGetProperty("System.Title", out var t) ? (t.GetString() ?? "") : "";
    string uri = $"{baseUrl}/{project}/_apis/wit/workitems/${Uri.EscapeDataString(type)}?validateOnly=true&api-version={ApiVersion}";
    var resp = await http.PatchAsync(uri, BuildPatch(fields, parentUrl: null, areaPath, assignedTo));
    string body = await resp.Content.ReadAsStringAsync();
    if (resp.IsSuccessStatusCode)
    {
        Console.WriteLine($"  PASS  {tag,-9} {type,-13} {Trunc(title, 54)}");
        return true;
    }
    Console.WriteLine($"  FAIL  {tag,-9} {type,-13} ({(int)resp.StatusCode}) {Trunc(body, 140)}");
    return false;
}

static async Task<string> GetEntraTokenAsync()
{
    const string adoResourceId = "499b84ac-1321-427f-aa17-267ca6975798"; // Azure DevOps (global constant)
    var psi = new ProcessStartInfo
    {
        FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "az",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    string azArgs = $"account get-access-token --resource {adoResourceId} --query accessToken -o tsv";
    psi.Arguments = OperatingSystem.IsWindows() ? $"/c az {azArgs}" : azArgs;

    using var proc = Process.Start(psi)
        ?? throw new InvalidOperationException("could not start az - is Azure CLI installed and 'az login' done?");
    string token = (await proc.StandardOutput.ReadToEndAsync()).Trim();
    string err = await proc.StandardError.ReadToEndAsync();
    await proc.WaitForExitAsync();
    if (proc.ExitCode != 0 || string.IsNullOrEmpty(token))
        throw new InvalidOperationException($"failed to get Entra token (az exit {proc.ExitCode}). Try 'az login'. {err.Trim()}");
    return token;
}

static string Trunc(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "...";
