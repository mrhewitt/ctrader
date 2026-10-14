using System;
using System.Linq;
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

    [Parameter(DefaultValue = 1)]
    public int FirstPartR { get; set; }

    [Parameter(DefaultValue = 3)]
    public int SecondPartR { get; set; }

    [Parameter(DefaultValue=5000)]
    public double Capital { get; set; }

    [Parameter(DefaultValue = 1)]
    public double DailyRiskLimitPercent { get; set; }

    [Parameter(DefaultValue = 4)]
    public double LossesAllowed { get; set; }

    [Parameter(DefaultValue = true)]
    public bool SplitLots { get; set; }

    [Parameter(DefaultValue = 2)]
    public int LotDecimals { get; set; }

   // [Parameter(DefaultValue = 0.01)]
    public double Lots = 0 ;

    private Button buyButton;
    private Button sellButton;
    private Button longButton;
    private Button shortButton;
    private Button retraceButton;
    private Button closeHalfButton;
    private Button beHalfButton;
    private Button gotoBEButton;
    private Button trailPartButton;
    private Button counterButton;
    private Button reverseButton;

    private Button[] tradeTypeButtons;

    private ChartStaticText drawDownLabel;

    private bool goLong  = false;
    private bool goShort = false;
    private bool waitRetrace = false;
    private bool lookForEntry  = false;
    private bool reversePosition = false;
    private bool closePartAtBE = false;
    private bool trailPart2 = false;
    
 //   private bool noMoreOrders = false;

    // number of seconds left until order can be closed to count for profit
    private int secondsLeft = 0;
    // number of pips risked on the original position, set after the trades open, used for R calculations
    private double slRange = 0;

    private double drawDownAllowed = 0;

    private string tradeLabel = "PO";

    private T3MADynamicEnvelope _envelope;
    
    protected override void OnStart()
    {
        Positions.Opened += OnPositionOpened;
        Positions.Closed += OnPositionClosed;

        _envelope = Indicators.GetIndicator<T3MADynamicEnvelope>();

        var buttonStyle = new Style();
        buttonStyle.Set(ControlProperty.Margin, new Thickness(0, 2, 0, 0));
        buttonStyle.Set(ControlProperty.Width, 75);
        var stackPanel = new StackPanel() {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(5)
        };

        double positionRisk = DailyRiskLimitPercent / LossesAllowed;
        Print("Position Risk: ", positionRisk);
        Print("SL Size: ", getRange() + StopSpace);
        Lots = getLotsForRisk(); //Symbol.VolumeInUnitsToQuantity(Symbol.VolumeForProportionalRisk( ProportionalAmountType.Balance, positionRisk, getRange() + StopSpace ));
        Print("Lots: ", Lots);

        computeInitialDrawdown();
        drawDownLabel = Chart.DrawStaticText("drawdown", "Daily Drawdown Limit: ", VerticalAlignment.Top, HorizontalAlignment.Right, Color.WhiteSmoke);
        showRemainingDrawdown();

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


        beHalfButton = new Button { Text = "BE HALF", Style = buttonStyle, BackgroundColor = Color.Gray };
        beHalfButton.Click += args =>
        {
            closePartAtBE = !closePartAtBE;
            updateButtons();
        };
        beHalfButton.IsVisible = false;
        stackPanel.AddChild(beHalfButton);


        trailPartButton = new Button { Text = "TRAIL SL", Style = buttonStyle, BackgroundColor = Color.Gray };
        trailPartButton.Click += args =>
        {
            trailPart2 = !trailPart2;
            updateButtons();
        };
        trailPartButton.IsVisible = false;
        stackPanel.AddChild(trailPartButton);

        reverseButton = new Button { Text = "REVERSE", Style = buttonStyle, BackgroundColor = Color.SteelBlue };
        reverseButton.Click += args =>
        {
            reversePosition = !reversePosition;
            updateButtons();
        };
        stackPanel.AddChild(reverseButton);
        
        // move all stop losses to BE provided they are not already beyond entry price
        gotoBEButton = new Button { Text = "SL TO BE", Style = buttonStyle, BackgroundColor = Color.DarkSlateBlue };
        gotoBEButton.Click += args =>
        {
            foreach ( var position in Positions ) {
                if ( position.TradeType == TradeType.Buy && position.EntryPrice > position.StopLoss ) {
                    position.ModifyStopLossPrice( position.EntryPrice );
                } else if ( position.TradeType == TradeType.Sell && position.EntryPrice < position.StopLoss ) {
                    position.ModifyStopLossPrice( position.EntryPrice );
                }
            }
        };
        gotoBEButton.IsVisible = false;
        stackPanel.AddChild(gotoBEButton);

        closeHalfButton = new Button { Text = "TP HALF", Style = buttonStyle, BackgroundColor = Color.DarkBlue };
        closeHalfButton.Click += args =>
        {
            // close position
            ClosePosition( Positions.First() );
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
        
        stackPanel.AddChild(createHorizontalDivider());

        tradeTypeButtons = new Button[] {
            createTradeTypeButton(buttonStyle, "P1"),
            createTradeTypeButton(buttonStyle, "P2"),
            createTradeTypeButton(buttonStyle, "P3"),
            createTradeTypeButton(buttonStyle, "P4"),
            createTradeTypeButton(buttonStyle, "P5"),
            createTradeTypeButton(buttonStyle, "P6"),
            createTradeTypeButton(buttonStyle, "MR")
        };

        foreach ( Button b in tradeTypeButtons ) {
            stackPanel.AddChild(b);
        }

        Chart.AddControl(stackPanel);

        updateButtons();

        //    Position position = Positions.Find("Part One");
        //    if ( position != null ) {
        //        closeOrSetTP(position, FirstPartR );
        ///    }

        //   position = Positions.Find("Part Two");
        //    if ( position != null ) {
        //        closeOrSetTP(position, SecondPartR );
        //    }
    }

    private Border createHorizontalDivider()
    {
        var divider = new Border 
        {
            Height = 1, 
            BackgroundColor = Color.Gray, // Customize your divider color
            Margin = new Thickness(0, 5, 0, 5) // Space above and below the line
        };
        return divider;
    }

    private Button createTradeTypeButton( Style buttonStyle, string typeLabel ) {
        Button b = new Button { Text = typeLabel, Style = buttonStyle, BackgroundColor = Color.Gray };
        b.Click += args =>
        {
            tradeLabel = tradeLabel == typeLabel ? "PO" : typeLabel;   
            Print($"Trade opened will be {tradeLabel}");
            updateButtons();
        };
        return b;
    }  

    private double getLotsForRisk() {
        double riskAmount = (Capital * (DailyRiskLimitPercent / 100))  / LossesAllowed;
        double calculatedVolume  = riskAmount / ((getRange() + StopSpace) * Symbol.PipValue);
        return Symbol.VolumeInUnitsToQuantity( Symbol.NormalizeVolumeInUnits(calculatedVolume) );
    }

    // return total drawdown experience so far today, used for initialiazion
    // after init a running drawdown total is tracked to safve iterating entire history
    private void computeInitialDrawdown() {
        drawDownAllowed = Capital * (DailyRiskLimitPercent/100);
        // Query history for positions closed today
        foreach ( var order in History.Where(x => x.ClosingTime.Date == Time.Date) ) {
           drawDownAllowed += order.NetProfit;
        }
    }

    private void showRemainingDrawdown() {
        double risk = getRiskCashAmount();
        drawDownLabel.Text = FormattableString.Invariant($"Lots: {Lots:0.0##}   Position Risk: ${risk:0.00}      Limit Remaining: ${drawDownAllowed:0.00}");
        if ( drawDownAllowed <= risk ) {
            drawDownLabel.Color = Color.OrangeRed;
        } else if ( drawDownAllowed < 0 ) {
            drawDownLabel.Color = Color.Red;
        } else {
            drawDownLabel.Color = Color.DimGray;
        }
    }
    
    private double getRiskCashAmount( double lots = 0 ) {
        return (Symbol.QuantityToVolumeInUnits( lots == 0 ? Lots : lots ) * (getRange() + StopSpace)) * Symbol.PipValue;
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
        } else if ( tradeLabel == "MR" ) {

            // we didnt specify long or short by flagged we are waiting for M(ean) R(eversion) entry
            // so check if condition match and if so open trade automatically, otherwise keep looking
            // we return here so that we dont do other stuff or reset state as state must remain if looking for MR
            if ( isBullBar() && Symbol.Bid < _envelope.Lower.Last(0) ) {
                // we are under the envelope and a bullish bar closed so we can go long
                buy();
            } else if ( isBearBar() && Symbol.Bid > _envelope.Upper.Last(0) ) {
                // above envelope and bear bar, so go short
                sell();
            } else {
                // no state reset etc, we keep monitoting for a mean reversion entry
                return;
            }
        }


        // reverse, so if we are in shorts, and last bar closed bull, close positions and open new ones; or vice versa
        // we do nothing if the bar did not close in the valid direction
        if ( reversePosition ) {
            if ( Positions.Count > 0 ) {
                TradeType currentTradeType = Positions.FirstOrDefault().TradeType;
                if ( (currentTradeType == TradeType.Buy && isBearBar()) || (currentTradeType == TradeType.Sell && isBullBar()) ) {
                    foreach (var position in Positions) {
                        ClosePosition(position);
                    }

                    if ( currentTradeType == TradeType.Buy ) {
                        sell();
                    } else {
                        buy();
                    }
                }
            }
        }

        if ( trailPart2 ) {
            foreach ( Position part2 in Positions ) {
                if ( part2 != null && part2.TradeType == TradeType.Buy && part2.NetProfit > 0 && isBullBar() ) {
                    part2.ModifyStopLossPrice( getSL(part2.TradeType) );
                } else if ( part2 != null && part2.TradeType == TradeType.Sell && part2.NetProfit > 0 && isBearBar() ) {
                    part2.ModifyStopLossPrice( getSL(part2.TradeType) );
                }
            }
        }

        resetState();
    }

    private void buy() {
      //   if ( !noMoreOrders ) {
      //      noMoreOrders = true;
         //   ExecuteMarketOrderAsync(TradeType.Buy, SymbolName, getVolume(), "Part One", getBuySL(Symbol.Ask), 0, "", false);
         //   if ( SplitLots ) {
         //       ExecuteMarketOrderAsync(TradeType.Buy, SymbolName, getVolume(false), "Part Two", getBuySL(Symbol.Ask), 0, "", false);
         //   }
            openPosition(TradeType.Buy);
            if ( SplitLots ) {
                openPosition(TradeType.Buy,false);
            }
            startExitTimer();
      //  }
    }

    private void sell() {
       // if ( !noMoreOrders ) {
        //    noMoreOrders = true;
          /*  ExecuteMarketOrderAsync(TradeType.Sell, SymbolName, getVolume(), "Part One", getSellSL(Symbol.Bid), 0, "", false);
            if ( SplitLots ) {
               ExecuteMarketOrderAsync(TradeType.Sell, SymbolName, getVolume(false), "Part Two", getSellSL(Symbol.Bid), 0, "", false);
            }*/
            openPosition(TradeType.Sell);
            if ( SplitLots ) {
                openPosition(TradeType.Sell,false);
            }
            startExitTimer();
     //   }
    }

    private void openPosition( TradeType type, bool isFirstHalf = true ) {
        if ( Positions.Count < 2 ) {
            double price = type == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
            double sl = type == TradeType.Buy ? getBuySL(price) : getSellSL(price);
            double volume = getVolume(isFirstHalf);
            if ( volume > 0.0 ) {
                Chart.ColorSettings.BackgroundColor = Color.Black;
                ExecuteMarketOrderAsync(type, SymbolName, volume, tradeLabel + " " + (isFirstHalf ? "Part One" : "Part Two"), sl, 0, "", false) ;
            }
        }
    }

    private void closeOrSetTP( Position position, int RMultiple ) {
        double tp = getTPForPosition(position, RMultiple);
        Print($"RMultiple: {RMultiple:0} TP: {tp:0.00000}");
        if ( (position.TradeType == TradeType.Buy && Symbol.Bid >= tp) ||
             (position.TradeType == TradeType.Sell && Symbol.Ask <= tp) ) {
            ClosePosition(position);
        } else {
            position.ModifyTakeProfitPrice(tp);
        }
    }

    private double getVolume( bool isFirstHalf = true ) {
        if ( SplitLots ) {
            double halfLots = Lots / 2.0;
            double tradableLots = Math.Round( halfLots, LotDecimals, isFirstHalf ? MidpointRounding.AwayFromZero : MidpointRounding.ToZero );
            Print($"Lots: {Lots:0.00#} Half Lots: {halfLots:0.00#} Tradeable: {tradableLots:0.00#}");
            return Symbol.QuantityToVolumeInUnits(tradableLots);
        } else {
            return Symbol.QuantityToVolumeInUnits(Lots);
        }
    }

    private bool isBullBar( int barsBack = 1 ) {
        return Bars.Last(barsBack).Close > Bars.Last(barsBack).Open;
    }

    private bool isBearBar( int barsBack = 1 ) {
        return Bars.Last(barsBack).Close < Bars.Last(barsBack).Open;
    }

    private bool isPriceInsideBar( int barsBack = 3 ) {
        return Bars.Last(barsBack).High > Symbol.Bid && Bars.Last(barsBack).Low < Symbol.Bid;
    }

    private double getSL( TradeType type ) {
        if ( type == TradeType.Buy ) {
            return Bars.Last(1).Low - toPrice(StopSpace);
        } else {
            return Bars.Last(1).High + toPrice(StopSpace);
        }
    }

    private double getTPForPosition( Position position, int RMultiple = 1 ) {
        // work out R (pips risked), if we want a TP as 1R multiple then add the offset again
        // to cater for commisions, slippage etc
     //  double R = getRange() + StopSpace + ( RMultiple == 1 ? StopSpace : 0 );    
     //   double tpRange = toPrice(R * RMultiple * (position.TradeType == TradeType.Buy ? 1 : -1));
     //   Print($"R: {R:0.0####} TP Size: {tpRange:0.00000}");
      //  double range = Math.Abs( position.EntryPrice - position.StopLoss ?? 0 );
        return position.EntryPrice + ( (slRange + toPrice( RMultiple == 1 ? StopSpace : 0 )) * RMultiple * (position.TradeType == TradeType.Buy ? 1 : -1) );
    }

    private void setInitialTPs() {
        // first part is closed if we are already beyond its target
        foreach ( Position position in Positions ) {
            closeOrSetTP(position, position.Label.Contains("Part One") ? FirstPartR : SecondPartR );
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

    private double toPrice(double pips) {
        return pips * Symbol.TickSize * 10;
    }

    private void goFlat() {
        trailPart2 = false;
        resetState();
        
        // close positions
        foreach (var position in Positions) {
            ClosePosition(position);
        }
        
       // showHideButtons(true);
    } 

    private void resetState() {
        lookForEntry = false;
        goLong = false;
        goShort = false;
        waitRetrace = false;
        reversePosition = false;
        tradeLabel = "P0";

        updateButtons();
    }

    private void showHideButtons( bool visible ) {
        buyButton.IsVisible = visible;
        sellButton.IsVisible = visible;
        longButton.IsVisible = visible;
        shortButton.IsVisible = visible;
        retraceButton.IsVisible = visible;
        closeHalfButton.IsVisible = !visible;
        beHalfButton.IsVisible = !visible;
        trailPartButton.IsVisible = !visible;
        gotoBEButton.IsVisible = !visible;

        foreach ( Button b in tradeTypeButtons ) {
            b.IsVisible = visible || reversePosition;
        }
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

        if ( closePartAtBE ) {
            if ( Positions.First().NetProfit >= 0 ) {
                ClosePosition(Positions.First());
                closePartAtBE = false;
                beHalfButton.BackgroundColor = Color.Gray;
            }
        }

        if ( Positions.Count == 0 ) {
            if ( ( isBullBar(3) && isBearBar(2) && isBearBar(1) && isBullBar(0) && isPriceInsideBar()) || (isBearBar(3) && isBullBar(2) && isBullBar(1) && isBearBar(0) && isPriceInsideBar()) ) {
                Chart.ColorSettings.BackgroundColor = Color.FromHex("FF012413");
            } else {
                Chart.ColorSettings.BackgroundColor = Color.Black;
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

            // now its safe to close in profit, set our targets
            setInitialTPs();
        } else {
            counterButton.Text = secondsLeft.ToString();
        }
    }

    protected override void OnStop()
    {
        cancelExitTimer();
    }

    private void updateButtons() {
        showHideButtons( Positions.Count == 0 );
        closeHalfButton.IsVisible = ( Positions.Count > 1 );
        reverseButton.IsVisible = ( Positions.Count > 1 );

        setButtonState(shortButton, goShort);
        setButtonState(longButton, goLong);
        setButtonState(retraceButton, waitRetrace);
        setButtonState(beHalfButton, closePartAtBE);
        setButtonState(trailPartButton, trailPart2);
        setButtonState(reverseButton, reversePosition);
     
        foreach ( Button b in tradeTypeButtons ) {
            b.BackgroundColor = b.Text == tradeLabel ? Color.Orange : Color.Gray;
        }
    }

    private void setButtonState( Button b, bool state ) {
        b.BackgroundColor =  ( state ? Color.Orange : Color.Gray );
    }
    
    // method is called when a position opens. 
    private void OnPositionOpened(PositionOpenedEventArgs args)
    {
        updateButtons();
        Position position = args.Position;
        slRange = Math.Abs( position.EntryPrice - position.StopLoss ?? 0 );
        /*if ( position.TradeType == TradeType.Buy ) {
            position.ModifyStopLossPips( getBuySL(position.EntryPrice) );
        } else {
            position.ModifyStopLossPips( getSellSL(position.EntryPrice) );
        }*/
    }

    private void OnPositionClosed(PositionClosedEventArgs obj)
    {
        if ( Positions.Count == 0 ) {
            cancelExitTimer();
            closePartAtBE = false;
        }        

        drawDownAllowed += obj.Position.NetProfit;
        showRemainingDrawdown();

        updateButtons();
    }

}