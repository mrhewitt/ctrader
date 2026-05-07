using System;
using cAlgo.API;
using cAlgo.API.Collections;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo
{
    [Indicator(IsOverlay = true, ScalePrecision = 2, AutoRescale = false, AccessRights = AccessRights.None)]
    public class T3MAColored : Indicator
    {
        [Parameter(DefaultValue = 5, MinValue = 1)]
        public int MAPeriod { get; set; }

        [Parameter(DefaultValue = 0.4, MinValue = 0)]
        public double b { get; set; }

        [Output("Main", LineColor = "Yellow")]
        public IndicatorDataSeries Result { get; set; }

        [Output("MAUpA", LineColor = "SteelBlue", LineStyle = LineStyle.Solid, PlotType = PlotType.DiscontinuousLine, Thickness = 5)]
        public IndicatorDataSeries MAUpa { get; set; }

        [Output("MAUpB", LineColor = "SteelBlue", LineStyle = LineStyle.Solid, PlotType = PlotType.DiscontinuousLine, Thickness = 5)]
        public IndicatorDataSeries MAUpb { get; set; }

        [Output("MADownA", LineColor = "MediumVioletRed", LineStyle = LineStyle.Solid, PlotType = PlotType.DiscontinuousLine, Thickness = 5)]
        public IndicatorDataSeries MADowna { get; set; }

        [Output("MADownB", LineColor = "MediumVioletRed", LineStyle = LineStyle.Solid, PlotType = PlotType.DiscontinuousLine, Thickness = 5)]
        public IndicatorDataSeries MADownb { get; set; }

        private IndicatorDataSeries e1,e2,e3,e4,e5,e6;
        private double c1,c2,c3,c4;
        private double n,w1,w2,b2,b3;

        protected override void Initialize()
        {
            c1 = c2 = c3 = c4 = 0;
            n = 0;
            w1 = w2 = 0;
            b2 = b3 = 0;
            
            b2 = b*b;
            b3 = b2 * b;
            c1 = -b3;
            c2 = (3 * (b2+b3));
            c3 = -3 * (2*b2+b+b3);
            c4 = (1 + 3*b+b3+3*b2);
            n = MAPeriod;

            if (n < 1 ) { n = 1; }
            n = 1 + 0.5*(n-1);
            w1 = 2 / (n + 1);
            w2 = 1 - w1;

            e1 = CreateDataSeries();
            e2 = CreateDataSeries();
            e3 = CreateDataSeries();
            e4 = CreateDataSeries();
            e5 = CreateDataSeries();
            e6 = CreateDataSeries();
        }

        public override void Calculate(int index)
        {
           // Result[index] = Bars.HighPrices[index] + 0.20; 
            e1[index] = w1 * Bars.ClosePrices[index] + w2*getV(e1,index);
            e2[index] = w1*e1[index] + w2*getV(e2,index);
            e3[index] = w1*e2[index] + w2*getV(e3,index);
            e4[index] = w1*e3[index] + w2*getV(e4,index);
            e5[index] = w1*e4[index] + w2*getV(e5,index);
            e6[index] = w1*e5[index] + w2*getV(e6,index);

            MAUpa[index] = double.NaN;
            MAUpb[index] = double.NaN;
            MADowna[index] = double.NaN;
            MADownb[index] = double.NaN;

            Result[index] = c1 * e6[index] + c2*e5[index] + c3*e4[index] + c4*e3[index];
            if ( Result[index] > Result[index-1] ) { plotPoint(index,MAUpa,MAUpb,Result); } 
            if ( Result[index] < Result[index-1] )  { plotPoint(index,MADowna,MADownb,Result); }
        }

        private void plotPoint( int index, IndicatorDataSeries first, IndicatorDataSeries second, IndicatorDataSeries result) {
            if ( first[index-1] == double.NaN ) {
                if ( first[index-2] == double.NaN ) {
                    first[index] = result[index];
                    first[index-1] = result[index-1];
                    second[index] = double.NaN;
                } else {
                    second[index] = result[index];
                    second[index-1] = result[index-1];
                    first[index] = double.NaN;
                }
            } else {
                first[index] = result[index];
                second[index] = double.NaN;
            }
        }

        private double getV(IndicatorDataSeries series, int index) {
            return double.IsNaN(series[index-1]) ? 1.0 : series[index-1];
        }
    }
}