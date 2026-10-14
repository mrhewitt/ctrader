using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Collections;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots;

[Robot(AccessRights = AccessRights.None, AddIndicators = false)]
public class YTCScalpTradeBar : Robot
{
   // [Parameter(DefaultValue = 4.0)]
  //  public double StopLossPips { get; set; }

  //  [Parameter(DefaultValue = 0.0)]
   // public double TakeProfitPips { get; set; }

    [Parameter(DefaultValue=5000)]
    public double Capital { get; set; }

    [Parameter(DefaultValue = 0.5)]
    public double DailyRiskLimitPercent { get; set; }

    [Parameter(DefaultValue = 5)]
    public double LossesAllowed { get; set; }

    // if true each lot will actually be treated as a seperate trade, so each part counts
    // as 1 trade towrd risk limit, and have same SL and position size
    [Parameter(DefaultValue=true)]
    public bool TreatLotsAsSeperate { get; set; }

    // if true, SL will be set wide, and all positions are closed when cash loss >= trade risk amount
    // if TreatLotsAsSeperate is false, risk amount is computed value, is true, risk amount is double computed value
    [Parameter(DefaultValue=true)]
    public bool NetValueStopLoss { get; set; }

   //[Parameter(DefaultValue = 0.01)]
   // public double Position1Lots { get; set; }

   // [Parameter(DefaultValue = 0.01)]
  //  public double Position2Lots { get; set; }

 //   [Parameter(DefaultValue = true)]
 //   public bool SplitLots { get; set; }

    [Parameter(DefaultValue = 35)]
    public int KeltnerPeriod { get; set; }

    [Parameter(DefaultValue = 3)]
    public int BandMultiple { get; set; }

    private Button buyButton;
    private Button sellButton;
    private Button closePart1Button;
    private Button closePart2Button;
    private Button counterButton;
    private Button flatButton;
    private Button gotoBEPart1Button;
    private Button gotoBEPart2Button;
    private Button bePart1Button;
    private Button bePart2Button;
    private Button exitOnPositiveButton;

    private Button[] tradeLabelFlowButtons;
    private Button[] tradeLabelBiasButtons;

    private ChartStaticText drawDownLabel;

    private KeltnerChannels _keltnerChannels;
    private KeltnerChannels _innerChannels;

    // number of seconds left until order can be closed to count for profit
    private int secondsLeft1 = 0;
    private int secondsLeft2 = 0;

    private bool bePart1 = false;
    private bool bePart2 = false;
    private bool exitWhenPositive = false;

    private double Position1Lots;
    private double Position2Lots;

    private double drawDownAllowed = 0;
    private double perTradeRiskAmount = 0;

    private string tradeLabelFlow = "WF";
    private string tradeLabelBias = "WB";

    protected override void OnStart()
    {   
        // if captial is not set then default to either one of our 5k or 10k accounts
        if ( Capital == 0 ) {
            Capital = Account.Balance > 9000 ? 10000 : 5000;
        }

        _keltnerChannels = Indicators.KeltnerChannels(KeltnerPeriod, MovingAverageType.Simple, 10, MovingAverageType.Simple, BandMultiple * 2);
        _innerChannels = Indicators.KeltnerChannels(KeltnerPeriod, MovingAverageType.Simple, 10, MovingAverageType.Simple, BandMultiple);

        Positions.Opened += OnPositionOpened;
        Positions.Closed += OnPositionClosed;
        PendingOrders.Created += OnPendingOrder;

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
    //    double slBandMulitple = TreatLotsAsSeperate ? 2 : 3;
     //   Print("SL Size: ", pipsInBand() * slBandMulitple);
        Position1Lots = getLotsForRisk(); //Symbol.VolumeInUnitsToQuantity(Symbol.VolumeForProportionalRisk( ProportionalAmountType.Balance, positionRisk, pipsInBand() * slBandMulitple ));
        Print("V4R ",Position1Lots );
        Position2Lots = Position1Lots;
        
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

        bePart1Button = new Button { Text = "BE PART 1", Style = buttonStyle, BackgroundColor = Color.Gray };
        bePart1Button.Click += args =>
        {
            bePart1 = !bePart1;
            updateButtons();
        };
        bePart1Button.IsVisible = false;
        stackPanel.AddChild(bePart1Button);
 

        bePart2Button = new Button { Text = "BE PART 2", Style = buttonStyle, BackgroundColor = Color.Gray };
        bePart2Button.Click += args =>
        {
            bePart2 = !bePart2;
            updateButtons();
        };
        bePart2Button.IsVisible = false;
        stackPanel.AddChild(bePart2Button);

        exitOnPositiveButton = new Button { Text = "EXIT BE", Style = buttonStyle, BackgroundColor = Color.Gray };
        exitOnPositiveButton.Click += args =>
        {
            exitWhenPositive = !exitWhenPositive;
            updateButtons();
        };
        exitOnPositiveButton.IsVisible = false;
        stackPanel.AddChild(exitOnPositiveButton);

        // move all stop losses to BE provided they are not already beyond entry price
        gotoBEPart1Button = new Button { Text = "SL 1 BE", Style = buttonStyle, BackgroundColor = Color.DarkSlateBlue };
        gotoBEPart1Button.Click += args =>
        {
            goBE( Positions.First() );
        };
        gotoBEPart1Button.IsVisible = false;
        stackPanel.AddChild(gotoBEPart1Button);
        
        gotoBEPart2Button = new Button { Text = "SL 2 BE", Style = buttonStyle, BackgroundColor = Color.DarkSlateBlue };
        gotoBEPart2Button.Click += args =>
        {
            goBE( Positions.Last() );
        };
        gotoBEPart2Button.IsVisible = false;
        stackPanel.AddChild(gotoBEPart2Button);

        closePart1Button = new Button { Text = "TP P1", Style = buttonStyle, BackgroundColor = Color.DarkBlue };
        closePart1Button.Click += args =>
        {
            // close the first position opened
            ClosePosition( Positions.FirstOrDefault() );
        };
        closePart1Button.IsVisible = false;
        stackPanel.AddChild(closePart1Button);

        closePart2Button = new Button { Text = "TP P2", Style = buttonStyle, BackgroundColor = Color.DarkBlue };
        closePart2Button.Click += args =>
        {
            // close the first position opened
            ClosePosition( Positions.FirstOrDefault() );
        };
        closePart2Button.IsVisible = false;
        stackPanel.AddChild(closePart2Button);

        flatButton = new Button { Text = "GO FLAT", Style = buttonStyle, BackgroundColor = Color.SteelBlue };
        flatButton.Click += args =>
        {
            goFlat();
        };
        stackPanel.AddChild(flatButton);

        counterButton = new Button { Text = "120", Style = buttonStyle };
        counterButton.Click += args =>
        {
            goFlat();
        };
        stackPanel.AddChild(counterButton);

        stackPanel.AddChild(createHorizontalDivider());
        
        tradeLabelFlowButtons = new Button[] {
            createTradeTypeFlowButton(buttonStyle, "WF"),
            createTradeTypeFlowButton(buttonStyle, "CF"),
        };
        foreach ( Button b in tradeLabelFlowButtons ) {
            stackPanel.AddChild(b);
        }

        stackPanel.AddChild(createHorizontalDivider());

        tradeLabelBiasButtons = new Button[] {
            createTradeTypeBiasButton(buttonStyle, "WB"),
            createTradeTypeBiasButton(buttonStyle, "CB"),
        };
        foreach ( Button b in tradeLabelBiasButtons ) {
            stackPanel.AddChild(b);
        }
                
        Chart.AddControl(stackPanel);
        resetState();

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

    private Button createTradeTypeFlowButton( Style buttonStyle, string typeLabel ) {
        Button b = new Button { Text = typeLabel, Style = buttonStyle, BackgroundColor = Color.Gray };
        b.Click += args =>
        {
            tradeLabelFlow = typeLabel;   
            Print($"Trade opened will be {tradeLabelFlow}");
            updateButtons();
        };
        return b;
    }  

    private Button createTradeTypeBiasButton( Style buttonStyle, string typeLabel ) {
        Button b = new Button { Text = typeLabel, Style = buttonStyle, BackgroundColor = Color.Gray };
        b.Click += args =>
        {
            tradeLabelBias = typeLabel;   
            Print($"Trade opened will be {tradeLabelBias}");
            updateButtons();
        };
        return b;
    }  

    // return total drawdown experience so far today, used for initialiazion
    // after init a running drawdown total is tracked to safve iterating entire history
    private void computeInitialDrawdown() {
        // changed this to show net P&L for day for new model of risking one full stop out per eval and rotating
        drawDownAllowed = 0; // Capital * (DailyRiskLimitPercent/100);
        // Query history for positions closed today
        foreach ( var order in History.Where(x => x.ClosingTime.Date == Time.Date) ) {
           drawDownAllowed += order.NetProfit;
        }
    }

    private double getLotsForRisk() {
        double slBandMulitple = TreatLotsAsSeperate ? 2 : 3;
        double slSize = pipsInBand() * slBandMulitple;
        Print("SL Size: ", slSize);

        double riskAmount = (Capital * (DailyRiskLimitPercent / 100))  / LossesAllowed;
        double calculatedVolume  = riskAmount / (slSize * Symbol.PipValue);
        return Symbol.VolumeInUnitsToQuantity( Symbol.NormalizeVolumeInUnits(calculatedVolume) );
    }

    private void showRemainingDrawdown() {
        double bandSize = pipsInBand();
        perTradeRiskAmount = ((Symbol.QuantityToVolumeInUnits(Position1Lots) * bandSize * 2) + (TreatLotsAsSeperate ? 0 : Symbol.QuantityToVolumeInUnits(Position2Lots) * bandSize)) * Symbol.PipValue;
        double netValueStop = getNetValueStop();
        string stoptype = ( NetValueStopLoss ? $"Net Value Stop ${netValueStop:0.00}" : "Hard Stop");
        drawDownLabel.Text = FormattableString.Invariant($"Lots: {Position1Lots:0.0##}/{Position2Lots:0.0##}  Risk: ${perTradeRiskAmount:0.00}   P&L: ${drawDownAllowed:0.00}  {stoptype}");
       /* if ( drawDownAllowed <= perTradeRiskAmount ) {
            drawDownLabel.Color = Color.OrangeRed;
        } else if ( drawDownAllowed < 0 ) {
            drawDownLabel.Color = Color.Red;
        } else {
            drawDownLabel.Color = Color.DimGray;
        }*/
        if ( drawDownAllowed <= -perTradeRiskAmount ) {
            drawDownLabel.Color = Color.Red;
        } else if ( drawDownAllowed >= perTradeRiskAmount ) {
            drawDownLabel.Color = Color.Green;
        } else {
            drawDownLabel.Color = Color.DimGray;
        }
    }

    private void buy() {
        openPosition(TradeType.Buy);
    }

    private void sell() {
        openPosition(TradeType.Sell);
    }

    private void openPosition( TradeType type ) {
        if ( getTotalOrders() < 2 ) {
            double price = type == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
            double volume = getVolume(price);
            if ( volume > 0.0 ) {
                int partNumber = Positions.Count + 1;
                ExecuteMarketOrderAsync(type, SymbolName, volume, $"SC P{partNumber} {tradeLabelFlow} {tradeLabelBias}", getStopLossPips(price,true), getTP(type));
            }
        }
    }

    private void goBE( Position position ) {
        position.ModifyStopLossPrice( position.EntryPrice );
    }

    private double getTP( TradeType side ) {
      //  if ( TakeProfitPips == 0 ) {
        if ( side == TradeType.Buy ) {
            return toPips(_keltnerChannels.Top.Last(0) - Symbol.Bid);
        } else {
            return toPips(Symbol.Bid - _keltnerChannels.Bottom.Last(0));
        }
      //  } else {
      //      return TakeProfitPips;
      //  }
    }

    private double getStopLossPips( double price, bool newMarketOrder = false ) {
        double bandSize = pipsInBand();
        if ( NetValueStopLoss ) {
            // stop loss is not a market order but rather all positions closed when net value of open positions is >= -risk amount
            // we set a hard stop here wquite wide simply as a precaution in case of network outage etc
            return bandSize * 4;
        } else {
            // count how many active orders, but if we are going to place a new market order
            // right now bump this by one to cater for order not in system yet
            int totalOrders = Positions.Count + PendingOrders.Count + (newMarketOrder ? 1 : 0);
            if ( totalOrders == 1 || TreatLotsAsSeperate ) {
                // first order always has a stop size to include basic and major wholesale region
                // if we are treating each position as a seperate trade (so it counts as 1 of our daily risks) then it gets same
                // size stop no matter if it is second or first order
                return bandSize * 2;
            } else {
                // second order has same stop loss as first 
                if ( Positions.Count > 0 ) {
                    return toPips( Math.Abs(price - Positions.OrderBy(order => order.EntryTime).FirstOrDefault().StopLoss ?? 0) );
                } else {
                    return toPips( Math.Abs(price - PendingOrders.OrderBy(order => order.SubmittedTime).FirstOrDefault().StopLoss ?? 0) );
                }
            }
        }
    }

    private double getNetValueStop() {
        return perTradeRiskAmount * (TreatLotsAsSeperate ? -2 : -1);
    }

    private string getPositionLabel() {
        return "YTC Scalper P" + Positions.Count.ToString();
    }

    private double getLots( double price ) {
        // if already have 2 positions (active or pending) dont allow
        // more to be opened to prevent accidently going over risk
        if ( getTotalOrders() > 2 ) {
            return 0.0;
        } else {
            // if postion is being reuqested in the inner bands use smaller lot
            // size no matter when (to prevent going over risk in cases where we chase)
            // price closer to center line on second position
            if ( isInnerBand(price) ) {
                return Math.Min(Position1Lots,Position2Lots);
            } else {
                return getTotalOrders() == 0 ? Position1Lots : Position2Lots;
            }
        }
    }

    private double getVolume( double price ) {
        double lots = getLots(price);
        return lots > 0.0 ? Symbol.QuantityToVolumeInUnits( lots ) : 0.0;
    }

    private int getTotalOrders() {
        return Positions.Count + PendingOrders.Count;
    }

    private double toPips(double range) {
        return range / Symbol.TickSize / 10;
    }

    private double pipsInBand() {
        return toPips(_innerChannels.Main.Last(0) - _innerChannels.Bottom.Last(0));
    } 

    private bool isInnerBand( double price ) {
        return _innerChannels.Bottom.Last(0) < price && _innerChannels.Top.Last(0) > price;
    }

    private void goFlat() {
        // close positions
        foreach (var position in Positions) {
            ClosePosition(position);
        }
        cancelOrders();
    } 

    private void cancelOrders() {
        foreach ( var order in PendingOrders ) {
            CancelPendingOrder(order);
        }
    }

    private void updateOrders() {
        foreach ( var order in PendingOrders ) {
            updatePendingOrderVolume(order);
        }
    }

    protected override void OnTick()
    {
        buyButton.IsVisible = getTotalOrders() < 2 && _keltnerChannels.Main.Last(0) > Symbol.Bid;
        sellButton.IsVisible = getTotalOrders() < 2 && _keltnerChannels.Main.Last(0) < Symbol.Bid;

        if ( bePart1 ) {
            if ( Positions.First().NetProfit >= 0 ) {
                ClosePosition(Positions.First());
                bePart1 = false;
                bePart1Button.BackgroundColor = Color.Gray;
            }
        }
 
        if ( bePart2 ) {
            if ( Positions.Last().NetProfit >= 0 ) {
                ClosePosition(Positions.Last());
                bePart2 = false;
                bePart2Button.BackgroundColor = Color.Gray;
            }
        }

        if ( Positions.Count > 0 ) {
            double netProfit = 0;
            foreach ( Position p in Positions ) {
                netProfit += p.NetProfit;
            }

                flatButton.Text = $"${netProfit:0.00} (X)";
                flatButton.BackgroundColor = netProfit < 0 ? Color.DarkRed : Color.DarkGreen;

            if ( NetValueStopLoss && Positions.Count > 0 ) {
                // if we are BE or better check if we requested to exit the trade if we are not overall negative
                if ( exitWhenPositive && netProfit >= 0 ) {
                    goFlat();
                    return;
                }

                // if net profit is <= to the per trade risk amount (or double the risk amount if we are allowed to treat each position as a seperate trade)
                // then its stoploss so close out all positions;   note what treating trades seperatly it does not matter if there is only 1 trade one, wwe still use
                // 2*risk value as stop loss as risk per trade will already be lower in this case as number of losses per day will be set higher
                if ( netProfit <= getNetValueStop() ) {
                    goFlat();
                }
            }
        }
    }

    private void resetState() {
        Timer.Stop();
        showHideButtons(true);
        counterButton.BackgroundColor = Color.Gray;

        bePart1 = false;
        bePart2 = false;
        exitWhenPositive = false;
        updateButtons();
    }

    private void updateButtons() {
        closePart1Button.IsVisible = ( Positions.Count > 0 );
        closePart2Button.IsVisible = ( Positions.Count > 1 );
        bePart1Button.BackgroundColor = bePart1 ? Color.Orange : Color.Gray;
        bePart2Button.BackgroundColor = bePart2 ? Color.Orange : Color.Gray;
        exitOnPositiveButton.BackgroundColor = exitWhenPositive ? Color.Orange : Color.Gray;
        bePart1Button.IsVisible = ( Positions.Count > 0 );
        bePart2Button.IsVisible = ( Positions.Count > 1 );
        gotoBEPart1Button.IsVisible = ( Positions.Count > 0 );
        gotoBEPart2Button.IsVisible = ( Positions.Count > 1 );
        exitOnPositiveButton.IsVisible = ( Positions.Count > 0 );

        if ( Positions.Count == 0 ) {
            flatButton.Text = "GO FLAT";
            flatButton.BackgroundColor = Color.SteelBlue;
        }
     
        toggleButtonGroup(tradeLabelFlowButtons, tradeLabelFlow);
        toggleButtonGroup(tradeLabelBiasButtons, tradeLabelBias);
    } 

    private void showHideButtons( bool visible ) {
        buyButton.IsVisible = visible;
        sellButton.IsVisible = visible;
        closePart1Button.IsVisible = !visible;
        closePart2Button.IsVisible = !visible;
        counterButton.IsVisible = !visible;
        gotoBEPart2Button.IsVisible = !visible;
        gotoBEPart1Button.IsVisible = !visible;

        showHideGroup(tradeLabelFlowButtons, visible);
        showHideGroup(tradeLabelBiasButtons, visible );
    }

    private void showHideGroup( Button[] list, bool visible ) {
        foreach ( Button b in list ) {
            b.IsVisible = visible;
        }    
    }

    private void toggleButtonGroup( Button[] list, string label ) {
        foreach ( Button b in list ) {
            b.BackgroundColor = b.Text == label ? Color.Orange : Color.Gray;
        }    
    }
    
    protected override void OnStop()
    {
        Timer.Stop();
    }

    
    protected override void OnTimer()
    {
        secondsLeft1 = adjustTimeLeft(secondsLeft1);
        secondsLeft2 = adjustTimeLeft(secondsLeft2);

        if ( secondsLeft1 <= 0 && secondsLeft2 <= 0 ) {
            counterButton.Text = "SAFE!";
            counterButton.BackgroundColor = Color.DarkGreen;
            Timer.Stop();
        } else {
            if ( secondsLeft1 > 0 && secondsLeft2 > 0 ) {
                counterButton.Text = secondsLeft1.ToString() + " / " + secondsLeft2.ToString();
            } else if ( secondsLeft2 > 0 ) {
                counterButton.Text = secondsLeft2.ToString();
            } else {
                counterButton.Text = secondsLeft1.ToString();
            }
        }
    }

    private int adjustTimeLeft( int secondsLeft ) {
        return Math.Max(0,secondsLeft-1);
    }

    // method is called when a position opens. 
    private void OnPositionOpened(PositionOpenedEventArgs args)
    {
        // cannot add more orders via buttons if more than one open on split lots or at all if not splitting 
       // noMoreOrders = SplitLots ? Positions.Count > 1 : true;
        // if we are at max limit then canel any other pending orders
       // if ( !SplitLots || Positions.Count > 1) {
       //     cancelOrders();
      //  }

        // update or remove volume on pending orders to ensure we can never have more than
        // two positions in 
        updateOrders();
        showHideButtons(false);
        updateButtons();

        if ( Positions.Count > 1) {
            secondsLeft2 = 120;
        } else {
            secondsLeft1 = 120;
        }
        Timer.Stop();
        Timer.Start(1);
        
        counterButton.BackgroundColor = Color.DarkGoldenrod;
        counterButton.IsVisible = true;
    }

    private void OnPositionClosed(PositionClosedEventArgs obj)
    {
        bePart2 = false;        // only one part left, so just left Part 1 BE even if it is called Part 2 to make buttons simple to operate
        updateButtons();

        // clear counter for the trade we closed, part1 or part2
        if ( obj.Position.Label.EndsWith("1") ) {
            secondsLeft1 = 0;
        } else {
            secondsLeft2 = 0; 
        }
              
        if ( Positions.Count == 0 ) {
            resetState();
        }
        drawDownAllowed += obj.Position.NetProfit;
        showRemainingDrawdown();
    }

    private void OnPendingOrder(PendingOrderCreatedEventArgs obj) {
        if ( updatePendingOrderVolume(obj.PendingOrder) ) {
            obj.PendingOrder.ModifyStopLossPips( getStopLossPips(obj.PendingOrder.TargetPrice) );

            double bandRange = _innerChannels.Main.Last(0) - _innerChannels.Bottom.Last(0);
            obj.PendingOrder.ModifyTakeProfitPrice( obj.PendingOrder.TargetPrice + (obj.PendingOrder.TradeType == TradeType.Buy ? bandRange*2 : -2*bandRange) );
        }
    }

    private bool updatePendingOrderVolume( PendingOrder order ) {
        double volume = getVolume( order.TargetPrice );
        if ( volume > 0.0 ) {
            order.ModifyVolume(volume);
            return true;
        } else {
            CancelPendingOrder(order);
            return false;
        }
    }
}