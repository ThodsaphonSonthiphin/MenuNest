#!/usr/bin/env dotnet
// my-work.cs
// .NET 10 file-based program — list the Azure DevOps work items assigned to you as a
// table, grouped by project, OPEN work first, sorted by state then priority. The ticket
// number is a clickable terminal hyperlink (opens the work item in the browser).
// Built-in libraries only (HttpClient + System.Text.Json) — no SDK, no NuGet packages.
//
// Run (PowerShell):
//   az login
//   $env:AZDO_ORG = "Cartagena365"
//   dotnet run "${CLAUDE_PLUGIN_ROOT}/scripts/my-work.cs"
//
// Options:
//   $env:AZDO_PAT            = "..."     # use a PAT (Work Items = Read) instead of the az token
//   $env:AZDO_INCLUDE_CLOSED = "true"    # also include Closed/Removed
//   $env:AZDO_SHOW_DONE      = "true"    # add a table of completed items (default: just a count)
//   $env:NO_COLOR            = "1"        # disable colors + clickable links
//
// Colors and clickable links are emitted only when output is a real terminal (auto-off when piped).

#:property JsonSerializerIsReflectionEnabledByDefault=true

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

// ── 1) config ────────────────────────────────────────────────────────────────
string org = Environment.GetEnvironmentVariable("AZDO_ORG")
    ?? throw new InvalidOperationException("AZDO_ORG is not set (the organization name)");
string? pat = Environment.GetEnvironmentVariable("AZDO_PAT");
bool includeClosed = Eq(Environment.GetEnvironmentVariable("AZDO_INCLUDE_CLOSED"), "true");
bool showDone = Eq(Environment.GetEnvironmentVariable("AZDO_SHOW_DONE"), "true");
bool rich = !Console.IsOutputRedirected
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
try { Console.OutputEncoding = Encoding.UTF8; } catch { /* ignore */ }

const string ApiVersion = "7.1";
string baseUrl = $"https://dev.azure.com/{org}";

var doneStates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "Done", "Closed", "Resolved", "Removed", "Completed", "Inactive" };

// ── 2) http + auth ────────────────────────────────────────────────────────────
using var http = new HttpClient();
if (!string.IsNullOrEmpty(pat))
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
        "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}")));
else
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
        "Bearer", await GetEntraTokenAsync());

// ── 3) WIQL: ids assigned to me ───────────────────────────────────────────────
string stateFilter = includeClosed ? "" : " AND [System.State] <> 'Closed' AND [System.State] <> 'Removed'";
string wiql = "SELECT [System.Id] FROM WorkItems WHERE [System.AssignedTo] = @Me" + stateFilter +
              " ORDER BY [System.ChangedDate] DESC";
var wiqlResp = await http.PostAsync($"{baseUrl}/_apis/wit/wiql?api-version={ApiVersion}",
    new StringContent(JsonSerializer.Serialize(new { query = wiql }), Encoding.UTF8, "application/json"));
await EnsureOk(wiqlResp, "WIQL query");

using var wiqlDoc = JsonDocument.Parse(await wiqlResp.Content.ReadAsStringAsync());
int[] ids = wiqlDoc.RootElement.GetProperty("workItems").EnumerateArray()
    .Select(w => w.GetProperty("id").GetInt32()).ToArray();

if (ids.Length == 0) { Console.WriteLine("No work items are assigned to you. 🎉"); return; }

// ── 4) fetch fields in batches (max 200/req) ──────────────────────────────────
string[] fields =
[
    "System.Id", "System.WorkItemType", "System.Title", "System.State",
    "System.TeamProject", "Microsoft.VSTS.Common.Priority"
];
var rows = new List<Item>();
foreach (int[] chunk in ids.Chunk(200))
{
    var resp = await http.PostAsync($"{baseUrl}/_apis/wit/workitemsbatch?api-version={ApiVersion}",
        new StringContent(JsonSerializer.Serialize(new { ids = chunk, fields, errorPolicy = "Omit" }),
                          Encoding.UTF8, "application/json"));
    await EnsureOk(resp, "workitemsbatch");
    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    foreach (var wi in doc.RootElement.GetProperty("value").EnumerateArray())
    {
        var f = wi.GetProperty("fields");
        rows.Add(new Item(
            wi.GetProperty("id").GetInt32(),
            GetStr(f, "System.WorkItemType"),
            GetStr(f, "System.Title"),
            GetStr(f, "System.State"),
            GetStr(f, "System.TeamProject"),
            GetInt(f, "Microsoft.VSTS.Common.Priority")));
    }
}

// ── 5) render ─────────────────────────────────────────────────────────────────
var open = rows.Where(r => !doneStates.Contains(r.State)).ToList();
var done = rows.Where(r => doneStates.Contains(r.State)).ToList();

string ttl = $" My Work — {org} ";
string dt = $" {DateTime.Now:yyyy-MM-dd} ";
int hw = Math.Max(ttl.Length + dt.Length, 58);
Console.WriteLine();
Console.WriteLine(Paint("1;36", "┌" + new string('─', hw) + "┐"));
Console.WriteLine(Paint("1;36", "│") + Bold(ttl) + new string(' ', hw - ttl.Length - dt.Length) + Dim(dt) + Paint("1;36", "│"));
Console.WriteLine(Paint("1;36", "└" + new string('─', hw) + "┘"));
Console.WriteLine($"  {Bold(rows.Count.ToString())} assigned   •   {Paint("33", open.Count + " open")}   •   {Dim(done.Count + " done")}");

if (open.Count == 0)
    Console.WriteLine("\n" + Paint("32", "  All clear — nothing open. 🎉"));

foreach (var grp in open.GroupBy(r => r.Project).OrderByDescending(g => g.Count()))
{
    int doneHere = done.Count(d => d.Project == grp.Key);
    Console.WriteLine();
    Console.WriteLine(Paint("1;37", $"▌ {grp.Key}") + "  "
        + Dim($"{grp.Count()} open" + (doneHere > 0 ? $" · {doneHere} done" : "")));
    var items = grp.OrderBy(r => StateRank(r.State)).ThenBy(r => r.Priority ?? 99).ThenBy(r => r.Id).ToList();
    PrintTable(items, baseUrl);
}

if (showDone && done.Count > 0)
{
    foreach (var grp in done.GroupBy(r => r.Project).OrderByDescending(g => g.Count()))
    {
        Console.WriteLine();
        Console.WriteLine(Dim($"▌ {grp.Key} — completed ({grp.Count()})"));
        PrintTable(grp.OrderBy(r => r.Id).ToList(), baseUrl);
    }
}
else if (done.Count > 0)
{
    Console.WriteLine();
    var byProj = done.GroupBy(d => d.Project).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}");
    Console.WriteLine(Dim($"  ✓ {done.Count} completed:  " + string.Join("  ·  ", byProj) + "   (AZDO_SHOW_DONE=true to list)"));
}

// ── render helpers ──────────────────────────────────────────────────────────────
void PrintTable(List<Item> items, string baseUrl)
{
    const int titleCap = 58;
    string state(Item i) => $"{StateIcon(i.State)} {i.State}";
    string pri(Item i) => i.Priority is int p ? $"P{p}" : "";

    int wId    = ColW("#",     items.Select(i => i.Id.ToString()));
    int wState = ColW("State", items.Select(state));
    int wPri   = ColW("P",     items.Select(pri));
    int wType  = ColW("Type",  items.Select(i => i.Type));
    int wTitle = Math.Min(titleCap, ColW("Title", items.Select(i => i.Title)));
    int[] ws = { wId, wState, wPri, wType, wTitle };

    Console.WriteLine(Dim(Border("┌", "┬", "┐", ws)));
    Console.WriteLine(RowLine(new[]
    {
        new Cell("#", Bold("#")), new Cell("State", Bold("State")), new Cell("P", Bold("P")),
        new Cell("Type", Bold("Type")), new Cell("Title", Bold("Title")),
    }, ws));
    Console.WriteLine(Dim(Border("├", "┼", "┤", ws)));

    foreach (var it in items)
    {
        string idS = it.Id.ToString();
        string url = $"{baseUrl}/{Uri.EscapeDataString(it.Project)}/_workitems/edit/{it.Id}";
        string st = state(it);
        string title = Trunc(it.Title, wTitle);
        var cells = new[]
        {
            new Cell(idS, rich ? Link(url, Paint("96", idS)) : idS),
            new Cell(st, Paint(StateColor(it.State), st)),
            new Cell(pri(it), Dim(pri(it))),
            new Cell(it.Type, Paint(TypeColor(it.Type), it.Type)),
            new Cell(title, title),
        };
        Console.WriteLine(RowLine(cells, ws));
    }
    Console.WriteLine(Dim(Border("└", "┴", "┘", ws)));
}

string Paint(string code, string s) => rich && s.Length > 0 ? $"\x1b[{code}m{s}\x1b[0m" : s;
string Bold(string s) => Paint("1", s);
string Dim(string s) => Paint("90", s);
string Link(string url, string text) => $"\x1b]8;;{url}\x1b\\{text}\x1b]8;;\x1b\\";

static string RowLine(Cell[] cells, int[] ws)
{
    var sb = new StringBuilder("│");
    for (int i = 0; i < cells.Length; i++)
        sb.Append(' ').Append(cells[i].Render)
          .Append(new string(' ', Math.Max(0, ws[i] - cells[i].Plain.Length))).Append(" │");
    return sb.ToString();
}

static string Border(string l, string m, string r, int[] ws)
    => l + string.Join(m, ws.Select(w => new string('─', w + 2))) + r;

static int ColW(string header, IEnumerable<string> vals)
    => Math.Max(header.Length, vals.DefaultIfEmpty("").Max(s => s.Length));

static int StateRank(string st) => st.ToLowerInvariant() switch
{
    "active" or "doing" or "in progress" or "committed" => 0,
    "new" or "to do" or "approved" or "open" or "proposed" => 1,
    "design" or "ready" => 2,
    _ => 3,
};
static string StateIcon(string st) => st.ToLowerInvariant() switch
{
    "active" or "doing" or "in progress" or "committed" => "▶",
    "new" or "to do" or "approved" or "open" or "proposed" => "○",
    "design" or "ready" => "◇",
    _ => "•",
};
static string StateColor(string st) => st.ToLowerInvariant() switch
{
    "active" or "doing" or "in progress" or "committed" => "32",
    "new" or "to do" or "approved" or "open" or "proposed" => "33",
    "design" or "ready" => "36",
    _ => "37",
};
static string TypeColor(string t) => t switch
{
    "Bug" => "31", "User Story" => "32", "Epic" or "Feature" => "35",
    "Task" => "34", "Issue" => "33", "Test Case" => "36", _ => "37",
};

static bool Eq(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

static string GetStr(JsonElement fields, string name)
{
    if (!fields.TryGetProperty(name, out var el)) return "";
    if (el.ValueKind == JsonValueKind.Object)
        return el.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "";
    return el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : el.ToString();
}
static int? GetInt(JsonElement fields, string name)
    => fields.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetInt32() : (int?)null;

static string Trunc(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

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
        ?? throw new InvalidOperationException("could not start az — is Azure CLI installed and 'az login' done?");
    string token = (await proc.StandardOutput.ReadToEndAsync()).Trim();
    string err = await proc.StandardError.ReadToEndAsync();
    await proc.WaitForExitAsync();
    if (proc.ExitCode != 0 || string.IsNullOrEmpty(token))
        throw new InvalidOperationException($"failed to get Entra token (az exit {proc.ExitCode}). Try 'az login'. {err.Trim()}");
    return token;
}

static async Task EnsureOk(HttpResponseMessage resp, string what)
{
    if (resp.IsSuccessStatusCode) return;
    throw new HttpRequestException($"{what} failed ({(int)resp.StatusCode} {resp.StatusCode}): {await resp.Content.ReadAsStringAsync()}");
}

record Cell(string Plain, string Render);
record Item(int Id, string Type, string Title, string State, string Project, int? Priority);
