using TiaGuard.Bridge.Host;
using Xunit;

namespace TiaGuard.Bridge.Host.Tests;

public sealed class BridgeWriteSafetyServiceTests
{
    private const string Operation = "upsert_tag";
    private const string Target = "project|C:/demo.ap21|offline-copy";
    private const string State = """{"Exists":false}""";

    [Fact]
    public void Token_is_single_use()
    {
        var safety = new BridgeWriteSafetyService();
        var request = Request("%M10.0");
        var json = BridgeWriteSafetyService.SerializeRequest(request);
        var preview = safety.CreatePreview(
            Operation, "test", Target, json, State);

        safety.ValidateAndConsume(
            preview.SafetyToken, Operation, Target, json, State);

        Assert.Throws<InvalidOperationException>(() =>
            safety.ValidateAndConsume(
                preview.SafetyToken, Operation, Target, json, State));
    }

    [Fact]
    public void Request_mismatch_is_rejected_and_consumes_token()
    {
        var safety = new BridgeWriteSafetyService();
        var expected = BridgeWriteSafetyService.SerializeRequest(Request("%M10.0"));
        var changed = BridgeWriteSafetyService.SerializeRequest(Request("%M10.1"));
        var preview = safety.CreatePreview(
            Operation, "test", Target, expected, State);

        Assert.Throws<InvalidOperationException>(() =>
            safety.ValidateAndConsume(
                preview.SafetyToken, Operation, Target, changed, State));

        Assert.Throws<InvalidOperationException>(() =>
            safety.ValidateAndConsume(
                preview.SafetyToken, Operation, Target, expected, State));
    }

    [Fact]
    public void Current_state_mismatch_is_rejected()
    {
        var safety = new BridgeWriteSafetyService();
        var json = BridgeWriteSafetyService.SerializeRequest(Request("%M10.0"));
        var preview = safety.CreatePreview(
            Operation, "test", Target, json, State);

        Assert.Throws<InvalidOperationException>(() =>
            safety.ValidateAndConsume(
                preview.SafetyToken,
                Operation,
                Target,
                json,
                """{"Exists":true}"""));
    }

    [Fact]
    public void Binding_mismatch_is_rejected()
    {
        var safety = new BridgeWriteSafetyService();
        var json = BridgeWriteSafetyService.SerializeRequest(Request("%M10.0"));
        var preview = safety.CreatePreview(
            Operation, "test", Target, json, State);

        Assert.Throws<InvalidOperationException>(() =>
            safety.ValidateAndConsume(
                preview.SafetyToken,
                Operation,
                "project|C:/other.ap21|offline-copy",
                json,
                State));
    }

    [Fact]
    public void Expired_token_is_rejected()
    {
        var safety = new BridgeWriteSafetyService(TimeSpan.FromMilliseconds(-1));
        var json = BridgeWriteSafetyService.SerializeRequest(Request("%M10.0"));
        var preview = safety.CreatePreview(
            Operation, "test", Target, json, State);

        Assert.Throws<InvalidOperationException>(() =>
            safety.ValidateAndConsume(
                preview.SafetyToken, Operation, Target, json, State));
    }

    [Theory]
    [InlineData("", "Tag", "Bool")]
    [InlineData("Table/Child", "Tag", "Bool")]
    [InlineData("Table", "Tag/Child", "Bool")]
    [InlineData("Table", "Tag", "")]
    public void Request_validation_rejects_unsafe_or_missing_names(
        string table,
        string tag,
        string dataType)
    {
        var request = new TagUpsertRequest
        {
            TableName = table,
            TagName = tag,
            DataType = dataType,
            LogicalAddress = "%M10.0"
        };

        Assert.Throws<ArgumentException>(() => request.Validate());
    }

    [Fact]
    public void Request_hash_is_deterministic()
    {
        var one = BridgeWriteSafetyService.SerializeRequest(Request("%M10.0"));
        var two = BridgeWriteSafetyService.SerializeRequest(Request("%M10.0"));

        Assert.Equal(one, two);
        Assert.Equal(
            BridgeWriteSafetyService.Hash(one),
            BridgeWriteSafetyService.Hash(two));
    }


    [Fact]
    public void Publish_preview_is_marked_as_saving_project()
    {
        var safety = new BridgeWriteSafetyService();
        var request = new ProjectPublishRequest
        {
            OutputDirectory = Path.GetTempPath(),
            OutputName = "tia-guard-bridge-publish-test-" + Guid.NewGuid().ToString("N")
        };
        request.Validate();
        var json = BridgeWriteSafetyService.SerializeRequest(request);

        var preview = safety.CreatePreview(
            "publish_offline_copy",
            "test",
            Target,
            json,
            "content-id",
            savesProject: true);

        Assert.True(preview.SavesProject);
        safety.ValidateAndConsume(
            preview.SafetyToken,
            "publish_offline_copy",
            Target,
            json,
            "content-id");
    }

    [Fact]
    public void Publish_request_refuses_existing_destination()
    {
        var parent = Path.Combine(
            Path.GetTempPath(),
            "tia-guard-bridge-publish-parent-" + Guid.NewGuid().ToString("N"));
        var child = "existing";
        Directory.CreateDirectory(Path.Combine(parent, child));
        try
        {
            var request = new ProjectPublishRequest
            {
                OutputDirectory = parent,
                OutputName = child
            };

            Assert.Throws<InvalidOperationException>(() => request.Validate());
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }


    [Fact]
    public void Publish_request_requires_preserved_project_name()
    {
        var request = new ProjectPublishRequest
        {
            OutputDirectory = Path.GetTempPath(),
            OutputName = "MotorProject"
        };

        request.ValidateProjectName("MotorProject");

        Assert.Throws<InvalidOperationException>(() =>
            request.ValidateProjectName("DifferentProject"));
    }

    private static TagUpsertRequest Request(string address) => new()
    {
        TableName = "Default",
        TagName = "BridgeTest",
        DataType = "Bool",
        LogicalAddress = address
    };
}
