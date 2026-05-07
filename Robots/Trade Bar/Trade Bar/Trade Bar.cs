using System;
using cAlgo.API;
using cAlgo.API.Collections;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots;

[Robot(AccessRights = AccessRights.None, AddIndicators = true)]
public class TradeBar : Robot
{
    [Parameter(DefaultValue = 1.0)]
    public double StopSpace { get; set; }

    [Parameter(DefaultValue = 5)]
    public int TP1Bars { get; set; }

    [Parameter(DefaultValue = 10)]
    public int TP2Bars { get; set; }

    [Parameter(DefaultValue = 0.001)]
    public double Risk { get; set; }

    [Parameter(DefaultValue = 0.01)]
    public double Lots { get; set; }

    [Parameter(DefaultValue = true)]
    public bool SplitLots { get; set; }

    private Button buyButton;
    private Button sellButton;
    private Button longButton;
    private Button shortButton;
    private Button retraceButton;
    private Button closeHalfButton;
    private Button counterButton;

    private bool goLong  = false;
    private bool goShort = false;
    private bool waitRetrace = false;
    private bool lookForEntry  = false;
    private bool noMoreOrders = false;

    // number of seconds left until order can be closed to count for profit
    private int secondsLeft = 120;
    
    protected override void OnStart()
    {
        Positions.Opened += OnPositionOpened;
        Positions.Closed += OnPositionClosed;

        var buttonStyle = new Style();
        buttonStyle.Set(ControlProperty.Margin, new Thickness(0, 2, 0, 0));
        buttonStyle.Set(ControlProperty.Width, 75);
        var stackPanel = new StackPanel() {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(5)
        };

        buyButton = new Button { Text = "BUY", Style = buttonStyle, BackgroundColor = Color.ForestGreen };
        buyButton.Click += args =>
        {
           buy();
        };
        stackPanel.AddChild(buyButton);

        sellButton = new Button { Text = "SELL", Style = buttonStyle, BackgroundColor = Color.Firebrick };
        sellButton.Click += args =>
        {
           sell();
        };
        stackPanel.AddChild(sellButton);

        longButton = new Button { Text = "LONG", Style = buttonStyle };
        longButton.Click += args =>
        {
            goLong = ! goLong;
            goShort = false;
            longButton.BackgroundColor = goLong ? Color.Orange : Color.Gray;
            shortButton.BackgroundColor = Color.Gray;
         //   Chart.DrawHorizontalLine("myLine", Bars.ClosePrices.Last(1), Color.Red, 2, LineStyle.Lines);
        };
        stackPanel.AddChild(longButton);

        shortButton = new Button { Text = "SHORT", Style = buttonStyle };
        shortButton.Click += args =>
        {
            goShort = ! goShort;
            goLong = false;
            longButton.BackgroundColor = Color.Gray;
            shortButton.BackgroundColor = goShort ? Color.Orange : Color.Gray;
         //  Chart.DrawHorizontalLine("myLine", Bars.ClosePrices.Last(1), Color.Red, 2, LineStyle.Lines);
        };
        stackPanel.AddChild(shortButton);

        retraceButton = new Button { Text = "RETRACE", Style = buttonStyle };
        retraceButton.Click += args =>
        {
            waitRetrace = ! waitRetrace;
            retraceButton.BackgroundColor = waitRetrace ? Color.Orange : Color.Gray;
         //  Chart.DrawHorizontalLine("myLine", Bars.ClosePrices.Last(1), Color.Red, 2, LineStyle.Lines);
        };

        stackPanel.AddChild(retraceButton);
        closeHalfButton = new Button { Text = "TP HALF", Style = buttonStyle, BackgroundColor = Color.DarkBlue };
        closeHalfButton.Click += args =>
        {
            // close positions
            if ( Positions.Count == 1 ) {
                goFlat();
            } else {
                foreach (var position in Positions) {
                    if ( position.Label == "Part One" ) {
                        ClosePosition(position);
                    }
                }
            }
        };
        closeHalfButton.IsVisible = false;
        stackPanel.AddChild(closeHalfButton);

        var flatButton = new Button { Text = "GO FLAT", Style = buttonStyle, BackgroundColor = Color.SteelBlue };
        flatButton.Click += args =>
        {
            goFlat();
        };
        stackPanel.AddChild(flatButton);

        counterButton = new Button { Text = "120", Style = buttonStyle };
        counterButton.Click += args =>
        {
         
        };
        stackPanel.AddChild(counterButton);
        counterButton.IsVisible = false;
        
        Chart.AddControl(stackPanel);
    }

    protected override void OnBar()
    {
        if ( waitRetrace && (goLong || goShort) ) {
            if ( (goLong && isBullBar()) || (goShort && isBearBar()) ) {
                lookForEntry = true;
                return;
            }
        } else if ( goLong && isBullBar() ) {
            buy();
        } else if ( goShort && isBearBar() ) {
            sell();
        }

        resetState();
    }

    private void buy() {
         if ( !noMoreOrders ) {
            noMoreOrders = true;
            ExecuteMarketOrderAsync(TradeType.Buy, SymbolName, getVolume(), "Part One", getBuySL(Symbol.Ask), getTPPips(TP1Bars), "", false);
            if ( SplitLots ) {
                ExecuteMarketOrderAsync(TradeType.Buy, SymbolName, getVolume(), "Part Two", getBuySL(Symbol.Ask), getTPPips(TP2Bars), "", false);
            }
            startExitTimer();
        }
    }

    private void sell() {
        if ( !noMoreOrders ) {
            noMoreOrders = true;
            ExecuteMarketOrderAsync(TradeType.Sell, SymbolName, getVolume(), "Part One", getSellSL(Symbol.Bid), getTPPips(TP1Bars), "", false);
            if ( SplitLots ) {
                ExecuteMarketOrderAsync(TradeType.Sell, SymbolName, getVolume(), "Part Two", getSellSL(Symbol.Bid), getTPPips(TP2Bars), "", false);
            }
            startExitTimer();
        }
    }

    private double getVolume() {
        return Symbol.QuantityToVolumeInUnits(SplitLots ? Lots/2.0 : Lots);
    }

    private bool isBullBar() {
        return Bars.Last(1).Close > Bars.Last(1).Open;
    }

    private bool isBearBar() {
        return Bars.Last(1).Close < Bars.Last(1).Open;
    }

    private double getSL( bool buy ) {
        if ( buy ) {
            return Bars.Last(1).Low - StopSpace;
        } else {
            return Bars.Last(1).High + StopSpace;
        }
    }

    private double getTP( bool buy , int bars = 1 ) {
        if ( buy ) {
            return getTPPips(bars) + Bars.Last(1).High;
        } else {
            return Bars.Last(1).Low - getTPPips(bars);
        }
    }

    private double getBuySL( double price ) {
        return toPips(price - Bars.Last(1).Low) + StopSpace;
    }

    private double getSellSL( double price ) {
        return toPips(Bars.Last(1).High - price) + StopSpace;

    }

    private double getTPPips( int bars) {
         return (getRange() * bars);
    }

    private double getRange() {
        return toPips( Math.Abs(Bars.Last(1).High - Bars.Last(1).Low) );
    }

    private double toPips(double range) {
        return range / Symbol.TickSize / 10;
    }

    private void goFlat() {
        resetState();
        // close positions
        foreach (var position in Positions) {
            ClosePosition(position);
        }
        
        showHideButtons(true);
    } 

    private void resetState() {
        lookForEntry = false;
        goLong = false;
        goShort = false;
        waitRetrace = false;
        noMoreOrders = false;

        shortButton.BackgroundColor = Color.Gray;
        longButton.BackgroundColor = Color.Gray;
        retraceButton.BackgroundColor = Color.Gray;
    }

    private void showHideButtons( bool visible ) {
        buyButton.IsVisible = visible;
        sellButton.IsVisible = visible;
        longButton.IsVisible = visible;
        shortButton.IsVisible = visible;
        retraceButton.IsVisible = visible;
        closeHalfButton.IsVisible = !visible;
    }

    private void startExitTimer() {
        secondsLeft = 120;
        Timer.Stop();
        Timer.Start(1);       
        
        counterButton.Text = secondsLeft.ToString();
        counterButton.BackgroundColor = Color.DarkGoldenrod;
        counterButton.IsVisible = true;
    }

    private void cancelExitTimer() {
        Timer.Stop();
        counterButton.IsVisible = false;
    }

    protected override void OnTick()
    {
        if ( lookForEntry ) {
            if ( goLong ) {
                if ( Symbol.Ask <= Bars.Last(1).High ) {
                    resetState();
                    buy();
                }
            }
            if ( goShort ) {
                if ( Symbol.Bid >= Bars.Last(1).Low ) {
                    resetState();
                    sell();
                } 
            }
        }
    }
    
    protected override void OnTimer()
    {
        secondsLeft = Math.Max(0,secondsLeft-1);
        if ( secondsLeft == 0 ) {
            counterButton.Text = "SAFE!";
            counterButton.BackgroundColor = Color.DarkGreen;
            Timer.Stop();
        } else {
            counterButton.Text = secondsLeft.ToString();
        }
    }

    protected override void OnStop()
    {
        cancelExitTimer();
    }

    
    // method is called when a position opens. 
    private void OnPositionOpened(PositionOpenedEventArgs args)
    {
        showHideButtons(false);
        Position position = args.Position;
    
        if ( position.TradeType == TradeType.Buy ) {
            position.ModifyStopLossPips( getBuySL(position.EntryPrice) );
        } else {
            position.ModifyStopLossPips( getSellSL(position.EntryPrice) );
        }

        closeHalfButton.IsVisible = ( Positions.Count > 1 );
    }

    private void OnPositionClosed(PositionClosedEventArgs obj)
    {
        closeHalfButton.IsVisible = ( Positions.Count > 1 );
        if ( Positions.Count == 0 ) {
            showHideButtons(true);
            cancelExitTimer();
        }
    }

}