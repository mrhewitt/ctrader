using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Collections;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots;

[Robot(AccessRights = AccessRights.None, AddIndicators = false)]
public class FVGTradeBar : Robot
{
    [Parameter(DefaultValue = 1.0)]
    public double StopSpace { get; set; }

    [Parameter(DefaultValue = 1.0)]
    public double EntrySpace { get; set; }

    [Parameter(DefaultValue = 2.0)]
    public double TargetInR { get; set; }

    [Parameter(DefaultValue=5000)]
    public double Capital { get; set; }

    [Parameter(DefaultValue = 0.25)]
    public double PositionPercentRisk { get; set; }

    private Button longButton;
    private Button shortButton;

    private bool goLong  = false;
    private bool goShort = false;

    // how many pips is half of our risk
    private double halfR = 0.0;
    // next price target to hit to move stop loss up by a halfR
    private double nextStopMoveTarget = 0.0;
    // flag we moved stop when price approached 90% of target
    private bool stopAtOneR = false;

    protected override void OnStart()
    {
    //    Positions.Opened += OnPositionOpened;
    //    Positions.Closed += OnPositionClosed;

        var buttonStyle = new Style();
        buttonStyle.Set(ControlProperty.Margin, new Thickness(0, 2, 0, 0));
        buttonStyle.Set(ControlProperty.Width, 75);
        var stackPanel = new StackPanel() {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(5)
        };

        longButton = new Button { Text = "LONG", Style = buttonStyle };
        longButton.Click += args =>
        {
            goLong = ! goLong;
            goShort = false;
            longButton.BackgroundColor = goLong ? Color.Orange : Color.Gray;
            shortButton.BackgroundColor = Color.Gray;
        };
        stackPanel.AddChild(longButton);

        shortButton = new Button { Text = "SHORT", Style = buttonStyle };
        shortButton.Click += args =>
        {
            goShort = ! goShort;
            goLong = false;
            longButton.BackgroundColor = Color.Gray;
            shortButton.BackgroundColor = goShort ? Color.Orange : Color.Gray;
        };
        stackPanel.AddChild(shortButton);

    }

    protected override void OnBar()
    {
        if ( goLong ) {
             openPosition(TradeType.Buy);
        } else if ( goShort ) {
             openPosition(TradeType.Sell);
        }
        goLong = goShort = false;
    }

    private void openPosition( TradeType type, int RMultiple = 1 ) {
        double sl = type == TradeType.Buy ? getLowestLow() - toPips(StopSpace) : getHighestHigh() + toPips(StopSpace);
        double price = type == TradeType.Buy ? Bars.Last(1).High + toPips(EntrySpace) : Bars.Last(1).Low - toPips(EntrySpace);
        double slRange = Math.Abs(price - sl);
        double tp = ( slRange * RMultiple * (type == TradeType.Buy ? 1 : -1) ) + price;
        double volume = Symbol.VolumeForProportionalRisk( ProportionalAmountType.Balance, PositionPercentRisk, toPips(slRange) );
        PlaceStopOrderAsync(type, SymbolName, volume, price, "FVG", sl, tp, ProtectionType.None);

        halfR = toPips( slRange/2 );
        nextStopMoveTarget = price + ( type == TradeType.Buy ? halfR : -halfR );
        stopAtOneR = false;
    }

    private double getHighestHigh() {
        double high = 0.0;
        for ( int i = 1; i <= 3; i++ ) {
            high = Math.Max( high, Bars.Last(i).High );
        }
        return high;
    }

    private double getLowestLow() {
        double low = 9999999.0;
        for ( int i = 1; i <= 3; i++ ) {
            low = Math.Min( low, Bars.Last(i).Low );
        }
        return low;
    }


    private double toPips(double range) {
        return range / Symbol.TickSize / 10;
    }

    private double toPrice(double pips) {
        return pips * Symbol.TickSize * 10;
    }

    protected override void OnTick()
    {
        Position position = Positions.FirstOrDefault();
        if ( position != null ) { 
            if ( position.TradeType == TradeType.Buy ) {
                if ( Symbol.Bid >= nextStopMoveTarget ) {
                    position.ModifyStopLossPrice( position.StopLoss + halfR );
                    nextStopMoveTarget += halfR;
                } else if ( !stopAtOneR ) {
                    // if we are 90% of way to target move stop to 1R
                    double distance = toPips(Symbol.Bid - position.EntryPrice);
                    if ( distance > (TargetInR * halfR * 2 * 0.9) ) {
                        position.ModifyStopLossPrice( position.EntryPrice + toPrice(halfR * 2) );
                        stopAtOneR = true;
                    }
                }
            } else {
                if ( Symbol.Ask <= nextStopMoveTarget ) {
                    position.ModifyStopLossPrice( position.StopLoss - halfR);
                    nextStopMoveTarget -= halfR;
                } else if ( !stopAtOneR ) {
                    // if we are 90% of way to target move stop to 1R
                    double distance = toPips(position.EntryPrice - Symbol.Ask);
                    if ( distance > (TargetInR * halfR * 2 * 0.9) ) {
                        position.ModifyStopLossPrice( position.EntryPrice - toPrice(halfR * 2) );
                        stopAtOneR = true;
                    }
                }
            }
        }
    }

    protected override void OnStop()
    {
        // Handle cBot stop here
    }
}