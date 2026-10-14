using TiaGuard.TwinCat.Validation;
using Xunit;

namespace TiaGuard.TwinCat.Validation.Tests;

public class TaskXmlTests
{
    [Fact] public void Actual4026TaskWithMatchingDefaultModuleIsComplete()
    {
        var result = TaskXmlProjection.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "task-4026.27.xml")));
        Assert.True(result.Complete, string.Join("; ", result.Unknowns));
    }

    [Theory]
    [InlineData("<AutoStart>true</AutoStart>", "<AutoStart>false</AutoStart>")]
    [InlineData("<Disabled>false</Disabled>", "<Disabled>true</Disabled>")]
    [InlineData("<CpuAffinity>#x0</CpuAffinity>", "<CpuAffinity>#x1</CpuAffinity>")]
    [InlineData("<CycleTime>10000000</CycleTime>", "<CycleTime>20000000</CycleTime>")]
    [InlineData("<InitSequence>PO</InitSequence>", "<InitSequence>SO</InitSequence>")]
    public void ChangedNativeDefaultsOrModuleTimingCannotPass(string before, string after)
    {
        var xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "task-4026.27.xml"));
        Assert.Contains(before, xml);
        Assert.False(TaskXmlProjection.Read(xml.Replace(before, after)).Complete);
    }
    [Fact] public void ExactCycleAndPriorityAreProjected()
    {
        var result = TaskXmlProjection.Read("<TreeItem><ItemName>Task</ItemName><TaskDef><CycleTime>100000</CycleTime><Priority>20</Priority></TaskDef></TreeItem>");
        Assert.True(result.Complete); Assert.Equal(10000, result.CycleTimeUs); Assert.Equal(20, result.Priority);
    }
    [Theory]
    [InlineData("<CycleTime>100001</CycleTime><Priority>20</Priority>")]
    [InlineData("<CycleTime>0</CycleTime><Priority>20</Priority>")]
    [InlineData("<CycleTime>100000</CycleTime><Priority>0</Priority>")]
    [InlineData("<CycleTime>100000</CycleTime>")]
    [InlineData("<CycleTime>100000</CycleTime><CycleTime>100000</CycleTime><Priority>20</Priority>")]
    [InlineData("<CycleTime>100000</CycleTime><Priority>20</Priority><CpuAffinity>1</CpuAffinity>")]
    [InlineData("<CycleTime>100000</CycleTime><Priority unknown='true'>20</Priority>")]
    public void MissingUnknownAndUnrepresentableOptionsPreventComplete(string body) => Assert.False(TaskXmlProjection.Read("<TreeItem><TaskDef>" + body + "</TaskDef></TreeItem>").Complete);
    [Fact] public void OpaqueMetadataPreventsComplete() => Assert.False(TaskXmlProjection.Read("<TreeItem><Unknown>0</Unknown><TaskDef><CycleTime>100000</CycleTime><Priority>20</Priority></TaskDef></TreeItem>").Complete);
    [Fact] public void DoctypeIsProhibited() => Assert.False(TaskXmlProjection.Read("<!DOCTYPE TreeItem [<!ENTITY x '100000'>]><TreeItem><TaskDef><CycleTime>&x;</CycleTime><Priority>20</Priority></TaskDef></TreeItem>").Complete);
}
