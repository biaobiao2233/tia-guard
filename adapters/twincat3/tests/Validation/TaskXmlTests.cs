using TiaGuard.TwinCat.Validation;
using Xunit;

namespace TiaGuard.TwinCat.Validation.Tests;

public class TaskXmlTests
{
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
