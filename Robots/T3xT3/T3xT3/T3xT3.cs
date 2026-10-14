namespace cAlgo.Robots;

using System.Linq;

[Robot(AccessRights = AccessRights.None, AddIndicators = false)]
public  class T3xT3 : Robot
{

    /**
     * Basic trade size
     */
    [Parameter(DefaultValue = 0.01)]
    public double TradeLots { get; set; }
    
    /**
     * If not zero every second trade will be this size
     * Use with TradeLots to make up odd numbers lots when scaling higher
     * e.g  if 0.01 lots per 1k, but half size for prop, on a 5k we want 0.005*5 = 0.025
     * which is not available, so lots of 0.01 and 0.02 averages to (0.02+0.03)=0.05/2 => 0.02    
     */
    [Parameter(DefaultValue = 0.0)]
    public double SecondTradeLots { get; set; }

    [Parameter(DefaultValue = 23)]
    public int NoTradeHour { get; set; }

    [Parameter(DefaultValue = 90)]
    public double MaxDrawDown { get; set; }

    [Parameter(DefaultValue = 0.0004)]
    public double MinTradeSpacing { get; set; }

    [Parameter(DefaultValue = false)]
    public bool CloseOnly { get; set; }

    private T3MADynamicEnvelope _envelope;

    // becomes false after a trade is entered to prevent placing another trade
    // until after the price goes back into the channel
    private bool readyToTrade = true;

    private ChartStaticText statusLabel;

    protected override void OnStart()
    { 
        _envelope = Indicators.GetIndicator<T3MADynamicEnvelope>();
        statusLabel = Chart.DrawStaticText("t3xstatus", "System Starting...", VerticalAlignment.Top, HorizontalAlignment.Right, Color.DimGray);
    }

    protected override void OnTick()
    {
        if ( Positions.Count > 0 ) {
            attemptClosePositions();
        }

        // if there are still positions open, then ensure that we have not exceeded max loss
        if ( Positions.Count > 0 ) {
            handleMaxDrawdown();
        }

        showStatus();
    }

    protected override void OnBar() {
        // when we are in close only, we can only complete current position, we cannot start a new one
        if ( (!CloseOnly || Positions.Count > 0) && !isOutOfHours() ) {
            double openPrice = Bars.OpenPrices.Last(1);
            double closePrice = Bars.ClosePrices.Last(1);
            double upperBand = getT3Upper();
            double lowerBand = getT3Lower();

            if ( readyToTrade ) {
                double lots = ( Positions.Count > 1 && Positions.Count % 2 == 0 ? SecondTradeLots : TradeLots );
                if ( isBearBar() ) {
                    if ( openPrice > upperBand && meetsMinSpacing(Symbol.Bid) ) {
                        openPosition(TradeType.Sell, lots);
                    }
                } else {
                    if ( openPrice < lowerBand && meetsMinSpacing(Symbol.Ask) ) {
                        openPosition(TradeType.Buy, lots);
                    }                
                }
            } else {
                // went a complete bar inside the channel before we can trade again
                readyToTrade = (closePrice < upperBand && closePrice > lowerBand && openPrice < upperBand && openPrice > lowerBand); 
            }
        } else {
            Print("Out of hours or close only, cannot trade");
        }
    }

    protected override void OnStop()
    {
        // Handle cBot stop here
    }

    private void showStatus() {
        string label = "";
        if ( CloseOnly ) {
            label = "CLOSE ONLY - ";
        }

        if ( Positions.Count > 0 ) {
            double netProfit = getNetProfit();
            if ( netProfit < 0 ) {
                double drawdownLevel = (Math.Abs(netProfit)/MaxDrawDown) * 100;
                if ( drawdownLevel < 25 ) {
                    statusLabel.Color = Color.Green;
                } else if ( drawdownLevel < 75 ) {
                    statusLabel.Color = Color.Orange;
                } else {
                    statusLabel.Color = Color.Red;
                }
                label += FormattableString.Invariant($"Drawdown Level: {drawdownLevel:0.00}% - ");
            } else {
                statusLabel.Color = Color.DimGray;
                label += "No Drawdown - ";
            }

            label += FormattableString.Invariant($"Net Profit: ${netProfit:0.00} -");
        }

        if ( readyToTrade ) {
            label += "Waiting for entry";
        } else {
            label += "Return to Channel";
        }

        statusLabel.Text = label;
    }

    private double getNetProfit() {
        double netProfit = 0;    
        foreach ( Position part in Positions ) {
            netProfit += part.NetProfit;
        }
        return netProfit;
    }

    /**
     * If we have exceeded the max allowable loss limit class the oldest position as this will be most in drawdown and have least chance of going positive
     */
    private void handleMaxDrawdown() {
        if ( getNetProfit() < MaxDrawDown*-1 ) {
            ClosePosition(Positions.First());
        }
    }

    /**
     * Attemp to close all open positions
     */
    private void attemptClosePositions() {
        Position trade = Positions.First();
        if ( (trade.TradeType == TradeType.Buy && Symbol.Bid >= getT3Upper()) || (trade.TradeType == TradeType.Sell && Symbol.Bid <= getT3Lower()) ) {
            foreach ( Position part in Positions ) {
                ClosePosition(part);
            }
        }                
    }

    private void openPosition( TradeType type, double lots = 0.01 ) {
        double price = type == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
        ExecuteMarketOrderAsync(type, SymbolName, Symbol.QuantityToVolumeInUnits(lots));
        // after we place an order we need to return to the channel before we can place another
        readyToTrade = false;
    }

    private double getT3Upper() {
        return _envelope.Upper.Last(0);
    }

    private double getT3Lower() {
        return _envelope.Lower.Last(0);
    }

    /**
     * Returns true is the given price is MinTradeSpacing away from the last trade
     */
    private bool meetsMinSpacing( double price ) {
        if ( Positions.Count > 0 ) {
            double gap = Math.Abs( Positions.Last().EntryPrice - price );
            Print("Last trade at: ", Positions.Last().EntryPrice, "; gap: ", gap);
            return gap > MinTradeSpacing;
        } else {
            return true;
        }
    }

    private bool isHighestBid() {
        foreach ( Position part in Positions ) {
            if ( part.EntryPrice >= Symbol.Bid ) { return false; }
        }

        return true;
    }

    private bool isOutOfHours() {
        return Bars.LastBar.OpenTime.Hour == NoTradeHour;
    }

    private bool isHLowestAsk() {
        foreach ( Position part in Positions ) {
            if ( part.EntryPrice <= Symbol.Ask ) { return false; }
        }

        return true;
    }

    private bool isBullBar( int barsBack = 1 ) {
        return Bars.Last(barsBack).Close > Bars.Last(barsBack).Open;
    }

    private bool isBearBar( int barsBack = 1 ) {
        return Bars.Last(barsBack).Close < Bars.Last(barsBack).Open;
    }
}