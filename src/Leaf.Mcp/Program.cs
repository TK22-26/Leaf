using Leaf.Composition;
using Leaf.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// HARD RULE for this project: stdout is the MCP JSON-RPC channel. No
// Console.Write* anywhere; framework logging goes to stderr below and
// Leaf's own Log writes to %LOCALAPPDATA%\Leaf\leaf.log only.
// LEAF_MCP_VERBOSE=1 turns on per-command git logging for diagnosing a
// headless server without a debugger attached.
Leaf.Services.Log.Init(Environment.GetEnvironmentVariable("LEAF_MCP_VERBOSE") == "1"
    ? Leaf.Services.LogLevel.Verbose
    : Leaf.Services.LogLevel.Normal);

// Keep the JSON-RPC pipes out of child git processes — must run before
// the transport starts reading stdin. See StdioHandleGuard for the
// msys-git deadlock this prevents.
StdioHandleGuard.MakeStdHandlesNonInheritable();

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddLeafHeadlessGitServices();
builder.Services.AddSingleton<RepoResolver>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<LeafTools>();

await builder.Build().RunAsync();
