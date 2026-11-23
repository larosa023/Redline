using Redline.Core.Gpu;
using Redline.Core.Sim;

namespace Redline.Core.Tests.Fakes;

public static class TestGpus
{
    public static Capabilities Caps(int coreMax = 500, int memMax = 2000, bool voltage = false, bool power = true,
        bool fans = true, bool sixteenPin = false, int coreStep = 15, int memStep = 50)
    {
        var r = new Dictionary<Control, ControlRange>
        {
            [Control.CoreOffset] = new(-200, coreMax, coreStep, 0),
            [Control.MemoryOffset] = new(-500, memMax, memStep, 0),
        };
        if (power) r[Control.PowerLimit] = new(50, 133, 1, 100);
        if (voltage) r[Control.VoltageOffset] = new(-150, 100, 5, 0);
        if (fans) r[Control.FanSpeed] = new(30, 100, 1, 0);
        return new Capabilities(r, HasFanCurve: fans, Has16PinConnector: sixteenPin);
    }

    public static SimGpuSpec Spec(int coreMax = 500, int memMax = 2000, int stableCore = 180, int stableMem = 1100,
        bool voltage = false, Dictionary<Control, int>? clamp = null, bool sixteenPin = false, int minStableVoltageMv = -60) =>
        new("Simulated RTX", Caps(coreMax, memMax, voltage, sixteenPin: sixteenPin), stableCore, stableMem, minStableVoltageMv,
            StockScore: 1000, StockPowerW: 400, DriverClamp: clamp);
}
