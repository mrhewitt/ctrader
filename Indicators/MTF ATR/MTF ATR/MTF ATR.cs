using System;
using cAlgo.API;
using cAlgo.API.Collections;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Indicators;

[Indicator(AccessRights = AccessRights.None)]
public class MTFATR : Indicator
{
    [Parameter(DefaultValue = 4)]
    public int ATRPeriod { get; set; }

    [Output("Main")]
    public IndicatorDataSeries Result { get; set; }

    private ChartStaticText _atrLabel;
    private AverageTrueRange _currentATR;
    private AverageTrueRange _htfATR;

    protected override void Initialize()
    {
        _atrLabel = Chart.DrawStaticText("MTFATR", "Current ATR:", VerticalAlignment.Top, HorizontalAlignment.Left, Color.LightSlateGray);
        _currentATR = Indicators.AverageTrueRange(ATRPeriod, MovingAverageType.Simple);
        _htfATR = Indicators.AverageTrueRange(MarketData.GetBars(TimeFrame.Hour), getHTFATRPeriod(), MovingAverageType.Simple);
    }

    public override void Calculate(int index)
    {
        double catr = _currentATR.Result[index-1] / Symbol.TickSize / 10;
        double _hatr = _htfATR.Result.Last(1) / Symbol.TickSize / 10;
        _atrLabel.Text = "Current ATR: " + catr.ToString("F0") + "     " + getHTFLabel() + ": " + _hatr.ToString("F0");
    }

    private int getHTFATRPeriod() {
        return 24;
    }

    private string getHTFLabel() {
        return "H1 ATR(24)";
    }
}