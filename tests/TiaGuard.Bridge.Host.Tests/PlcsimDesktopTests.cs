using System.Text.Json;
using TiaGuard.Bridge.Host;
using TiaGuard.Bridge.Worker;
using Xunit;

public sealed class PlcsimDesktopTests
{
    [Fact] public void Delayed_power_readback_allows_cleanup_without_replaying_on()
    {
        Assert.Equal(PlcsimPowerDecision.Toggle, PlcsimUiIdentity.DecidePower(true, null, false));
        Assert.Equal(PlcsimPowerDecision.Wait, PlcsimUiIdentity.DecidePower(true, true, null));
        Assert.Equal(PlcsimPowerDecision.Wait, PlcsimUiIdentity.DecidePower(true, true, false));
        Assert.Equal(PlcsimPowerDecision.Toggle, PlcsimUiIdentity.DecidePower(false, true, true));
        Assert.Equal(PlcsimPowerDecision.Wait, PlcsimUiIdentity.DecidePower(false, false, null));
        Assert.Equal(PlcsimPowerDecision.Complete, PlcsimUiIdentity.DecidePower(false, false, false));
    }
    [Fact] public void Old_off_readback_cannot_clear_a_pending_on_request()
    {
        Assert.Throws<InvalidOperationException>(() => PlcsimUiIdentity.DecidePower(false, true, false));
        Assert.Throws<InvalidOperationException>(() => PlcsimUiIdentity.DecidePower(false, true, null));
        Assert.Throws<InvalidOperationException>(() => PlcsimUiIdentity.DecidePower(true, null, null));
        Assert.Equal(PlcsimPowerDecision.Complete, PlcsimUiIdentity.DecidePower(true, true, true));
    }
    private static readonly string Id = Guid.NewGuid().ToString("N");
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    private sealed class FakeGateway : IPlcsimDesktopGateway
    {
        public int Generation { get; set; } = 1;
        public List<string> Calls { get; } = [];
        public Func<string, object, JsonElement>? Reply { get; set; }
        public Task<JsonElement> CallAsync(string method, object payload, CancellationToken ct)
        {
            Calls.Add(method);
            return Task.FromResult(Reply?.Invoke(method, payload) ?? Json(new { success = true, sessionId = Id }));
        }
    }
    private static BridgePlcsimDesktopCoordinator Coordinator(FakeGateway gateway, bool enabled = true) => new(new() { Enabled = enabled }, gateway);

    [Fact] public void Status_does_not_launch_a_worker_or_application()
    {
        var gateway = new FakeGateway(); var status = Json(Coordinator(gateway, false).Status());
        Assert.False(status.GetProperty("enabled").GetBoolean()); Assert.Empty(gateway.Calls);
    }
    [Fact] public async Task Disabled_desktop_cannot_open_or_inspect()
    {
        var gateway = new FakeGateway(); var c = Coordinator(gateway, false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.OpenAsync(default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.InspectAsync(Id, default));
        Assert.Empty(gateway.Calls);
    }
    [Fact] public async Task Unknown_session_has_no_effect()
    {
        var gateway = new FakeGateway(); var c = Coordinator(gateway); await c.OpenAsync(default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.ActAsync(new() { SessionId = "wrong", Action = "delete" }, default));
        Assert.Single(gateway.Calls);
    }
    [Fact] public async Task Worker_restart_does_not_adopt_a_new_desktop()
    {
        var gateway = new FakeGateway(); var c = Coordinator(gateway); await c.OpenAsync(default); gateway.Generation++;
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.CloseAsync(Id, default));
        Assert.Single(gateway.Calls);
    }
    [Fact] public async Task Generation_change_during_action_invalidates_result()
    {
        var gateway = new FakeGateway(); var c = Coordinator(gateway); await c.OpenAsync(default);
        gateway.Reply = (_, _) => { gateway.Generation++; return Json(new { success = true }); };
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.InspectAsync(Id, default));
    }
    [Theory] [InlineData("power")] [InlineData("run")] [InlineData("script")] [InlineData("click")]
    public async Task Unqualified_actions_are_not_forwarded(string action)
    {
        var gateway = new FakeGateway(); var c = Coordinator(gateway); await c.OpenAsync(default);
        await Assert.ThrowsAsync<ArgumentException>(() => c.ActAsync(new() { SessionId = Id, Action = action }, default));
        Assert.Single(gateway.Calls);
    }
    [Theory] [InlineData(null)] [InlineData("UserInstance")] [InlineData("TiaGuard_bad\n")] [InlineData("TiaGuard_你好")]
    public async Task Invalid_names_are_rejected_before_worker_call(string? name)
    {
        var gateway = new FakeGateway(); var c = Coordinator(gateway); await c.OpenAsync(default);
        await Assert.ThrowsAsync<ArgumentException>(() => c.ActAsync(new() { SessionId = Id, Action = "rename", Name = name }, default));
        Assert.Single(gateway.Calls);
    }
    [Fact] public async Task Failed_open_preserves_cleanup_binding()
    {
        var gateway = new FakeGateway { Reply = (_, _) => Json(new { success = false, sessionId = Id, closeRequired = true }) };
        var c = Coordinator(gateway); await c.OpenAsync(default);
        Assert.Equal(Id, Json(c.Status()).GetProperty("sessionId").GetString());
        gateway.Reply = (_, _) => Json(new { success = true, closed = true });
        await c.CloseAsync(Id, default);
        Assert.Equal(JsonValueKind.Null, Json(c.Status()).GetProperty("sessionId").ValueKind);
    }
    [Fact] public async Task Failed_close_retains_the_exact_binding_for_retry()
    {
        var gateway = new FakeGateway(); var c = Coordinator(gateway); await c.OpenAsync(default);
        gateway.Reply = (_, _) => Json(new { success = false, closeRequired = true });
        await c.CloseAsync(Id, default);
        Assert.Equal(Id, Json(c.Status()).GetProperty("sessionId").GetString());
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.OpenAsync(default));
    }
    [Fact] public void Localized_captions_do_not_select_the_wrong_trash_button()
    {
        var controls = new[] { Button("删除", "clear-icon-button"), Button("anything", "property-title-delete-icon", "instance-header-section") };
        Assert.Equal(1, PlcsimUiIdentity.ResolveButton(controls, "property-title-delete-icon", "instance-header-section"));
    }
    [Fact] public void Duplicate_actionable_controls_are_rejected()
    {
        var controls = new[] { Button("one", "settings"), Button("two", "settings") };
        Assert.Throws<InvalidOperationException>(() => PlcsimUiIdentity.ResolveButton(controls, "settings"));
    }
    [Fact] public void Disabled_hidden_and_noninvokable_controls_are_not_actions()
    {
        var controls = new[] { Button("one", "settings"), Button("two", "settings"), Button("three", "settings") };
        controls[0].Enabled = false; controls[1].Offscreen = true; controls[2].Patterns = [];
        Assert.Throws<InvalidOperationException>(() => PlcsimUiIdentity.ResolveButton(controls, "settings"));
    }
    [Fact] public void Ancestor_instance_identity_is_deduplicated_and_static_label_is_not_an_instance()
    {
        var id = Guid.NewGuid(); var controls = new[] { Button("first", "instance-name-" + id), Button("second", "instance-name-" + id), Button("label", "instance-name-label"), Button("editor", "instance-name-input") };
        Assert.Equal(new[] { id }, PlcsimUiIdentity.InstanceIds(controls));
    }
    [Theory] [InlineData("instance-name-invalid")] [InlineData("instance-name-00000000-0000-0000-0000-000000000000")]
    public void Unknown_identity_cannot_prove_an_empty_workspace(string identity) =>
        Assert.Throws<InvalidOperationException>(() => PlcsimUiIdentity.InstanceIds(new[] { Button("unknown", identity) }));
    [Fact] public void Manual_rename_or_extra_instance_prevents_destructive_action()
    {
        var id = Guid.NewGuid(); var control = Button("user changed name", "instance-name-" + id);
        Assert.Throws<InvalidOperationException>(() => PlcsimUiIdentity.RequireSingleton(new[] { control }, id, "TiaGuard_expected"));
        control.Name = "TiaGuard_expected";
        Assert.Throws<InvalidOperationException>(() => PlcsimUiIdentity.RequireSingleton(new[] { control, Button("user", "instance-name-" + Guid.NewGuid()) }, id, "TiaGuard_expected"));
    }
    [Theory] [InlineData(0, false)] [InlineData(1, true)]
    public void Power_readback_does_not_depend_on_localized_names_or_disabled_run_buttons(int runtimes, bool expected)
    {
        var id = Guid.NewGuid(); var icon = PowerIcon(id);
        var run = Button("任意语言", "runButton-" + id); run.Enabled = false;
        Assert.Equal(expected, PlcsimUiIdentity.ObservedPower(new[] { icon, run }, id, runtimes));
    }
    [Theory] [InlineData(-1)] [InlineData(2)]
    public void Ambiguous_runtime_count_never_confirms_power(int runtimes) =>
        Assert.Null(PlcsimUiIdentity.ObservedPower(new[] { PowerIcon(Guid.Empty) }, Guid.Empty, runtimes));
    [Fact] public void Transitioning_hidden_or_duplicate_power_controls_are_unknown()
    {
        var id = Guid.NewGuid(); var icon = PowerIcon(id);
        Assert.Null(PlcsimUiIdentity.ObservedPower(Array.Empty<PlcsimUiControl>(), id, 1));
        icon.Offscreen = true; Assert.Null(PlcsimUiIdentity.ObservedPower(new[] { icon }, id, 1));
        icon.Offscreen = false; icon.Enabled = false; Assert.Null(PlcsimUiIdentity.ObservedPower(new[] { icon }, id, 0));
        icon.Enabled = true; Assert.Null(PlcsimUiIdentity.ObservedPower(new[] { icon, PowerIcon(id) }, id, 1));
        Assert.Null(PlcsimUiIdentity.ObservedPower(new[] { icon }, Guid.NewGuid(), 1));
    }
    [Theory] [InlineData("power-on")] [InlineData("power-off")]
    public async Task Explicit_power_actions_use_the_owned_session(string action)
    {
        var gateway = new FakeGateway(); var c = Coordinator(gateway); await c.OpenAsync(default);
        await c.ActAsync(new() { SessionId = Id, Action = action }, default);
        Assert.Equal("act_plcsim_desktop", gateway.Calls.Last());
    }
    private static PlcsimUiControl PowerIcon(Guid id) => new() { AutomationId = "instance-power-icon-" + id, Type = "Image", Enabled = true };
    [Fact] public void Navigation_expansion_is_scoped_and_rejects_ambiguous_buttons()
    {
        var controls = new[] { Button("Unknown", "other-panel"), Button("Unknown", "nav-bar"), Button("Unknown", "menu-tabs", "nav-bar") };
        Assert.Equal(1, PlcsimUiIdentity.ResolveNavigationExpander(controls));
        controls[1].Offscreen = true;
        Assert.Throws<InvalidOperationException>(() => PlcsimUiIdentity.ResolveNavigationExpander(controls));
        controls[1].Offscreen = false; controls[0].AncestorIds = ["nav-bar"];
        Assert.Throws<InvalidOperationException>(() => PlcsimUiIdentity.ResolveNavigationExpander(controls));
    }
    [Theory] [InlineData("Q0.0", "Q0.0")] [InlineData("I0.0", "I0.0")] [InlineData("I0.8", "Q0.0")]
    [InlineData("I00.0", "Q0.0")] [InlineData("I0.0\n", "Q0.0")] [InlineData("I0.0", "Q0.0\n")]
    [InlineData("I0.0,I0.0", "Q0.0")] [InlineData("I0.0", "Q0.0,Q0.0")] [InlineData("M0.0", "Q0.0")]
    public async Task Invalid_io_declarations_fail_at_both_boundaries_without_worker_mutation(string input, string output)
    {
        var inputs = input.Split(','); var outputs = output.Split(',');
        Assert.Throws<ArgumentException>(() => PlcsimUiIdentity.ValidateIoAddresses(inputs, outputs));
        var gateway = new FakeGateway(); var c = Coordinator(gateway); await c.OpenAsync(default);
        await Assert.ThrowsAsync<ArgumentException>(() => c.ActAsync(new() { SessionId = Id, Action = "prepare-io", InputAddresses = inputs, OutputAddresses = outputs }, default));
        Assert.Single(gateway.Calls);
    }
    [Fact] public void Io_configuration_requires_an_explicit_nonempty_output_set()
    {
        Assert.Throws<ArgumentException>(() => SimulationRequest.ValidateIoAddressSets([], []));
        Assert.Throws<ArgumentException>(() => PlcsimUiIdentity.ValidateIoAddresses([], []));
        Assert.Throws<ArgumentException>(() => SimulationRequest.ValidateIoAddressSets(null, ["Q0.0"]));
        Assert.Throws<ArgumentException>(() => PlcsimUiIdentity.ValidateIoAddresses(null!, ["Q0.0"]));
    }
    private static PlcsimUiControl Button(string name, params string[] ids) => new() { Name = name, AncestorIds = ids, Type = "Button", Enabled = true, Patterns = ["Invoke"] };
}
