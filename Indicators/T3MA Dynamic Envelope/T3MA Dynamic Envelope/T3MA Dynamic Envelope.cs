using System;
using cAlgo.API;
using cAlgo.API.Collections;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo
{
    [Indicator(ScalePrecision = 2, IsOverlay = true, AutoRescale = false, AccessRights = AccessRights.None)]
    public class T3MADynamicEnvelope : Indicator
    {
        private T3MA _t3ma;
        private AverageTrueRange _atr;

        [Output("Upper", LineColor = "DimGray", LineStyle = LineStyle.Solid)]
        public IndicatorDataSeries Upper { get; set; }

        [Output("Lower", LineColor = "DimGray", LineStyle = LineStyle.Solid)]
        public IndicatorDataSeries Lower { get; set; }

        [Parameter(DefaultValue = 30, MinValue = 1)]
        public int MAPeriod { get; set; }

        [Parameter(DefaultValue = 0.4)]
        public double HotParameter { get; set; }

        [Parameter(DefaultValue = 100)]
        public int ATRPeriod { get; set; }

        [Parameter(DefaultValue = 4.0)]
        public double ATRMultiple { get; set; }

        protected override void Initialize()
        {
         //   _movingAverage = Indicators.MovingAverage(Source, Periods, MAType);
            _t3ma = Indicators.GetIndicator<T3MA>(MAPeriod,HotParameter );
            _atr = Indicators.AverageTrueRange(ATRPeriod, MovingAverageType.Simple);
        }

        public override void Calculate(int index)
        {
        //    var maValue = _movingAverage.Result[index];
            double atr = _atr.Result[index]; 
            double t3ma = _t3ma.Result[index];
            Upper[index] = t3ma + (atr * ATRMultiple);
            Lower[index] = t3ma - (atr * ATRMultiple);
        }
    }
}