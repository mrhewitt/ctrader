using System;
using cAlgo.API;
using cAlgo.API.Collections;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots;

[Robot(AccessRights = AccessRights.None, AddIndicators = true)]
public class DrillTradeBar : Robot
{
    [Parameter(DefaultValue = 4.0)]
    public double StopLossPips { get; set; }
    
    [Parameter(DefaultValue = 0.01)]
    public double Lots { get; set; }

    [Parameter(DefaultValue = true)]
    public bool SplitLots { get; set; }

    private Button buyButton;
    private Button sellButton;
    private Button closeHalfButton;
    private Button counterButton;
    private Button flatButton;

    private bool noMoreOrders = false;         
    private ExponentialMovingAverage _channelTop;
    private ExponentialMovingAverage _channelBottom;


    // number of seconds left until order can be closed to count for profit
    private int secondsLeft = 120;

    protected override void OnStart()
    {
        _channelTop = Indicators.ExponentialMovingAverage(Bars.HighPrices, 5);
        _channelBottom = Indicators.ExponentialMovingAverage(Bars.LowPrices, 5);

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

    private void buy() {
        if ( !noMoreOrders ) {
            noMoreOrders = true;
            ExecuteMarketOrderAsync(TradeType.Buy, SymbolName, getVolume(), "EMA Scalper P1", StopLossPips, 0);
            if ( SplitLots ) {
                ExecuteMarketOrderAsync(TradeType.Buy, SymbolName, getVolume(), "EMA Scalper P2", StopLossPips, 0);
            }
        }
    }

    private void sell() {
        if ( !noMoreOrders ) {
            noMoreOrders = true;
            ExecuteMarketOrderAsync(TradeType.Sell, SymbolName, getVolume(), "EMA Scalper P1", StopLossPips, 0);
            if ( SplitLots ) {
                ExecuteMarketOrderAsync(TradeType.Sell, SymbolName, getVolume(), "EMA Scalper P2", StopLossPips, 0);
            }
        }
    }

    private double getVolume() {
        return Symbol.QuantityToVolumeInUnits(SplitLots ? Lots/2.0 : Lots);
    }

    private double toPips(double range) {
        return range / Symbol.TickSize / 10;
    }

    private void goFlat() {
        // close positions
        foreach (var position in Positions) {
            ClosePosition(position);
        }
    } 

    protected override void OnTick()
    {
        buyButton.IsVisible = Positions.Count == 0 && Symbol.Bid < _channelBottom.Result.Last(0);
        sellButton.IsVisible = Positions.Count == 0 && Symbol.Bid > _channelTop.Result.Last(0);
    }

    private void resetState() {
        Timer.Stop();
        noMoreOrders = false;
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
        secondsLeft = Math.Max(0,secondsLeft-1);
        if ( secondsLeft == 0 ) {
            counterButton.Text = "SAFE!";
            counterButton.BackgroundColor = Color.DarkGreen;
            Timer.Stop();
        } else {
            counterButton.Text = secondsLeft.ToString();
        }
    }

    // method is called when a position opens. 
    private void OnPositionOpened(PositionOpenedEventArgs args)
    {
        showHideButtons(false);
        closeHalfButton.IsVisible = ( Positions.Count > 1 );

        secondsLeft = 120;
        Timer.Stop();
        Timer.Start(1);
        
        counterButton.BackgroundColor = Color.DarkGoldenrod;
        counterButton.IsVisible = true;
    }

    private void OnPositionClosed(PositionClosedEventArgs obj)
    {
        closeHalfButton.IsVisible = ( Positions.Count < 2 );
        if ( Positions.Count == 0 ) {
            resetState();
        }
    }
}