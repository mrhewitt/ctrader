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

    [Parameter(DefaultValue = 1)]
    public double DailyRiskLimitPercent { get; set; }

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
    private Button closeHalfButton;
    private Button counterButton;
    private Button flatButton;
    private ChartStaticText drawDownLabel;

    private KeltnerChannels _keltnerChannels;
    private KeltnerChannels _innerChannels;

    // number of seconds left until order can be closed to count for profit
    private int secondsLeft1 = 0;
    private int secondsLeft2 = 0;

    private double Position1Lots;
    private double Position2Lots;

    private double drawDownAllowed = 0;

    protected override void OnStart()
    {
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

        double positionRisk = DailyRiskLimitPercent / 4;
        Print("Position Risk: ", positionRisk);
        Print("SL Size: ", pipsInBand() * 3);
        Position1Lots = Symbol.VolumeInUnitsToQuantity(Symbol.VolumeForProportionalRisk( ProportionalAmountType.Balance, positionRisk, pipsInBand() * 3 ));
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

        closeHalfButton = new Button { Text = "TP HALF", Style = buttonStyle, BackgroundColor = Color.DarkBlue };
        closeHalfButton.Click += args =>
        {
            // close positions
            if ( Positions.Count == 1 ) {
                goFlat();
            } else {
                ClosePosition(Positions[0]);
            }
        };
        closeHalfButton.IsVisible = false;
        stackPanel.AddChild(closeHalfButton);

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

        Chart.AddControl(stackPanel);
        resetState();

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
        double bandSize = pipsInBand();
        double risk = ((Symbol.QuantityToVolumeInUnits(Position1Lots) * bandSize * 2) + (Symbol.QuantityToVolumeInUnits(Position2Lots) * bandSize)) * Symbol.PipValue;
        drawDownLabel.Text = FormattableString.Invariant($"Lots: {Position1Lots:0.0##}/{Position2Lots:0.0##}  Position Risk: ${risk:0.00}      Limit Remaining: ${drawDownAllowed:0.00}");
        if ( drawDownAllowed <= risk ) {
            drawDownLabel.Color = Color.OrangeRed;
        } else if ( drawDownAllowed < 0 ) {
            drawDownLabel.Color = Color.Red;
        } else {
            drawDownLabel.Color = Color.DimGray;
        }
    }



    private void buy() {
       // if ( !noMoreOrders ) {
            // cannot add more orders after this is there is already 1 open if splitting, or at all if no split
      //      noMoreOrders = SplitLots ? Positions.Count == 1 : true;
        openPosition(TradeType.Buy);
    }

    private void sell() {
      //  if ( !noMoreOrders ) {
            // cannot add more orders after this is there is already 1 open if splitting, or at all if no split
       //     noMoreOrders = SplitLots ? Positions.Count == 1 : true;
        openPosition(TradeType.Sell);
    }

    private void openPosition( TradeType type ) {
        if ( getTotalOrders() < 2 ) {
            double price = type == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
            double volume = getVolume(price);
            if ( volume > 0.0 ) {
                ExecuteMarketOrderAsync(type, SymbolName, volume, "", getStopLossPips(price,true), getTP(type));
            }
        }
    }

    private double getTP( TradeType side ) {
      //  if ( TakeProfitPips == 0 ) {
        if ( side == TradeType.Buy ) {
            return _keltnerChannels.Top.Last(0) - Symbol.Bid;
        } else {
            return Symbol.Bid - _keltnerChannels.Bottom.Last(0);
        }
      //  } else {
      //      return TakeProfitPips;
      //  }
    }

    private double getStopLossPips( double price, bool newMarketOrder = false ) {
        double bandSize = pipsInBand();
        // count how many active orders, but if we are going to place a new market order
        // right now bump this by one to cater for order not in system yet
        int totalOrders = Positions.Count + PendingOrders.Count + (newMarketOrder ? 1 : 0);
        if ( totalOrders == 1 ) {
            // first order always has a stop size to include basic and major wholesale region
            return bandSize * 2;
        } else {
            // second order has same stop loss as first
            if ( Positions.Count > 0 ) {
                return Math.Abs(price - Positions[0].StopLoss ?? 0);
            } else {
                return Math.Abs(price - PendingOrders[0].StopLoss ?? 0);
            }
        }
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
    }

    private void resetState() {
        Timer.Stop();
        showHideButtons(true);
        counterButton.BackgroundColor = Color.Gray;
    }

    private void showHideButtons( bool visible ) {
        buyButton.IsVisible = visible;
        sellButton.IsVisible = visible;
        closeHalfButton.IsVisible = !visible;
        counterButton.IsVisible = !visible;
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
        closeHalfButton.IsVisible = ( Positions.Count > 1 );

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
        closeHalfButton.IsVisible = ( Positions.Count > 1 );
        
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
        updatePendingOrderVolume(obj.PendingOrder);
        obj.PendingOrder.ModifyStopLossPips( getStopLossPips(obj.PendingOrder.TargetPrice) );
        obj.PendingOrder.ModifyTakeProfitPrice( obj.PendingOrder.TradeType == TradeType.Buy ? _keltnerChannels.Top.Last(0) : _keltnerChannels.Bottom.Last(0) );
    }

    private void updatePendingOrderVolume( PendingOrder order ) {
        double volume = getVolume( order.TargetPrice );
        if ( volume > 0.0 ) {
            order.ModifyVolume(volume);
        } else {
            CancelPendingOrder(order);
        }
    }
}