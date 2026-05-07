using System;
using cAlgo.API;
using cAlgo.API.Collections;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Indicators;

[Indicator(AccessRights = AccessRights.None)]
public class DailyATR : Indicator
{

   // [Output("Main")]
   // public IndicatorDataSeries Result { get; set; }

    private int _dayStartBarIndex;
    private ChartTrendLine _highLine;
    private ChartTrendLine _lowLine;
    private ChartText _highLabel;
    private ChartText _lowLabel;
    private AverageTrueRange _ATR;
    private Bars _dailyBars;

    protected override void Initialize()
    {
        _dailyBars = MarketData.GetBars(TimeFrame.Daily);
        _ATR = Indicators.AverageTrueRange(MarketData.GetBars(TimeFrame.Daily), 20, MovingAverageType.Simple);

        // find first bar of the day
        DateTime dayStartTime = Bars.OpenTimes.Last(0).Date;
        _dayStartBarIndex = 1;
        while ( dayStartTime == Bars.OpenTimes.Last(_dayStartBarIndex).Date ) {
            _dayStartBarIndex++;
        } 

        // Draw Lines
        _highLine = Chart.DrawTrendLine("D1ATR_HIGH", Bars.OpenTimes.Last(_dayStartBarIndex), _dailyBars.HighPrices.LastValue, Bars.OpenTimes.Last(0), _dailyBars.HighPrices.LastValue, Color.Yellow, 2, LineStyle.DotsRare);
        _lowLine = Chart.DrawTrendLine("D1ATR_LOW", Bars.OpenTimes.Last(_dayStartBarIndex), _dailyBars.LowPrices.LastValue, Bars.OpenTimes.Last(0), _dailyBars.LowPrices.LastValue, Color.Yellow, 2, LineStyle.DotsRare);

        // Add labels to the line
        _highLabel = Chart.DrawText("D1ATR_HIGH_LABEL", "Daily ATR Projection: " + dblToString(_highLine.Y1), 
            Chart.LastVisibleBarIndex - _dayStartBarIndex, _highLine.Y1, 
            Color.Yellow);
        _lowLabel = Chart.DrawText("D1ATR_LOW_LABEL", "Daily ATR Projection: " + dblToString(_lowLine.Y1), 
            Chart.LastVisibleBarIndex - _dayStartBarIndex, _lowLine.Y1, 
            Color.Yellow);
    }

    public override void Calculate(int index)
    {   
        double atr = getD1Atr();
        _highLine.Y1 = _highLine.Y2 = _dailyBars.LowPrices.LastValue + atr;
        _lowLine.Y1 = _lowLine.Y2 = _dailyBars.HighPrices.LastValue - atr;
        _highLabel.Y = _highLine.Y1;
        _lowLabel.Y = _lowLine.Y1;
        _highLabel.Text = dblToString(_highLine.Y1);
        _lowLabel.Text = dblToString(_lowLine.Y1);

        if ( Symbol.Bid > _highLine.Y1 ) {
            setLinePassed(_highLine, _highLabel);
        } else {
            setLineNormal(_highLine, _highLabel);
        }

        if ( Symbol.Bid < _lowLine.Y1 ) {
            setLinePassed(_lowLine, _lowLabel);
        } else {
            setLineNormal(_lowLine, _lowLabel);
        }
    }

    private void setLineNormal( ChartTrendLine line, ChartText label) {
        line.Color = Color.Yellow;
        line.LineStyle = LineStyle.DotsRare;
        label.Color = Color.Yellow;
    }

    private void setLinePassed( ChartTrendLine line, ChartText label ) {
        line.Color = Color.Red;
        line.LineStyle = LineStyle.Solid;
        label.Color = Color.Red;
    }

    private double getD1Atr() {
        return _ATR.Result.Last(0);
    }

    private string dblToString( double x ) {
        return Math.Round(x,Symbol.Digits-1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}