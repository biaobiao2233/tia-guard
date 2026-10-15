using System.Text.Json;
using TwinCat3.Contracts;
using TwinCat3.Engineering;
using TwinCat3.EnvironmentHost;
using TiaGuard.TwinCat.Validation;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native harness requires Windows.");
string Option(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : throw new ArgumentException("Missing " + name);
}
var workspace = Path.GetFullPath(Option("--workspace"));
var source = Path.GetFullPath(Option("--source"));
var output = Path.GetFullPath(Option("--output"));
var reportPath = Path.GetFullPath(Option("--report"));
var allocation = new EngineeringAllocation(workspace, Path.Combine(workspace, "scratch"));
var input = GenerationInputLoader.Load(allocation.CheckInput(source));
var environment = new WindowsEngineeringEnvironment(Option("--interop"));
object result;
try
{
    await using var session = await environment.CreateOwnedSessionAsync(input.Manifest.Environment, workspace);
    var authorizations = new StageAuthorizationRegistry();
    var pipeline = new EngineeringPipeline(allocation, authorizations, new ExecutionGrantStore(writesEnabled: true),
        new NativeEngineeringCreator(allocation, authorizations), new NativeEngineeringCompiler(TimeSpan.FromMinutes(3)),
        new NativeProjectCaptureService(), new SourceBoundProjectComparer());
    var request = new GenerationRequest { SourceDirectory = source, OutputDirectory = output };
    var preview = pipeline.Preview(request, session);
    var grant = pipeline.IssueGrant(preview.PreviewId, session.SessionId);
    var generation = await pipeline.BuildAsync(request, preview.PreviewId, grant, session);
    result = generation;
}
catch (Exception error)
{
    result = new { success = false, phase = "session", error = error.GetType().Name, error.Message };
}
// The report is emitted only after disposal; failed cleanup can never look like success.
var json = JsonSerializer.Serialize(result, ContractJson.Options);
await File.WriteAllTextAsync(reportPath, json);
Console.WriteLine(JsonSerializer.Serialize(new { reportPath, success = result is GenerationResult { Success: true }, phase = (result as GenerationResult)?.Phase }, ContractJson.Options));
return result is GenerationResult { Success: true } ? 0 : 5;
