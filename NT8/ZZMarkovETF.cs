#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

public enum ZZMarkovStateRule
{
	TrendVolatility,
	ReturnBands
}

//This namespace holds Indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators
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
	/// Exposed  : MarkovState, ExpectedReturn (mean next-bar log return from the current state
	///            over the window), ExpectedDuration (1 / (1 - P[s,s]) bars),
	///            TransitionProbability(from, to).
	///
	/// Transitions are only committed from closed bars, so with Calculate other than OnBarClose
	/// the matrix lags one bar while MarkovState/plots still track the forming bar.
	/// </summary>
	public class ZZMarkovETF : Indicator
	{
		private const int MaxStates = 4;

		private Series<double> retSeries;
		private Series<double> volSeries;
		private Series<double> stateSeries;
		private Series<double> expectedReturnSeries;
		private Series<double> expectedDurationSeries;

		private int numStates;
		private int warmup;
		private int[,] counts;
		private int[] rowCounts;
		private double[] rowReturnSums;
		private int totalTransitions;
		private double[] stationary;
		private Brush[] stateBrushes;
		private SimpleFont matrixFont;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= "Observable Markov chain for ETFs: rule based states, rolling transition matrix";
				Name						= "ZZMarkovETF";
				Calculate					= Calculate.OnBarClose;
				IsOverlay					= false;
				DisplayInDataBox			= true;
				DrawOnPricePanel			= true;
				PaintPriceMarkers			= true;
				ScaleJustification			= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive	= true;
				// the rolling window reaches back Window + 2 bars
				MaximumBarsLookBack			= MaximumBarsLookBack.Infinite;

				StateRule			= ZZMarkovStateRule.TrendVolatility;
				TrendPeriod			= 200;
				VolPeriod			= 20;
				VolLookback			= 100;
				BandK				= 0.5;
				Window				= 252;
				Smoothing			= 1.0;
				MinTransitions		= 60;
				ColorBackground		= true;
				BackgroundOpacity	= 40;
				ShowMatrix			= true;

				AddPlot(Brushes.Green, "PBullNext");
				AddPlot(Brushes.RoyalBlue, "PStay");
				AddPlot(Brushes.DarkOrange, "StationaryBull");
				AddLine(Brushes.DarkGray, 0.5, "Half");
			}
			else if (State == State.DataLoaded)
			{
				retSeries				= new Series<double>(this, MaximumBarsLookBack.Infinite);
				volSeries				= new Series<double>(this, MaximumBarsLookBack.Infinite);
				stateSeries				= new Series<double>(this, MaximumBarsLookBack.Infinite);
				expectedReturnSeries	= new Series<double>(this, MaximumBarsLookBack.Infinite);
				expectedDurationSeries	= new Series<double>(this, MaximumBarsLookBack.Infinite);

				counts			= new int[MaxStates, MaxStates];
				rowCounts		= new int[MaxStates];
				rowReturnSums	= new double[MaxStates];
				stationary		= new double[MaxStates];
				totalTransitions = 0;

				numStates = StateRule == ZZMarkovStateRule.TrendVolatility ? 4 : 3;
				if (StateRule == ZZMarkovStateRule.TrendVolatility)
					warmup = Math.Max(TrendPeriod, VolPeriod + VolLookback);
				else
					warmup = VolPeriod + 1;

				stateBrushes = new Brush[MaxStates];
				for (int i = 0; i < numStates; i++)
				{
					Color c = StateColor(i);
					if (c == Colors.Transparent)
						continue;
					Brush b = new SolidColorBrush(Color.FromArgb((byte)BackgroundOpacity, c.R, c.G, c.B));
					b.Freeze();
					stateBrushes[i] = b;
				}

				matrixFont = new SimpleFont("Courier New", 11);
			}
		}

		protected override void OnBarUpdate()
		{
			retSeries[0] = CurrentBar < 1 ? 0 : Math.Log(Input[0] / Input[1]);
			volSeries[0] = StdDev(retSeries, VolPeriod)[0];

			if (CurrentBar < warmup)
				return;

			int state = ClassifyState();
			stateSeries[0] = state;

			// commit the transition that ended on the most recent closed bar, drop the one leaving the window
			int k = Calculate == Calculate.OnBarClose ? 0 : 1;
			if (IsFirstTickOfBar && IsValidState(k + 1))
			{
				AddTransition(k, 1);
				if (IsValidState(k + Window + 1))
					AddTransition(k + Window, -1);
			}

			if (ColorBackground && stateBrushes[state] != null)
				BackBrush = stateBrushes[state];

			if (totalTransitions < MinTransitions)
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

			PBullNext[0]				= pBull;
			PStay[0]					= pStay;
			StationaryBull[0]			= stationaryBull;
			expectedReturnSeries[0]		= rowCounts[state] > 0 ? rowReturnSums[state] / rowCounts[state] : 0;
			expectedDurationSeries[0]	= 1.0 / Math.Max(1e-9, 1.0 - pStay);

			if (ShowMatrix && CurrentBar >= Count - 2)
				Draw.TextFixed(this, "ZZMarkovETFMatrix", MatrixText(state), TextPosition.TopLeft,
					Brushes.Black, matrixFont, Brushes.Transparent, Brushes.White, 60);
		}

		private int ClassifyState()
		{
			if (StateRule == ZZMarkovStateRule.TrendVolatility)
			{
				bool bull = Input[0] > SMA(Input, TrendPeriod)[0];
				bool isVolatile = volSeries[0] > SMA(volSeries, VolLookback)[0];
				if (bull)
					return isVolatile ? 3 : 2;
				return isVolatile ? 0 : 1;
			}

			// sigma from the prior bar so today's return does not widen its own band
			double band = BandK * volSeries[1];
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
			return (counts[from, to] + Smoothing) / (rowCounts[from] + Smoothing * numStates);
		}

		// power iteration on the smoothed matrix (irreducible when Smoothing > 0)
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
			if (StateRule == ZZMarkovStateRule.TrendVolatility)
				return state >= 2;
			return state == 2;
		}

		private string StateName(int state)
		{
			if (StateRule == ZZMarkovStateRule.TrendVolatility)
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
			if (StateRule == ZZMarkovStateRule.TrendVolatility)
			{
				switch (state)
				{
					case 0: return Colors.Red;
					case 1: return Colors.Orange;
					case 2: return Colors.Green;
					default: return Colors.YellowGreen;
				}
			}
			switch (state)
			{
				case 0: return Colors.Red;
				case 1: return Colors.Transparent;
				default: return Colors.Green;
			}
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
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> PBullNext
		{
			get { return Values[0]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> PStay
		{
			get { return Values[1]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> StationaryBull
		{
			get { return Values[2]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> MarkovState
		{
			get { Update(); return stateSeries; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> ExpectedReturn
		{
			get { Update(); return expectedReturnSeries; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> ExpectedDuration
		{
			get { Update(); return expectedDurationSeries; }
		}

		[NinjaScriptProperty]
		[Display(Name = "StateRule", Description = "Rule set used to assign each bar to a state", Order = 1, GroupName = "Parameters")]
		public ZZMarkovStateRule StateRule
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "TrendPeriod", Description = "SMA period for the Bull/Bear trend rule (TrendVolatility)", Order = 2, GroupName = "Parameters")]
		public int TrendPeriod
		{ get; set; }

		[NinjaScriptProperty]
		[Range(2, int.MaxValue)]
		[Display(Name = "VolPeriod", Description = "Bars used for the standard deviation of log returns", Order = 3, GroupName = "Parameters")]
		public int VolPeriod
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "VolLookback", Description = "SMA period of volatility that splits Quiet/Volatile (TrendVolatility)", Order = 4, GroupName = "Parameters")]
		public int VolLookback
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "BandK", Description = "Band width in sigmas for Up/Down (ReturnBands)", Order = 5, GroupName = "Parameters")]
		public double BandK
		{ get; set; }

		[NinjaScriptProperty]
		[Range(10, int.MaxValue)]
		[Display(Name = "Window", Description = "Number of most recent transitions used to estimate the matrix", Order = 6, GroupName = "Parameters")]
		public int Window
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "Smoothing", Description = "Laplace smoothing added to every transition count", Order = 7, GroupName = "Parameters")]
		public double Smoothing
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "MinTransitions", Description = "Transitions required in the window before plotting", Order = 8, GroupName = "Parameters")]
		public int MinTransitions
		{ get; set; }

		[Display(Name = "ColorBackground", Description = "Shade the panel background by state", Order = 1, GroupName = "Visual")]
		public bool ColorBackground
		{ get; set; }

		[Range(0, 255)]
		[Display(Name = "BackgroundOpacity", Description = "Background shading opacity (0-255)", Order = 2, GroupName = "Visual")]
		public int BackgroundOpacity
		{ get; set; }

		[Display(Name = "ShowMatrix", Description = "Draw the current transition matrix on the chart", Order = 3, GroupName = "Visual")]
		public bool ShowMatrix
		{ get; set; }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private ZZMarkovETF[] cacheZZMarkovETF;
		public ZZMarkovETF ZZMarkovETF(ZZMarkovStateRule stateRule, int trendPeriod, int volPeriod, int volLookback, double bandK, int window, double smoothing, int minTransitions)
		{
			return ZZMarkovETF(Input, stateRule, trendPeriod, volPeriod, volLookback, bandK, window, smoothing, minTransitions);
		}

		public ZZMarkovETF ZZMarkovETF(ISeries<double> input, ZZMarkovStateRule stateRule, int trendPeriod, int volPeriod, int volLookback, double bandK, int window, double smoothing, int minTransitions)
		{
			if (cacheZZMarkovETF != null)
				for (int idx = 0; idx < cacheZZMarkovETF.Length; idx++)
					if (cacheZZMarkovETF[idx] != null && cacheZZMarkovETF[idx].StateRule == stateRule && cacheZZMarkovETF[idx].TrendPeriod == trendPeriod && cacheZZMarkovETF[idx].VolPeriod == volPeriod && cacheZZMarkovETF[idx].VolLookback == volLookback && cacheZZMarkovETF[idx].BandK == bandK && cacheZZMarkovETF[idx].Window == window && cacheZZMarkovETF[idx].Smoothing == smoothing && cacheZZMarkovETF[idx].MinTransitions == minTransitions && cacheZZMarkovETF[idx].EqualsInput(input))
						return cacheZZMarkovETF[idx];
			return CacheIndicator<ZZMarkovETF>(new ZZMarkovETF(){ StateRule = stateRule, TrendPeriod = trendPeriod, VolPeriod = volPeriod, VolLookback = volLookback, BandK = bandK, Window = window, Smoothing = smoothing, MinTransitions = minTransitions }, input, ref cacheZZMarkovETF);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.ZZMarkovETF ZZMarkovETF(ZZMarkovStateRule stateRule, int trendPeriod, int volPeriod, int volLookback, double bandK, int window, double smoothing, int minTransitions)
		{
			return indicator.ZZMarkovETF(Input, stateRule, trendPeriod, volPeriod, volLookback, bandK, window, smoothing, minTransitions);
		}

		public Indicators.ZZMarkovETF ZZMarkovETF(ISeries<double> input , ZZMarkovStateRule stateRule, int trendPeriod, int volPeriod, int volLookback, double bandK, int window, double smoothing, int minTransitions)
		{
			return indicator.ZZMarkovETF(input, stateRule, trendPeriod, volPeriod, volLookback, bandK, window, smoothing, minTransitions);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.ZZMarkovETF ZZMarkovETF(ZZMarkovStateRule stateRule, int trendPeriod, int volPeriod, int volLookback, double bandK, int window, double smoothing, int minTransitions)
		{
			return indicator.ZZMarkovETF(Input, stateRule, trendPeriod, volPeriod, volLookback, bandK, window, smoothing, minTransitions);
		}

		public Indicators.ZZMarkovETF ZZMarkovETF(ISeries<double> input , ZZMarkovStateRule stateRule, int trendPeriod, int volPeriod, int volLookback, double bandK, int window, double smoothing, int minTransitions)
		{
			return indicator.ZZMarkovETF(input, stateRule, trendPeriod, volPeriod, volLookback, bandK, window, smoothing, minTransitions);
		}
	}
}

#endregion
