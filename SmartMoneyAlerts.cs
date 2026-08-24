#region Using declarations
using System;
using System.ComponentModel;
using System.Drawing;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
#endregion

// This namespace holds all indicators and is required. Do not change it.
namespace NinjaTrader.Indicator
{
    /// <summary>
    /// Detects bullish/bearish Fair Value Gaps and Order Blocks on a fixed timeframe and fires an alert. Add as a Market Analyzer column to get FVGSignal/OBSignal values and popup/sound alerts on new signals.
    /// </summary>
    [Description("Detects bullish/bearish Fair Value Gaps and Order Blocks on a fixed timeframe and fires an alert. Add as a Market Analyzer column.")]
    public class SmartMoneyAlerts : Indicator
    {
        #region Variables
        // Wizard generated variables
        private int     timeframeMinutes    = 5;
        private int     fvgMinTicks         = 0;
        private int     obSwingStrength     = 3;
        private int     obSearchBars        = 15;
        private bool    enableFvg           = true;
        private bool    enableOrderBlock    = true;
        private bool    enableAlerts        = true;
        private string  alertSoundFile      = "Alert2.wav";
        // User defined variables (add any user defined variables below)
        private int     lastHtfBar          = -1;
        #endregion

        /// <summary>
        /// This method is used to configure the indicator and is called once before any bar data is loaded.
        /// </summary>
        protected override void Initialize()
        {
            Add(new Plot(Color.DodgerBlue, PlotStyle.Dot, "FVGSignal"));
            Add(new Plot(Color.OrangeRed, PlotStyle.Dot, "OBSignal"));

            Plots[0].Pen.Width = 3;
            Plots[1].Pen.Width = 3;

            CalculateOnBarClose = true;
            Overlay             = false;
            AutoScale           = false;

            Add(PeriodType.Minute, timeframeMinutes);
        }

        /// <summary>
        /// Called on each bar update event (incoming tick)
        /// </summary>
        protected override void OnBarUpdate()
        {
            if (CurrentBars[0] < 0 || CurrentBars[1] < 0)
                return;

            // Only evaluate once per fully closed higher timeframe bar, not on every primary tick/bar
            if (CurrentBars[1] == lastHtfBar)
                return;
            lastHtfBar = CurrentBars[1];

            double fvgSignal = 0;
            double obSignal  = 0;

            string soundPath = string.IsNullOrEmpty(alertSoundFile) ? string.Empty : Cbi.Core.InstallDir + @"\sounds\" + alertSoundFile;

            // ---- Fair Value Gap: 3 closed HTF bars, most recent = index 1 (index 0 is the still-forming bar) ----
            if (enableFvg && CurrentBars[1] >= 3)
            {
                double highA = Highs[1][3];
                double lowA  = Lows[1][3];
                double highC = Highs[1][1];
                double lowC  = Lows[1][1];
                double minGap = fvgMinTicks * TickSize;

                if (lowC > highA && (lowC - highA) >= minGap)
                {
                    fvgSignal = 1;
                    if (enableAlerts && !Historical)
                        Alert("BullFVG" + CurrentBars[1], Cbi.Priority.Medium, Instrument.FullName + ": Bullish FVG formed on " + timeframeMinutes + " min", soundPath, 0, Color.LimeGreen, Color.Black);
                }
                else if (lowA > highC && (lowA - highC) >= minGap)
                {
                    fvgSignal = -1;
                    if (enableAlerts && !Historical)
                        Alert("BearFVG" + CurrentBars[1], Cbi.Priority.Medium, Instrument.FullName + ": Bearish FVG formed on " + timeframeMinutes + " min", soundPath, 0, Color.Red, Color.White);
                }
            }

            // ---- Order Block: last closed HTF bar (index 1) breaks recent swing high/low, then walk back to the opposite colored candle ----
            if (enableOrderBlock && CurrentBars[1] >= obSwingStrength + obSearchBars + 1)
            {
                double breakoutOpen  = Opens[1][1];
                double breakoutClose = Closes[1][1];

                double recentHigh = double.MinValue;
                double recentLow  = double.MaxValue;
                for (int k = 2; k <= 1 + obSwingStrength; k++)
                {
                    if (Highs[1][k] > recentHigh) recentHigh = Highs[1][k];
                    if (Lows[1][k]  < recentLow)  recentLow  = Lows[1][k];
                }

                bool bullishBreak = breakoutClose > breakoutOpen && breakoutClose > recentHigh;
                bool bearishBreak = breakoutClose < breakoutOpen && breakoutClose < recentLow;

                if (bullishBreak)
                {
                    for (int k = 2; k <= obSearchBars + 1; k++)
                    {
                        if (Closes[1][k] < Opens[1][k])
                        {
                            obSignal = 1;
                            if (enableAlerts && !Historical)
                                Alert("BullOB" + CurrentBars[1], Cbi.Priority.Medium, Instrument.FullName + ": Bullish Order Block confirmed on " + timeframeMinutes + " min", soundPath, 0, Color.LimeGreen, Color.Black);
                            break;
                        }
                    }
                }
                else if (bearishBreak)
                {
                    for (int k = 2; k <= obSearchBars + 1; k++)
                    {
                        if (Closes[1][k] > Opens[1][k])
                        {
                            obSignal = -1;
                            if (enableAlerts && !Historical)
                                Alert("BearOB" + CurrentBars[1], Cbi.Priority.Medium, Instrument.FullName + ": Bearish Order Block confirmed on " + timeframeMinutes + " min", soundPath, 0, Color.Red, Color.White);
                            break;
                        }
                    }
                }
            }

            FVGSignal.Set(fvgSignal);
            OBSignal.Set(obSignal);
        }

        #region Properties
        [Browsable(false)]	// this line prevents the data series from being displayed in the indicator properties dialog, do not remove
        [XmlIgnore()]		// this line ensures that the indicator can be saved/recovered as part of a chart template, do not remove
        public DataSeries FVGSignal
        {
            get { return Values[0]; }
        }

        [Browsable(false)]	// this line prevents the data series from being displayed in the indicator properties dialog, do not remove
        [XmlIgnore()]		// this line ensures that the indicator can be saved/recovered as part of a chart template, do not remove
        public DataSeries OBSignal
        {
            get { return Values[1]; }
        }

        [Description("Fixed timeframe (in minutes) that FVG/Order Block detection runs on, regardless of the chart or Market Analyzer row period")]
        [GridCategory("Parameters")]
        public int TimeframeMinutes
        {
            get { return timeframeMinutes; }
            set { timeframeMinutes = Math.Max(1, value); }
        }

        [Description("Minimum FVG gap size in ticks required to trigger a signal (0 = any gap)")]
        [GridCategory("Parameters")]
        public int FvgMinTicks
        {
            get { return fvgMinTicks; }
            set { fvgMinTicks = Math.Max(0, value); }
        }

        [Description("Bars each side used to define the recent swing high/low that must be broken to confirm an Order Block")]
        [GridCategory("Parameters")]
        public int ObSwingStrength
        {
            get { return obSwingStrength; }
            set { obSwingStrength = Math.Max(1, value); }
        }

        [Description("Maximum bars to search backward for the opposite-colored candle that forms the Order Block")]
        [GridCategory("Parameters")]
        public int ObSearchBars
        {
            get { return obSearchBars; }
            set { obSearchBars = Math.Max(1, value); }
        }

        [Description("Enable Fair Value Gap detection")]
        [GridCategory("Parameters")]
        public bool EnableFvg
        {
            get { return enableFvg; }
            set { enableFvg = value; }
        }

        [Description("Enable Order Block detection")]
        [GridCategory("Parameters")]
        public bool EnableOrderBlock
        {
            get { return enableOrderBlock; }
            set { enableOrderBlock = value; }
        }

        [Description("Pop up the NinjaTrader Alerts window (with sound) on new bullish/bearish signals")]
        [GridCategory("Parameters")]
        public bool EnableAlerts
        {
            get { return enableAlerts; }
            set { enableAlerts = value; }
        }

        [Description("Sound file (from the NinjaTrader \\sounds folder) played on alert. Leave blank for no sound")]
        [GridCategory("Parameters")]
        public string AlertSoundFile
        {
            get { return alertSoundFile; }
            set { alertSoundFile = value; }
        }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.
// This namespace holds all indicators and is required. Do not change it.
namespace NinjaTrader.Indicator
{
    public partial class Indicator : IndicatorBase
    {
        private SmartMoneyAlerts[] cacheSmartMoneyAlerts = null;

        private static SmartMoneyAlerts checkSmartMoneyAlerts = new SmartMoneyAlerts();

        /// <summary>
        /// Detects bullish/bearish Fair Value Gaps and Order Blocks on a fixed timeframe and fires an alert. Add as a Market Analyzer column.
        /// </summary>
        /// <returns></returns>
        public SmartMoneyAlerts SmartMoneyAlerts(int timeframeMinutes, int fvgMinTicks, int obSwingStrength, int obSearchBars, bool enableFvg, bool enableOrderBlock, bool enableAlerts, string alertSoundFile)
        {
            return SmartMoneyAlerts(Input, timeframeMinutes, fvgMinTicks, obSwingStrength, obSearchBars, enableFvg, enableOrderBlock, enableAlerts, alertSoundFile);
        }

        /// <summary>
        /// Detects bullish/bearish Fair Value Gaps and Order Blocks on a fixed timeframe and fires an alert. Add as a Market Analyzer column.
        /// </summary>
        /// <returns></returns>
        public SmartMoneyAlerts SmartMoneyAlerts(Data.IDataSeries input, int timeframeMinutes, int fvgMinTicks, int obSwingStrength, int obSearchBars, bool enableFvg, bool enableOrderBlock, bool enableAlerts, string alertSoundFile)
        {
            if (cacheSmartMoneyAlerts != null)
                for (int idx = 0; idx < cacheSmartMoneyAlerts.Length; idx++)
                    if (cacheSmartMoneyAlerts[idx].TimeframeMinutes == timeframeMinutes && cacheSmartMoneyAlerts[idx].FvgMinTicks == fvgMinTicks && cacheSmartMoneyAlerts[idx].ObSwingStrength == obSwingStrength && cacheSmartMoneyAlerts[idx].ObSearchBars == obSearchBars && cacheSmartMoneyAlerts[idx].EnableFvg == enableFvg && cacheSmartMoneyAlerts[idx].EnableOrderBlock == enableOrderBlock && cacheSmartMoneyAlerts[idx].EnableAlerts == enableAlerts && cacheSmartMoneyAlerts[idx].AlertSoundFile == alertSoundFile && cacheSmartMoneyAlerts[idx].EqualsInput(input))
                        return cacheSmartMoneyAlerts[idx];

            lock (checkSmartMoneyAlerts)
            {
                checkSmartMoneyAlerts.TimeframeMinutes = timeframeMinutes;
                timeframeMinutes = checkSmartMoneyAlerts.TimeframeMinutes;
                checkSmartMoneyAlerts.FvgMinTicks = fvgMinTicks;
                fvgMinTicks = checkSmartMoneyAlerts.FvgMinTicks;
                checkSmartMoneyAlerts.ObSwingStrength = obSwingStrength;
                obSwingStrength = checkSmartMoneyAlerts.ObSwingStrength;
                checkSmartMoneyAlerts.ObSearchBars = obSearchBars;
                obSearchBars = checkSmartMoneyAlerts.ObSearchBars;
                checkSmartMoneyAlerts.EnableFvg = enableFvg;
                enableFvg = checkSmartMoneyAlerts.EnableFvg;
                checkSmartMoneyAlerts.EnableOrderBlock = enableOrderBlock;
                enableOrderBlock = checkSmartMoneyAlerts.EnableOrderBlock;
                checkSmartMoneyAlerts.EnableAlerts = enableAlerts;
                enableAlerts = checkSmartMoneyAlerts.EnableAlerts;
                checkSmartMoneyAlerts.AlertSoundFile = alertSoundFile;
                alertSoundFile = checkSmartMoneyAlerts.AlertSoundFile;

                if (cacheSmartMoneyAlerts != null)
                    for (int idx = 0; idx < cacheSmartMoneyAlerts.Length; idx++)
                        if (cacheSmartMoneyAlerts[idx].TimeframeMinutes == timeframeMinutes && cacheSmartMoneyAlerts[idx].FvgMinTicks == fvgMinTicks && cacheSmartMoneyAlerts[idx].ObSwingStrength == obSwingStrength && cacheSmartMoneyAlerts[idx].ObSearchBars == obSearchBars && cacheSmartMoneyAlerts[idx].EnableFvg == enableFvg && cacheSmartMoneyAlerts[idx].EnableOrderBlock == enableOrderBlock && cacheSmartMoneyAlerts[idx].EnableAlerts == enableAlerts && cacheSmartMoneyAlerts[idx].AlertSoundFile == alertSoundFile && cacheSmartMoneyAlerts[idx].EqualsInput(input))
                            return cacheSmartMoneyAlerts[idx];

                SmartMoneyAlerts indicator = new SmartMoneyAlerts();
                indicator.BarsRequired = BarsRequired;
                indicator.CalculateOnBarClose = CalculateOnBarClose;
#if NT7
                indicator.ForceMaximumBarsLookBack256 = ForceMaximumBarsLookBack256;
                indicator.MaximumBarsLookBack = MaximumBarsLookBack;
#endif
                indicator.Input = input;
                indicator.TimeframeMinutes = timeframeMinutes;
                indicator.FvgMinTicks = fvgMinTicks;
                indicator.ObSwingStrength = obSwingStrength;
                indicator.ObSearchBars = obSearchBars;
                indicator.EnableFvg = enableFvg;
                indicator.EnableOrderBlock = enableOrderBlock;
                indicator.EnableAlerts = enableAlerts;
                indicator.AlertSoundFile = alertSoundFile;
                Indicators.Add(indicator);
                indicator.SetUp();

                SmartMoneyAlerts[] tmp = new SmartMoneyAlerts[cacheSmartMoneyAlerts == null ? 1 : cacheSmartMoneyAlerts.Length + 1];
                if (cacheSmartMoneyAlerts != null)
                    cacheSmartMoneyAlerts.CopyTo(tmp, 0);
                tmp[tmp.Length - 1] = indicator;
                cacheSmartMoneyAlerts = tmp;
                return indicator;
            }
        }
    }
}

// This namespace holds all market analyzer column definitions and is required. Do not change it.
namespace NinjaTrader.MarketAnalyzer
{
    public partial class Column : ColumnBase
    {
        /// <summary>
        /// Detects bullish/bearish Fair Value Gaps and Order Blocks on a fixed timeframe and fires an alert. Add as a Market Analyzer column.
        /// </summary>
        /// <returns></returns>
        [Gui.Design.WizardCondition("Indicator")]
        public Indicator.SmartMoneyAlerts SmartMoneyAlerts(int timeframeMinutes, int fvgMinTicks, int obSwingStrength, int obSearchBars, bool enableFvg, bool enableOrderBlock, bool enableAlerts, string alertSoundFile)
        {
            return _indicator.SmartMoneyAlerts(Input, timeframeMinutes, fvgMinTicks, obSwingStrength, obSearchBars, enableFvg, enableOrderBlock, enableAlerts, alertSoundFile);
        }

        /// <summary>
        /// Detects bullish/bearish Fair Value Gaps and Order Blocks on a fixed timeframe and fires an alert. Add as a Market Analyzer column.
        /// </summary>
        /// <returns></returns>
        public Indicator.SmartMoneyAlerts SmartMoneyAlerts(Data.IDataSeries input, int timeframeMinutes, int fvgMinTicks, int obSwingStrength, int obSearchBars, bool enableFvg, bool enableOrderBlock, bool enableAlerts, string alertSoundFile)
        {
            return _indicator.SmartMoneyAlerts(input, timeframeMinutes, fvgMinTicks, obSwingStrength, obSearchBars, enableFvg, enableOrderBlock, enableAlerts, alertSoundFile);
        }
    }
}

// This namespace holds all strategies and is required. Do not change it.
namespace NinjaTrader.Strategy
{
    public partial class Strategy : StrategyBase
    {
        /// <summary>
        /// Detects bullish/bearish Fair Value Gaps and Order Blocks on a fixed timeframe and fires an alert. Add as a Market Analyzer column.
        /// </summary>
        /// <returns></returns>
        [Gui.Design.WizardCondition("Indicator")]
        public Indicator.SmartMoneyAlerts SmartMoneyAlerts(int timeframeMinutes, int fvgMinTicks, int obSwingStrength, int obSearchBars, bool enableFvg, bool enableOrderBlock, bool enableAlerts, string alertSoundFile)
        {
            return _indicator.SmartMoneyAlerts(Input, timeframeMinutes, fvgMinTicks, obSwingStrength, obSearchBars, enableFvg, enableOrderBlock, enableAlerts, alertSoundFile);
        }

        /// <summary>
        /// Detects bullish/bearish Fair Value Gaps and Order Blocks on a fixed timeframe and fires an alert. Add as a Market Analyzer column.
        /// </summary>
        /// <returns></returns>
        public Indicator.SmartMoneyAlerts SmartMoneyAlerts(Data.IDataSeries input, int timeframeMinutes, int fvgMinTicks, int obSwingStrength, int obSearchBars, bool enableFvg, bool enableOrderBlock, bool enableAlerts, string alertSoundFile)
        {
            if (InInitialize && input == null)
                throw new ArgumentException("You only can access an indicator with the default input/bar series from within the 'Initialize()' method");

            return _indicator.SmartMoneyAlerts(input, timeframeMinutes, fvgMinTicks, obSwingStrength, obSearchBars, enableFvg, enableOrderBlock, enableAlerts, alertSoundFile);
        }
    }
}
#endregion
