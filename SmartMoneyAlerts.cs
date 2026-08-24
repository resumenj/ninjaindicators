#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

// This namespace holds indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// Detects bullish/bearish Fair Value Gaps and Order Blocks on a fixed timeframe and fires an Alert. Add as a Market Analyzer column (FVGSignal / OBSignal plots, -1/0/1).
    /// </summary>
    public class SmartMoneyAlerts : Indicator
    {
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                = "Detects bullish/bearish Fair Value Gaps and Order Blocks on a fixed timeframe and fires an Alert. Add as a Market Analyzer column.";
                Name                        = "SmartMoneyAlerts";
                Calculate                    = Calculate.OnBarClose;
                IsOverlay                    = false;
                DisplayInDataBox            = true;
                DrawOnPricePanel            = false;
                PaintPriceMarkers            = true;
                ScaleJustification            = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive    = true;

                TimeframeMinutes    = 5;
                FvgMinTicks            = 0;
                ObSwingStrength        = 3;
                ObSearchBars        = 15;
                EnableFvg            = true;
                EnableOrderBlock    = true;
                EnableAlerts        = true;
                AlertSoundFile        = "Alert2.wav";

                AddPlot(Brushes.DodgerBlue, "FVGSignal");
                AddPlot(Brushes.OrangeRed, "OBSignal");
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, TimeframeMinutes);
            }
        }

        protected override void OnBarUpdate()
        {
            // Only react to the fixed higher timeframe series closing a bar; ignore the primary series update
            if (BarsInProgress != 1)
                return;

            if (CurrentBars[1] < 0)
                return;

            double fvgSignal = 0;
            double obSignal  = 0;

            string soundPath = string.IsNullOrEmpty(AlertSoundFile) ? string.Empty : NinjaTrader.Core.Globals.InstallDir + @"\sounds\" + AlertSoundFile;

            // ---- Fair Value Gap: 3 most recently closed HTF bars. Under Calculate.OnBarClose, index 0 is the bar that just closed. ----
            if (EnableFvg && CurrentBars[1] >= 2)
            {
                double highA = Highs[1][2];
                double lowA  = Lows[1][2];
                double highC = Highs[1][0];
                double lowC  = Lows[1][0];
                double minGap = FvgMinTicks * TickSize;

                if (lowC > highA && (lowC - highA) >= minGap)
                {
                    fvgSignal = 1;
                    if (EnableAlerts && State == State.Realtime)
                        Alert("BullFVG" + CurrentBars[1], Priority.Medium, Instrument.FullName + ": Bullish FVG formed on " + TimeframeMinutes + " min", soundPath, 0, Brushes.LimeGreen, Brushes.Black);
                }
                else if (lowA > highC && (lowA - highC) >= minGap)
                {
                    fvgSignal = -1;
                    if (EnableAlerts && State == State.Realtime)
                        Alert("BearFVG" + CurrentBars[1], Priority.Medium, Instrument.FullName + ": Bearish FVG formed on " + TimeframeMinutes + " min", soundPath, 0, Brushes.Red, Brushes.White);
                }
            }

            // ---- Order Block: newest closed HTF bar (index 0) breaks the recent swing high/low, then walk back to the opposite colored candle ----
            if (EnableOrderBlock && CurrentBars[1] >= Math.Max(ObSwingStrength, ObSearchBars))
            {
                double breakoutOpen  = Opens[1][0];
                double breakoutClose = Closes[1][0];

                double recentHigh = double.MinValue;
                double recentLow  = double.MaxValue;
                for (int k = 1; k <= ObSwingStrength; k++)
                {
                    if (Highs[1][k] > recentHigh) recentHigh = Highs[1][k];
                    if (Lows[1][k]  < recentLow)  recentLow  = Lows[1][k];
                }

                bool bullishBreak = breakoutClose > breakoutOpen && breakoutClose > recentHigh;
                bool bearishBreak = breakoutClose < breakoutOpen && breakoutClose < recentLow;

                if (bullishBreak)
                {
                    for (int k = 1; k <= ObSearchBars; k++)
                    {
                        if (Closes[1][k] < Opens[1][k])
                        {
                            obSignal = 1;
                            if (EnableAlerts && State == State.Realtime)
                                Alert("BullOB" + CurrentBars[1], Priority.Medium, Instrument.FullName + ": Bullish Order Block confirmed on " + TimeframeMinutes + " min", soundPath, 0, Brushes.LimeGreen, Brushes.Black);
                            break;
                        }
                    }
                }
                else if (bearishBreak)
                {
                    for (int k = 1; k <= ObSearchBars; k++)
                    {
                        if (Closes[1][k] > Opens[1][k])
                        {
                            obSignal = -1;
                            if (EnableAlerts && State == State.Realtime)
                                Alert("BearOB" + CurrentBars[1], Priority.Medium, Instrument.FullName + ": Bearish Order Block confirmed on " + TimeframeMinutes + " min", soundPath, 0, Brushes.Red, Brushes.White);
                            break;
                        }
                    }
                }
            }

            Values[0][0] = fvgSignal;
            Values[1][0] = obSignal;
        }

        #region Properties
        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Timeframe (minutes)", Description = "Fixed timeframe that FVG/Order Block detection runs on, regardless of the chart or Market Analyzer row period", GroupName = "Parameters", Order = 0)]
        public int TimeframeMinutes
        { get; set; }

        [Range(0, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "FVG min gap (ticks)", Description = "Minimum gap size in ticks required to trigger an FVG signal (0 = any gap)", GroupName = "Parameters", Order = 1)]
        public int FvgMinTicks
        { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "OB swing strength", Description = "Bars used to define the recent swing high/low that must be broken to confirm an Order Block", GroupName = "Parameters", Order = 2)]
        public int ObSwingStrength
        { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "OB search bars", Description = "Maximum bars to search backward for the opposite-colored candle that forms the Order Block", GroupName = "Parameters", Order = 3)]
        public int ObSearchBars
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable FVG", GroupName = "Parameters", Order = 4)]
        public bool EnableFvg
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Order Block", GroupName = "Parameters", Order = 5)]
        public bool EnableOrderBlock
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Alerts", Description = "Pop up the NinjaTrader Alerts window (with sound) on new bullish/bearish signals", GroupName = "Parameters", Order = 6)]
        public bool EnableAlerts
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Alert sound file", Description = "Sound file (from the NinjaTrader \\sounds folder) played on alert. Leave blank for no sound", GroupName = "Parameters", Order = 7)]
        public string AlertSoundFile
        { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> FVGSignal
        {
            get { return Values[0]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> OBSignal
        {
            get { return Values[1]; }
        }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
    {
        private SmartMoneyAlerts[] cacheSmartMoneyAlerts;
        public SmartMoneyAlerts SmartMoneyAlerts(int timeframeMinutes, int fvgMinTicks, int obSwingStrength, int obSearchBars, bool enableFvg, bool enableOrderBlock, bool enableAlerts, string alertSoundFile)
        {
            return SmartMoneyAlerts(Input, timeframeMinutes, fvgMinTicks, obSwingStrength, obSearchBars, enableFvg, enableOrderBlock, enableAlerts, alertSoundFile);
        }

        public SmartMoneyAlerts SmartMoneyAlerts(ISeries<double> input, int timeframeMinutes, int fvgMinTicks, int obSwingStrength, int obSearchBars, bool enableFvg, bool enableOrderBlock, bool enableAlerts, string alertSoundFile)
        {
            if (cacheSmartMoneyAlerts != null)
                for (int idx = 0; idx < cacheSmartMoneyAlerts.Length; idx++)
                    if (cacheSmartMoneyAlerts[idx] != null && cacheSmartMoneyAlerts[idx].TimeframeMinutes == timeframeMinutes && cacheSmartMoneyAlerts[idx].FvgMinTicks == fvgMinTicks && cacheSmartMoneyAlerts[idx].ObSwingStrength == obSwingStrength && cacheSmartMoneyAlerts[idx].ObSearchBars == obSearchBars && cacheSmartMoneyAlerts[idx].EnableFvg == enableFvg && cacheSmartMoneyAlerts[idx].EnableOrderBlock == enableOrderBlock && cacheSmartMoneyAlerts[idx].EnableAlerts == enableAlerts && cacheSmartMoneyAlerts[idx].AlertSoundFile == alertSoundFile && cacheSmartMoneyAlerts[idx].EqualsInput(input))
                        return cacheSmartMoneyAlerts[idx];
            return CacheIndicator<SmartMoneyAlerts>(new SmartMoneyAlerts() { TimeframeMinutes = timeframeMinutes, FvgMinTicks = fvgMinTicks, ObSwingStrength = obSwingStrength, ObSearchBars = obSearchBars, EnableFvg = enableFvg, EnableOrderBlock = enableOrderBlock, EnableAlerts = enableAlerts, AlertSoundFile = alertSoundFile }, input, ref cacheSmartMoneyAlerts);
        }
    }
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
    public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
    {
        public Indicators.SmartMoneyAlerts SmartMoneyAlerts(int timeframeMinutes, int fvgMinTicks, int obSwingStrength, int obSearchBars, bool enableFvg, bool enableOrderBlock, bool enableAlerts, string alertSoundFile)
        {
            return indicator.SmartMoneyAlerts(Input, timeframeMinutes, fvgMinTicks, obSwingStrength, obSearchBars, enableFvg, enableOrderBlock, enableAlerts, alertSoundFile);
        }

        public Indicators.SmartMoneyAlerts SmartMoneyAlerts(ISeries<double> input, int timeframeMinutes, int fvgMinTicks, int obSwingStrength, int obSearchBars, bool enableFvg, bool enableOrderBlock, bool enableAlerts, string alertSoundFile)
        {
            return indicator.SmartMoneyAlerts(input, timeframeMinutes, fvgMinTicks, obSwingStrength, obSearchBars, enableFvg, enableOrderBlock, enableAlerts, alertSoundFile);
        }
    }
}

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
    {
        public Indicators.SmartMoneyAlerts SmartMoneyAlerts(int timeframeMinutes, int fvgMinTicks, int obSwingStrength, int obSearchBars, bool enableFvg, bool enableOrderBlock, bool enableAlerts, string alertSoundFile)
        {
            return indicator.SmartMoneyAlerts(Input, timeframeMinutes, fvgMinTicks, obSwingStrength, obSearchBars, enableFvg, enableOrderBlock, enableAlerts, alertSoundFile);
        }

        public Indicators.SmartMoneyAlerts SmartMoneyAlerts(ISeries<double> input, int timeframeMinutes, int fvgMinTicks, int obSwingStrength, int obSearchBars, bool enableFvg, bool enableOrderBlock, bool enableAlerts, string alertSoundFile)
        {
            return indicator.SmartMoneyAlerts(input, timeframeMinutes, fvgMinTicks, obSwingStrength, obSearchBars, enableFvg, enableOrderBlock, enableAlerts, alertSoundFile);
        }
    }
}

#endregion
