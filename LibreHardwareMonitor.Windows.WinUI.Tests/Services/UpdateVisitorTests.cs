using System;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.WinUI.Services;
using Moq;
using Xunit;

namespace LibreHardwareMonitor.Windows.WinUI.Tests.Services;

public class UpdateVisitorTests
{
    [Fact]
    public void VisitComputer_CallsTraverse()
    {
        var mockComputer = new Mock<IComputer>();
        var visitor = new UpdateVisitor();

        visitor.VisitComputer(mockComputer.Object);

        mockComputer.Verify(c => c.Traverse(visitor), Times.Once);
    }

    [Fact]
    public void VisitHardware_CallsUpdateAndAcceptsOnSubHardware()
    {
        var visitor = new UpdateVisitor();

        var mockHardware = new Mock<IHardware>();
        var mockSubHardware1 = new Mock<IHardware>();
        var mockSubHardware2 = new Mock<IHardware>();

        mockHardware.Setup(h => h.SubHardware).Returns(new[] { mockSubHardware1.Object, mockSubHardware2.Object });

        visitor.VisitHardware(mockHardware.Object);

        mockHardware.Verify(h => h.Update(), Times.Once);
        mockSubHardware1.Verify(h => h.Accept(visitor), Times.Once);
        mockSubHardware2.Verify(h => h.Accept(visitor), Times.Once);
    }

    [Fact]
    public void VisitSensor_DoesNothing()
    {
        var visitor = new UpdateVisitor();
        var mockSensor = new Mock<ISensor>();

        visitor.VisitSensor(mockSensor.Object);
        // Assert it does not throw
    }

    [Fact]
    public void VisitParameter_DoesNothing()
    {
        var visitor = new UpdateVisitor();
        var mockParameter = new Mock<IParameter>();

        visitor.VisitParameter(mockParameter.Object);
        // Assert it does not throw
    }
}
