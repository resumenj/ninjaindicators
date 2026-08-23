#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// This namespace holds all indicators and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators
{
	/// <summary>
	/// Plots the Initial Balance (high/low range of the first N minutes of the session), its midline,
	/// a shaded range box, and configurable range extensions projected off the Initial Balance range.
	/// </summary>
	public class InitialBalanceIndicator : Indicator
	{
		#region Variables
		private DateTime		currentSessionDate	= Core.Globals.MinDate;
		private DateTime		ibStartTime			= Core.Globals.MinDate;
		private DateTime		ibEndTime			= Core.Globals.MinDate;
		private int				ibStartBar			= -1;
		private double			ibHigh				= double.MinValue;
		private double			ibLow				= double.MaxValue;
		private bool			ibRangeSet			= false;
		private bool			alertedHigh			= false;
		private bool			alertedLow			= false;
		private string			sessionTag			= string.Empty;
		private Queue<string>	sessionTagHistory	= new Queue<string>();

		private static readonly string[] drawSuffixes = new string[]
		{
			"_High", "_Low", "_Mid", "_Box",
			"_HighTxt", "_LowTxt", "_MidTxt",
			"_Ext1U", "_Ext1UTxt", "_Ext1D", "_Ext1DTxt",
			"_Ext2U", "_Ext2UTxt", "_Ext2D", "_Ext2DTxt",
			"_Ext3U", "_Ext3UTxt", "_Ext3D", "_Ext3DTxt"
		};
		#endregion

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= "Plots the Initial Balance high/low of the session, its midline, and range extensions.";
				Name						= "InitialBalanceIndicator";
				Calculate					= Calculate.OnPriceChange;
				IsOverlay					= true;
				DisplayInDataBox			= true;
				DrawOnPricePanel			= true;
				PaintPriceMarkers			= false;
				ScaleJustification			= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive	= true;

				StartTimeHr			= 9;
				StartTimeMinute		= 30;
				DurationMinutes		= 60;
				ShowMidline			= true;
				ShowLabels			= true;
				ShowRangeBox		= true;
				EnableBreakoutAlerts= false;
				MaxDaysToShow		= 5;
				LineWidth			= 2;

				ShowExtension1		= true;
				Extension1Multiple	= 1.0;
				ShowExtension2		= true;
				Extension2Multiple	= 2.0;
				ShowExtension3		= false;
				Extension3Multiple	= 3.0;

				IBHighColor		= Brushes.DodgerBlue;
				IBLowColor		= Brushes.OrangeRed;
				IBMidColor		= Brushes.Gray;
				ExtensionColor	= Brushes.MediumPurple;
				RangeBoxColor	= Brushes.SteelBlue;

				AddPlot(new Stroke(Brushes.DodgerBlue, 2), PlotStyle.Line, "IBHigh");
				AddPlot(new Stroke(Brushes.OrangeRed, 2), PlotStyle.Line, "IBLow");
				AddPlot(new Stroke(Brushes.Gray, 1), PlotStyle.Line, "IBMid");
			}
			else if (State == State.Configure)
			{
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsPeriod.BarsPeriodType == BarsPeriodType.Day || BarsPeriod.BarsPeriodType == BarsPeriodType.Week
				|| BarsPeriod.BarsPeriodType == BarsPeriodType.Month || BarsPeriod.BarsPeriodType == BarsPeriodType.Year)
			{
				Draw.TextFixed(this, "ibErrorMsg", "Initial Balance Indicator only works on intraday charts", TextPosition.BottomRight);
				return;
			}

			DateTime barDate = Time[0].Date;

			if (barDate != currentSessionDate)
			{
				if (ibRangeSet && MaxDaysToShow > 0)
				{
					sessionTagHistory.Enqueue(sessionTag);
					while (sessionTagHistory.Count > MaxDaysToShow)
						RemoveSessionDrawObjects(sessionTagHistory.Dequeue());
				}

				currentSessionDate	= barDate;
				sessionTag			= "IB_" + barDate.ToString("yyyyMMdd");
				ibStartTime			= barDate.Add(new TimeSpan(0, StartTimeHr, StartTimeMinute, 0));
				ibEndTime			= ibStartTime.AddMinutes(Math.Max(1, DurationMinutes));
				ibHigh				= double.MinValue;
				ibLow				= double.MaxValue;
				ibStartBar			= -1;
				ibRangeSet			= false;
				alertedHigh			= false;
				alertedLow			= false;
			}

			if (Time[0] >= ibStartTime && Time[0] < ibEndTime)
			{
				if (ibStartBar == -1)
					ibStartBar = CurrentBar;

				if (High[0] > ibHigh)
					ibHigh = High[0];
				if (Low[0] < ibLow)
					ibLow = Low[0];

				ibRangeSet = true;
			}

			if (!ibRangeSet)
				return;

			IBHigh[0] = ibHigh;
			IBLow[0] = ibLow;
			double ibMid = (ibHigh + ibLow) / 2.0;
			IBMid[0] = ibMid;

			int startBarsAgo = CurrentBar - ibStartBar;

			Draw.Line(this, sessionTag + "_High", false, startBarsAgo, ibHigh, 0, ibHigh, IBHighColor, DashStyleHelper.Solid, LineWidth);
			Draw.Line(this, sessionTag + "_Low", false, startBarsAgo, ibLow, 0, ibLow, IBLowColor, DashStyleHelper.Solid, LineWidth);

			if (ShowMidline)
				Draw.Line(this, sessionTag + "_Mid", false, startBarsAgo, ibMid, 0, ibMid, IBMidColor, DashStyleHelper.Dash, 1);
			else
				RemoveDrawObject(sessionTag + "_Mid");

			if (ShowRangeBox)
			{
				int boxEndBarsAgo = Time[0] < ibEndTime ? 0 : Math.Max(0, CurrentBar - Bars.GetBar(ibEndTime));
				Draw.Rectangle(this, sessionTag + "_Box", false, startBarsAgo, ibHigh, boxEndBarsAgo, ibLow, RangeBoxColor, RangeBoxColor, 8);
			}
			else
				RemoveDrawObject(sessionTag + "_Box");

			if (ShowLabels)
			{
				Draw.Text(this, sessionTag + "_HighTxt", "IBH", startBarsAgo, ibHigh, IBHighColor);
				Draw.Text(this, sessionTag + "_LowTxt", "IBL", startBarsAgo, ibLow, IBLowColor);
				if (ShowMidline)
					Draw.Text(this, sessionTag + "_MidTxt", "IBM", startBarsAgo, ibMid, IBMidColor);
				else
					RemoveDrawObject(sessionTag + "_MidTxt");
			}
			else
			{
				RemoveDrawObject(sessionTag + "_HighTxt");
				RemoveDrawObject(sessionTag + "_LowTxt");
				RemoveDrawObject(sessionTag + "_MidTxt");
			}

			double ibRange = ibHigh - ibLow;
			if (ibRange > 0)
			{
				DrawExtension(1, ShowExtension1, Extension1Multiple, startBarsAgo, ibHigh, ibLow, ibRange);
				DrawExtension(2, ShowExtension2, Extension2Multiple, startBarsAgo, ibHigh, ibLow, ibRange);
				DrawExtension(3, ShowExtension3, Extension3Multiple, startBarsAgo, ibHigh, ibLow, ibRange);
			}

			if (EnableBreakoutAlerts && Time[0] >= ibEndTime)
			{
				if (!alertedHigh && Close[0] > ibHigh)
				{
					alertedHigh = true;
					Alert(sessionTag + "_AlertHigh", Priority.Medium,
						Instrument.FullName + " broke above the Initial Balance High (" + ibHigh.ToString() + ")",
						Core.Globals.InstallDir + @"\sounds\Alert4.wav", 0, Brushes.Yellow, Brushes.Black);
				}

				if (!alertedLow && Close[0] < ibLow)
				{
					alertedLow = true;
					Alert(sessionTag + "_AlertLow", Priority.Medium,
						Instrument.FullName + " broke below the Initial Balance Low (" + ibLow.ToString() + ")",
						Core.Globals.InstallDir + @"\sounds\Alert4.wav", 0, Brushes.Yellow, Brushes.Black);
				}
			}
		}

		/// <summary>
		/// Draws (or removes) one pair of range-extension lines/labels above IBHigh and below IBLow.
		/// </summary>
		private void DrawExtension(int index, bool show, double multiple, int startBarsAgo, double ibHighValue, double ibLowValue, double ibRange)
		{
			string upTag		= sessionTag + "_Ext" + index + "U";
			string upTxtTag	= upTag + "Txt";
			string downTag		= sessionTag + "_Ext" + index + "D";
			string downTxtTag	= downTag + "Txt";

			if (!show)
			{
				RemoveDrawObject(upTag);
				RemoveDrawObject(upTxtTag);
				RemoveDrawObject(downTag);
				RemoveDrawObject(downTxtTag);
				return;
			}

			double upValue		= ibHighValue + ibRange * multiple;
			double downValue	= ibLowValue - ibRange * multiple;

			Draw.Line(this, upTag, false, startBarsAgo, upValue, 0, upValue, ExtensionColor, DashStyleHelper.Dot, 1);
			Draw.Line(this, downTag, false, startBarsAgo, downValue, 0, downValue, ExtensionColor, DashStyleHelper.Dot, 1);

			if (ShowLabels)
			{
				Draw.Text(this, upTxtTag, "+" + multiple.ToString("0.0") + "x", startBarsAgo, upValue, ExtensionColor);
				Draw.Text(this, downTxtTag, "-" + multiple.ToString("0.0") + "x", startBarsAgo, downValue, ExtensionColor);
			}
			else
			{
				RemoveDrawObject(upTxtTag);
				RemoveDrawObject(downTxtTag);
			}
		}

		/// <summary>
		/// Removes every draw object belonging to a given (now stale) session tag.
		/// </summary>
		private void RemoveSessionDrawObjects(string tag)
		{
			for (int i = 0; i < drawSuffixes.Length; i++)
				RemoveDrawObject(tag + drawSuffixes[i]);
		}

		#region Properties
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> IBHigh
		{
			get { return Values[0]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> IBLow
		{
			get { return Values[1]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> IBMid
		{
			get { return Values[2]; }
		}

		[NinjaScriptProperty]
		[Range(0, 23)]
		[Display(Name = "Start time hour", Description = "Hour the Initial Balance period starts (24hr time format).", Order = 1, GroupName = "Parameters")]
		public int StartTimeHr
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, 59)]
		[Display(Name = "Start time minute", Description = "Minute the Initial Balance period starts.", Order = 2, GroupName = "Parameters")]
		public int StartTimeMinute
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Duration (minutes)", Description = "Length of the Initial Balance period, in minutes (e.g. 60 for the classic first hour).", Order = 3, GroupName = "Parameters")]
		public int DurationMinutes
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show midline", Description = "Show the Initial Balance midline.", Order = 4, GroupName = "Parameters")]
		public bool ShowMidline
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show labels", Description = "Show IBH/IBL/IBM text labels.", Order = 5, GroupName = "Parameters")]
		public bool ShowLabels
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show range box", Description = "Shade the Initial Balance period with a rectangle.", Order = 6, GroupName = "Parameters")]
		public bool ShowRangeBox
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable breakout alerts", Description = "Play an alert sound the first time price closes beyond the Initial Balance high/low.", Order = 7, GroupName = "Parameters")]
		public bool EnableBreakoutAlerts
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Max days to show", Description = "Number of past sessions' Initial Balance drawings to keep on the chart. 0 = keep all.", Order = 8, GroupName = "Parameters")]
		public int MaxDaysToShow
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Line width", Description = "Line width for the Initial Balance high/low lines.", Order = 9, GroupName = "Parameters")]
		public int LineWidth
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show extension 1", Description = "Show the first range extension (IB range x multiple) above/below the Initial Balance.", Order = 1, GroupName = "Range Extensions")]
		public bool ShowExtension1
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0.1, double.MaxValue)]
		[Display(Name = "Extension 1 multiple", Description = "Multiple of the Initial Balance range for the first extension.", Order = 2, GroupName = "Range Extensions")]
		public double Extension1Multiple
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show extension 2", Description = "Show the second range extension (IB range x multiple) above/below the Initial Balance.", Order = 3, GroupName = "Range Extensions")]
		public bool ShowExtension2
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0.1, double.MaxValue)]
		[Display(Name = "Extension 2 multiple", Description = "Multiple of the Initial Balance range for the second extension.", Order = 4, GroupName = "Range Extensions")]
		public double Extension2Multiple
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show extension 3", Description = "Show the third range extension (IB range x multiple) above/below the Initial Balance.", Order = 5, GroupName = "Range Extensions")]
		public bool ShowExtension3
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0.1, double.MaxValue)]
		[Display(Name = "Extension 3 multiple", Description = "Multiple of the Initial Balance range for the third extension.", Order = 6, GroupName = "Range Extensions")]
		public double Extension3Multiple
		{ get; set; }

		[XmlIgnore]
		[Display(Name = "Initial Balance high", Description = "Color of the Initial Balance high line.", Order = 1, GroupName = "Plot Colors")]
		public Brush IBHighColor
		{ get; set; }

		[Browsable(false)]
		public string IBHighColorSerializable
		{
			get { return Serialize.BrushToString(IBHighColor); }
			set { IBHighColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Initial Balance low", Description = "Color of the Initial Balance low line.", Order = 2, GroupName = "Plot Colors")]
		public Brush IBLowColor
		{ get; set; }

		[Browsable(false)]
		public string IBLowColorSerializable
		{
			get { return Serialize.BrushToString(IBLowColor); }
			set { IBLowColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Initial Balance mid", Description = "Color of the Initial Balance midline.", Order = 3, GroupName = "Plot Colors")]
		public Brush IBMidColor
		{ get; set; }

		[Browsable(false)]
		public string IBMidColorSerializable
		{
			get { return Serialize.BrushToString(IBMidColor); }
			set { IBMidColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Range extensions", Description = "Color of the range extension lines.", Order = 4, GroupName = "Plot Colors")]
		public Brush ExtensionColor
		{ get; set; }

		[Browsable(false)]
		public string ExtensionColorSerializable
		{
			get { return Serialize.BrushToString(ExtensionColor); }
			set { ExtensionColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Range box", Description = "Fill color of the Initial Balance range box.", Order = 5, GroupName = "Plot Colors")]
		public Brush RangeBoxColor
		{ get; set; }

		[Browsable(false)]
		public string RangeBoxColorSerializable
		{
			get { return Serialize.BrushToString(RangeBoxColor); }
			set { RangeBoxColor = Serialize.StringToBrush(value); }
		}
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.
// NOTE: NinjaTrader rewrites this region automatically the first time this file is compiled
// inside the NinjaScript Editor (it adds the Strategy / MarketAnalyzer fluent-access wrappers
// too). The block below is only what's needed to apply the indicator from a chart.
namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : IndicatorBase
	{
		private InitialBalanceIndicator[] cacheInitialBalanceIndicator;
		public InitialBalanceIndicator InitialBalanceIndicator()
		{
			return InitialBalanceIndicator(Input);
		}

		public InitialBalanceIndicator InitialBalanceIndicator(ISeries<double> input)
		{
			if (cacheInitialBalanceIndicator != null)
				for (int idx = 0; idx < cacheInitialBalanceIndicator.Length; idx++)
					if (cacheInitialBalanceIndicator[idx] != null && cacheInitialBalanceIndicator[idx].EqualsInput(input))
						return cacheInitialBalanceIndicator[idx];
			return CacheIndicator<InitialBalanceIndicator>(new InitialBalanceIndicator(), input, ref cacheInitialBalanceIndicator);
		}
	}
}
#endregion
