using System;
using cAlgo.API;
using cAlgo.API.Collections;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Indicators;

[Indicator(AccessRights = AccessRights.None)]
public class TradeTimer : Indicator
{

    bool _hasOpenPosition = false;

    [Output("Main")]
    public IndicatorDataSeries Result { get; set; }

    protected override void Initialize()
    {
    }

    public override void Calculate(int index)
    {
        // Calculate value at specified index
        // Result[index] = 
    }



}