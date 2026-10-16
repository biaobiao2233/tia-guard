using TiaGuard.Bridge.Host;
using Xunit;

public sealed class WorkerInteractionTests
{
    [Theory] [InlineData("Openness 访问 (0033:000666)")] [InlineData("Openness access (0033:000666)")]
    public void Access_prompt_uses_the_vendor_code_across_display_languages(string title) =>
        Assert.True(WorkerInteractionProbe.IsOpennessAccessPrompt(title));
    [Theory] [InlineData("TIA Portal")] [InlineData("Openness access (0033:000001)")] [InlineData("Error 0033:000666")]
    public void An_unrelated_window_is_not_reported_as_permission_required(string title) =>
        Assert.False(WorkerInteractionProbe.IsOpennessAccessPrompt(title));
    [Fact] public void An_idle_bridge_does_not_launch_a_worker_for_interaction_status() =>
        Assert.Null(WorkerInteractionProbe.Read(null));
}
