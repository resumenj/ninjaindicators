#region Using declarations
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using ZZMarkovETF.Utility;
#endregion

// This namespace holds all indicators and is required. Do not change it.
namespace NinjaTrader.Indicator
{
    /// <summary>
    /// Observable (fully visible, rule based) Markov chain for ETFs.
    ///
    /// Each bar is put in a discrete state by fixed rules, so the state is known with
    /// certainty (no hidden states, no fitting). The transition matrix is estimated
    /// from the last Window transitions only (rolling counts, Laplace smoothed), and
    /// the row of the current state gives the next-bar outlook.
    ///
    /// State rules:
    ///   TrendVolatility (4 states)
    ///     Trend      : Input > SMA(TrendPeriod)                     -> Bull, else Bear
    ///     Volatility : StdDev(log returns, VolPeriod) > its SMA(VolLookback) -> Volatile, else Quiet
    ///     0 = Bear-Volatile, 1 = Bear-Quiet, 2 = Bull-Quiet, 3 = Bull-Volatile
    ///   ReturnBands (3 states)
    ///     sigma = StdDev(log returns, VolPeriod) of the prior bar
    ///     0 = Down (r < -BandK*sigma), 1 = Flat, 2 = Up (r > +BandK*sigma)
    ///
    /// Plots    : PBullNext (P next state is bullish), PStay (P[s,s]), StationaryBull
    ///            (long run share of bullish states under the current rolling matrix).
    /// Exposed  : State, ExpectedReturn (mean next-bar log return from the current state
    ///            over the window), ExpectedDuration (1 / (1 - P[s,s]) bars),
    ///            TransitionProbability(from, to).
    ///
    /// Transitions are only committed from closed bars, so with CalculateOnBarClose = false
    /// the matrix lags one bar while State/plots still track the forming bar.
    /// </summary>
    [Description("Observable Markov chain for ETFs: rule based states, rolling transition matrix")]
    public class ZZMarkovETF : Indicator
    {
        #region Variables
        private ZZMarkovStateRule stateRule = ZZMarkovStateRule.TrendVolatility;
        private int trendPeriod = 200;
        private int volPeriod = 20;
        private int volLookback = 100;
        private double bandK = 0.5;
        private int window = 252;
        private double smoothing = 1.0;
        private int minTransitions = 60;

        private bool colorBackground = true;
        private int backgroundOpacity = 40;
        private bool showMatrix = true;

        private const int MaxStates = 4;

        private DataSeries retSeries;
        private DataSeries volSeries;
        private DataSeries stateSeries;
        private DataSeries expectedReturnSeries;
        private DataSeries expectedDurationSeries;

        private int numStates;
        private int warmup;
        private int[,] counts = new int[MaxStates, MaxStates];
        private int[] rowCounts = new int[MaxStates];
        private double[] rowReturnSums = new double[MaxStates];
        private int totalTransitions;
        private double[] stationary = new double[MaxStates];
        private Font matrixFont = new Font("Courier New", 8);
        #endregion

        /// <summary>
        /// This method is used to configure the indicator and is called once before any bar data is loaded.
        /// </summary>
        protected override void Initialize()
        {
            Add(new Plot(Color.FromKnownColor(KnownColor.Green), PlotStyle.Line, "PBullNext"));
            Add(new Plot(Color.FromKnownColor(KnownColor.Blue), PlotStyle.Line, "PStay"));
            Add(new Plot(Color.FromKnownColor(KnownColor.DarkOrange), PlotStyle.Line, "StationaryBull"));
            Add(new Line(Color.FromKnownColor(KnownColor.DarkGray), 0.5, "Half"));

            retSeries = new DataSeries(this);
            volSeries = new DataSeries(this);
            stateSeries = new DataSeries(this);
            expectedReturnSeries = new DataSeries(this);
            expectedDurationSeries = new DataSeries(this);

            // the rolling window reaches back Window + 2 bars
            MaximumBarsLookBack = MaximumBarsLookBack.Infinite;
            Overlay = false;
        }

        protected override void OnStartUp()
        {
            numStates = stateRule == ZZMarkovStateRule.TrendVolatility ? 4 : 3;
            if (stateRule == ZZMarkovStateRule.TrendVolatility)
                warmup = Math.Max(trendPeriod, volPeriod + volLookback);
            else
                warmup = volPeriod + 1;
        }

        /// <summary>
        /// Called on each bar update event (incoming tick)
        /// </summary>
        protected override void OnBarUpdate()
        {
            retSeries.Set(CurrentBar < 1 ? 0 : Math.Log(Input[0] / Input[1]));
            volSeries.Set(StdDev(retSeries, volPeriod)[0]);

            if (CurrentBar < warmup)
                return;

            int state = ClassifyState();
            stateSeries.Set(state);

            // commit the transition that ended on the most recent closed bar, drop the one leaving the window
            int k = CalculateOnBarClose ? 0 : 1;
            if (FirstTickOfBar && IsValidState(k + 1))
            {
                AddTransition(k, 1);
                if (IsValidState(k + window + 1))
                    AddTransition(k + window, -1);
            }

            ColorState(state);

            if (totalTransitions < minTransitions)
                return;

            double pStay = Probability(state, state);
            double pBull = 0;
            for (int j = 0; j < numStates; j++)
                if (IsBullish(j))
                    pBull += Probability(state, j);

            ComputeStationary();
            double stationaryBull = 0;
            for (int j = 0; j < numStates; j++)
                if (IsBullish(j))
                    stationaryBull += stationary[j];

            PBullNext.Set(pBull);
            PStay.Set(pStay);
            StationaryBull.Set(stationaryBull);
            expectedReturnSeries.Set(rowCounts[state] > 0 ? rowReturnSums[state] / rowCounts[state] : 0);
            expectedDurationSeries.Set(1.0 / Math.Max(1e-9, 1.0 - pStay));

            if (showMatrix && CurrentBar >= Count - 2)
                DrawTextFixed("ZZMarkovETFMatrix", MatrixText(state), TextPosition.TopLeft, Color.Black, matrixFont, Color.Transparent, Color.White, 6);
        }

        private int ClassifyState()
        {
            if (stateRule == ZZMarkovStateRule.TrendVolatility)
            {
                bool bull = Input[0] > SMA(Input, trendPeriod)[0];
                bool volatile_ = volSeries[0] > SMA(volSeries, volLookback)[0];
                if (bull)
                    return volatile_ ? 3 : 2;
                return volatile_ ? 0 : 1;
            }

            // sigma from the prior bar so today's return does not widen its own band
            double band = bandK * volSeries[1];
            if (retSeries[0] > band)
                return 2;
            if (retSeries[0] < -band)
                return 0;
            return 1;
        }

        private bool IsValidState(int barsAgo)
        {
            return CurrentBar - barsAgo >= warmup;
        }

        // transition ending at barsAgo: state[barsAgo + 1] -> state[barsAgo], realised return retSeries[barsAgo]
        private void AddTransition(int barsAgo, int sign)
        {
            int from = (int)stateSeries[barsAgo + 1];
            int to = (int)stateSeries[barsAgo];
            counts[from, to] += sign;
            rowCounts[from] += sign;
            rowReturnSums[from] += sign * retSeries[barsAgo];
            totalTransitions += sign;
        }

        private double Probability(int from, int to)
        {
            return (counts[from, to] + smoothing) / (rowCounts[from] + smoothing * numStates);
        }

        // power iteration on the smoothed matrix (irreducible when smoothing > 0)
        private void ComputeStationary()
        {
            double[] next = new double[MaxStates];
            for (int i = 0; i < numStates; i++)
                stationary[i] = 1.0 / numStates;

            for (int iter = 0; iter < 200; iter++)
            {
                double diff = 0;
                for (int j = 0; j < numStates; j++)
                {
                    next[j] = 0;
                    for (int i = 0; i < numStates; i++)
                        next[j] += stationary[i] * Probability(i, j);
                }
                for (int j = 0; j < numStates; j++)
                {
                    diff += Math.Abs(next[j] - stationary[j]);
                    stationary[j] = next[j];
                }
                if (diff < 1e-10)
                    break;
            }
        }

        private bool IsBullish(int state)
        {
            if (stateRule == ZZMarkovStateRule.TrendVolatility)
                return state >= 2;
            return state == 2;
        }

        private string StateName(int state)
        {
            if (stateRule == ZZMarkovStateRule.TrendVolatility)
            {
                switch (state)
                {
                    case 0: return "BearVol";
                    case 1: return "BearQuiet";
                    case 2: return "BullQuiet";
                    default: return "BullVol";
                }
            }
            switch (state)
            {
                case 0: return "Down";
                case 1: return "Flat";
                default: return "Up";
            }
        }

        private Color StateColor(int state)
        {
            if (stateRule == ZZMarkovStateRule.TrendVolatility)
            {
                switch (state)
                {
                    case 0: return Color.Red;
                    case 1: return Color.Orange;
                    case 2: return Color.Green;
                    default: return Color.YellowGreen;
                }
            }
            switch (state)
            {
                case 0: return Color.Red;
                case 1: return Color.Empty;
                default: return Color.Green;
            }
        }

        private void ColorState(int state)
        {
            if (!colorBackground)
                return;
            Color c = StateColor(state);
            if (c != Color.Empty)
                BackColor = Color.FromArgb(backgroundOpacity, c);
        }

        private string MatrixText(int state)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("Markov ({0} transitions)  state: {1}\n", totalTransitions, StateName(state));
            sb.Append("from \\ to ");
            for (int j = 0; j < numStates; j++)
                sb.AppendFormat("{0,10}", StateName(j));
            sb.Append("    n   E[r]%\n");
            for (int i = 0; i < numStates; i++)
            {
                sb.AppendFormat("{0,-10}", StateName(i) + (i == state ? "*" : ""));
                for (int j = 0; j < numStates; j++)
                    sb.AppendFormat("{0,10:0.000}", Probability(i, j));
                double er = rowCounts[i] > 0 ? 100.0 * rowReturnSums[i] / rowCounts[i] : 0;
                sb.AppendFormat("{0,5}{1,8:0.000}\n", rowCounts[i], er);
            }
            sb.Append("stationary");
            for (int j = 0; j < numStates; j++)
                sb.AppendFormat("{0,10:0.000}", stationary[j]);
            return sb.ToString();
        }

        /// <summary>
        /// Smoothed rolling probability of moving from one state to another on the next bar.
        /// </summary>
        public double TransitionProbability(int from, int to)
        {
            Update();
            if (from < 0 || from >= numStates || to < 0 || to >= numStates)
                return double.NaN;
            return Probability(from, to);
        }

        #region Properties
        [Browsable(false)]	// this line prevents the data series from being displayed in the indicator properties dialog, do not remove
        [XmlIgnore()]		// this line ensures that the indicator can be saved/recovered as part of a chart template, do not remove
        public DataSeries PBullNext
        {
            get { return Values[0]; }
        }

        [Browsable(false)]
        [XmlIgnore()]
        public DataSeries PStay
        {
            get { return Values[1]; }
        }

        [Browsable(false)]
        [XmlIgnore()]
        public DataSeries StationaryBull
        {
            get { return Values[2]; }
        }

        [Browsable(false)]
        [XmlIgnore()]
        public DataSeries State
        {
            get { Update(); return stateSeries; }
        }

        [Browsable(false)]
        [XmlIgnore()]
        public DataSeries ExpectedReturn
        {
            get { Update(); return expectedReturnSeries; }
        }

        [Browsable(false)]
        [XmlIgnore()]
        public DataSeries ExpectedDuration
        {
            get { Update(); return expectedDurationSeries; }
        }

        [Description("Rule set used to assign each bar to a state")]
        [GridCategory("Parameters")]
        public ZZMarkovStateRule StateRule
        {
            get { return stateRule; }
            set { stateRule = value; }
        }

        [Description("SMA period for the Bull/Bear trend rule (TrendVolatility)")]
        [GridCategory("Parameters")]
        public int TrendPeriod
        {
            get { return trendPeriod; }
            set { trendPeriod = Math.Max(1, value); }
        }

        [Description("Bars used for the standard deviation of log returns")]
        [GridCategory("Parameters")]
        public int VolPeriod
        {
            get { return volPeriod; }
            set { volPeriod = Math.Max(2, value); }
        }

        [Description("SMA period of volatility that splits Quiet/Volatile (TrendVolatility)")]
        [GridCategory("Parameters")]
        public int VolLookback
        {
            get { return volLookback; }
            set { volLookback = Math.Max(1, value); }
        }

        [Description("Band width in sigmas for Up/Down (ReturnBands)")]
        [GridCategory("Parameters")]
        public double BandK
        {
            get { return bandK; }
            set { bandK = Math.Max(0, value); }
        }

        [Description("Number of most recent transitions used to estimate the matrix")]
        [GridCategory("Parameters")]
        public int Window
        {
            get { return window; }
            set { window = Math.Max(10, value); }
        }

        [Description("Laplace smoothing added to every transition count")]
        [GridCategory("Parameters")]
        public double Smoothing
        {
            get { return smoothing; }
            set { smoothing = Math.Max(0, value); }
        }

        [Description("Transitions required in the window before plotting")]
        [GridCategory("Parameters")]
        public int MinTransitions
        {
            get { return minTransitions; }
            set { minTransitions = Math.Max(1, value); }
        }

        [Description("Shade the panel background by state")]
        [Category("Visual")]
        public bool ColorBackground
        {
            get { return colorBackground; }
            set { colorBackground = value; }
        }

        [Description("Background shading opacity (0-255)")]
        [Category("Visual")]
        public int BackgroundOpacity
        {
            get { return backgroundOpacity; }
            set { backgroundOpacity = Math.Min(255, Math.Max(0, value)); }
        }

        [Description("Draw the current transition matrix on the chart")]
        [Category("Visual")]
        public bool ShowMatrix
        {
            get { return showMatrix; }
            set { showMatrix = value; }
        }
        #endregion
    }
}

namespace ZZMarkovETF.Utility
{
    public enum ZZMarkovStateRule
    {
        TrendVolatility,
        ReturnBands
    }
}

#region NinjaScript generated code. Neither change nor remove.
// This namespace holds all indicators and is required. Do not change it.
namespace NinjaTrader.Indicator
{
    public partial class Indicator : IndicatorBase
    {
        private ZZMarkovETF[] cacheZZMarkovETF = null;

        private static ZZMarkovETF checkZZMarkovETF = new ZZMarkovETF();

        /// <summary>
        /// Observable Markov chain for ETFs: rule based states, rolling transition matrix
        /// </summary>
        /// <returns></returns>
        public ZZMarkovETF ZZMarkovETF(double bandK, int minTransitions, double smoothing, ZZMarkovStateRule stateRule, int trendPeriod, int volLookback, int volPeriod, int window)
        {
            return ZZMarkovETF(Input, bandK, minTransitions, smoothing, stateRule, trendPeriod, volLookback, volPeriod, window);
        }

        /// <summary>
        /// Observable Markov chain for ETFs: rule based states, rolling transition matrix
        /// </summary>
        /// <returns></returns>
        public ZZMarkovETF ZZMarkovETF(Data.IDataSeries input, double bandK, int minTransitions, double smoothing, ZZMarkovStateRule stateRule, int trendPeriod, int volLookback, int volPeriod, int window)
        {
            if (cacheZZMarkovETF != null)
                for (int idx = 0; idx < cacheZZMarkovETF.Length; idx++)
                    if (Math.Abs(cacheZZMarkovETF[idx].BandK - bandK) <= double.Epsilon && cacheZZMarkovETF[idx].MinTransitions == minTransitions && Math.Abs(cacheZZMarkovETF[idx].Smoothing - smoothing) <= double.Epsilon && cacheZZMarkovETF[idx].StateRule == stateRule && cacheZZMarkovETF[idx].TrendPeriod == trendPeriod && cacheZZMarkovETF[idx].VolLookback == volLookback && cacheZZMarkovETF[idx].VolPeriod == volPeriod && cacheZZMarkovETF[idx].Window == window && cacheZZMarkovETF[idx].EqualsInput(input))
                        return cacheZZMarkovETF[idx];

            lock (checkZZMarkovETF)
            {
                checkZZMarkovETF.BandK = bandK;
                bandK = checkZZMarkovETF.BandK;
                checkZZMarkovETF.MinTransitions = minTransitions;
                minTransitions = checkZZMarkovETF.MinTransitions;
                checkZZMarkovETF.Smoothing = smoothing;
                smoothing = checkZZMarkovETF.Smoothing;
                checkZZMarkovETF.StateRule = stateRule;
                stateRule = checkZZMarkovETF.StateRule;
                checkZZMarkovETF.TrendPeriod = trendPeriod;
                trendPeriod = checkZZMarkovETF.TrendPeriod;
                checkZZMarkovETF.VolLookback = volLookback;
                volLookback = checkZZMarkovETF.VolLookback;
                checkZZMarkovETF.VolPeriod = volPeriod;
                volPeriod = checkZZMarkovETF.VolPeriod;
                checkZZMarkovETF.Window = window;
                window = checkZZMarkovETF.Window;

                if (cacheZZMarkovETF != null)
                    for (int idx = 0; idx < cacheZZMarkovETF.Length; idx++)
                        if (Math.Abs(cacheZZMarkovETF[idx].BandK - bandK) <= double.Epsilon && cacheZZMarkovETF[idx].MinTransitions == minTransitions && Math.Abs(cacheZZMarkovETF[idx].Smoothing - smoothing) <= double.Epsilon && cacheZZMarkovETF[idx].StateRule == stateRule && cacheZZMarkovETF[idx].TrendPeriod == trendPeriod && cacheZZMarkovETF[idx].VolLookback == volLookback && cacheZZMarkovETF[idx].VolPeriod == volPeriod && cacheZZMarkovETF[idx].Window == window && cacheZZMarkovETF[idx].EqualsInput(input))
                            return cacheZZMarkovETF[idx];

                ZZMarkovETF indicator = new ZZMarkovETF();
                indicator.BarsRequired = BarsRequired;
                indicator.CalculateOnBarClose = CalculateOnBarClose;
#if NT7
                indicator.ForceMaximumBarsLookBack256 = ForceMaximumBarsLookBack256;
                indicator.MaximumBarsLookBack = MaximumBarsLookBack;
#endif
                indicator.Input = input;
                indicator.BandK = bandK;
                indicator.MinTransitions = minTransitions;
                indicator.Smoothing = smoothing;
                indicator.StateRule = stateRule;
                indicator.TrendPeriod = trendPeriod;
                indicator.VolLookback = volLookback;
                indicator.VolPeriod = volPeriod;
                indicator.Window = window;
                Indicators.Add(indicator);
                indicator.SetUp();

                ZZMarkovETF[] tmp = new ZZMarkovETF[cacheZZMarkovETF == null ? 1 : cacheZZMarkovETF.Length + 1];
                if (cacheZZMarkovETF != null)
                    cacheZZMarkovETF.CopyTo(tmp, 0);
                tmp[tmp.Length - 1] = indicator;
                cacheZZMarkovETF = tmp;
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
        /// Observable Markov chain for ETFs: rule based states, rolling transition matrix
        /// </summary>
        /// <returns></returns>
        [Gui.Design.WizardCondition("Indicator")]
        public Indicator.ZZMarkovETF ZZMarkovETF(double bandK, int minTransitions, double smoothing, ZZMarkovStateRule stateRule, int trendPeriod, int volLookback, int volPeriod, int window)
        {
            return _indicator.ZZMarkovETF(Input, bandK, minTransitions, smoothing, stateRule, trendPeriod, volLookback, volPeriod, window);
        }

        /// <summary>
        /// Observable Markov chain for ETFs: rule based states, rolling transition matrix
        /// </summary>
        /// <returns></returns>
        public Indicator.ZZMarkovETF ZZMarkovETF(Data.IDataSeries input, double bandK, int minTransitions, double smoothing, ZZMarkovStateRule stateRule, int trendPeriod, int volLookback, int volPeriod, int window)
        {
            return _indicator.ZZMarkovETF(input, bandK, minTransitions, smoothing, stateRule, trendPeriod, volLookback, volPeriod, window);
        }
    }
}

// This namespace holds all strategies and is required. Do not change it.
namespace NinjaTrader.Strategy
{
    public partial class Strategy : StrategyBase
    {
        /// <summary>
        /// Observable Markov chain for ETFs: rule based states, rolling transition matrix
        /// </summary>
        /// <returns></returns>
        [Gui.Design.WizardCondition("Indicator")]
        public Indicator.ZZMarkovETF ZZMarkovETF(double bandK, int minTransitions, double smoothing, ZZMarkovStateRule stateRule, int trendPeriod, int volLookback, int volPeriod, int window)
        {
            return _indicator.ZZMarkovETF(Input, bandK, minTransitions, smoothing, stateRule, trendPeriod, volLookback, volPeriod, window);
        }

        /// <summary>
        /// Observable Markov chain for ETFs: rule based states, rolling transition matrix
        /// </summary>
        /// <returns></returns>
        public Indicator.ZZMarkovETF ZZMarkovETF(Data.IDataSeries input, double bandK, int minTransitions, double smoothing, ZZMarkovStateRule stateRule, int trendPeriod, int volLookback, int volPeriod, int window)
        {
            if (InInitialize && input == null)
                throw new ArgumentException("You only can access an indicator with the default input/bar series from within the 'Initialize()' method");

            return _indicator.ZZMarkovETF(input, bandK, minTransitions, smoothing, stateRule, trendPeriod, volLookback, volPeriod, window);
        }
    }
}
#endregion
