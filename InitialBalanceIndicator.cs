#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
#endregion

// This namespace holds all indicators and is required. Do not change it.
namespace NinjaTrader.Indicator
{
	/// <summary>
	/// Plots the Initial Balance (high/low range of the first N minutes of the session), its midline,
	/// and configurable range extensions projected off the Initial Balance range.
	/// </summary>
	[Description("Plots the Initial Balance high/low of the session, its midline, and range extensions.")]
	public class InitialBalanceIndicator : Indicator
	{
		#region Variables
		// Wizard generated variables
		private int		startTimeHr			= 9;	// Default setting for StartTimeHr
		private int		startTimeMinute		= 30;	// Default setting for StartTimeMinute
		private int		durationMinutes		= 60;	// Default setting for DurationMinutes

		// User defined variables (add any user defined variables below)
		private bool	showMidline			= true;
		private bool	showLabels			= true;
		private bool	showRangeBox		= true;
		private bool	enableBreakoutAlerts= false;
		private int		maxDaysToShow		= 5;
		private int		lineWidth			= 2;

		private bool	showExtension1		= true;
		private double	extension1Multiple	= 1.0;
		private bool	showExtension2		= true;
		private double	extension2Multiple	= 2.0;
		private bool	showExtension3		= false;
		private double	extension3Multiple	= 3.0;

		private Color	ibHighColor			= Color.DodgerBlue;
		private Color	ibLowColor			= Color.OrangeRed;
		private Color	ibMidColor			= Color.Gray;
		private Color	extensionColor		= Color.MediumPurple;
		private Color	rangeBoxColor		= Color.SteelBlue;

		private DateTime		currentSessionDate	= Cbi.Globals.MinDate;
		private DateTime		ibStartTime			= Cbi.Globals.MinDate;
		private DateTime		ibEndTime			= Cbi.Globals.MinDate;
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

		/// <summary>
		/// This method is used to configure the indicator and is called once before any bar data is loaded.
		/// </summary>
		protected override void Initialize()
		{
			Add(new Plot(new Pen(Color.DodgerBlue, 2), PlotStyle.Line, "IBHigh"));
			Add(new Plot(new Pen(Color.OrangeRed, 2), PlotStyle.Line, "IBLow"));
			Add(new Plot(new Pen(Color.Gray, 1), PlotStyle.Line, "IBMid"));

			CalculateOnBarClose	= false;
			Overlay				= true;
			PriceTypeSupported	= false;
			AutoScale			= false;
		}

		/// <summary>
		/// Called on each bar update event (incoming tick)
		/// </summary>
		protected override void OnBarUpdate()
		{
			if (!Bars.BarsType.IsIntraday)
			{
				DrawTextFixed("ibErrorMsg", "Initial Balance Indicator only works on intraday charts", TextPosition.BottomRight);
				return;
			}

			DateTime barDate = Time[0].Date;

			if (barDate != currentSessionDate)
			{
				if (ibRangeSet && maxDaysToShow > 0)
				{
					sessionTagHistory.Enqueue(sessionTag);
					while (sessionTagHistory.Count > maxDaysToShow)
						RemoveSessionDrawObjects(sessionTagHistory.Dequeue());
				}

				currentSessionDate	= barDate;
				sessionTag			= "IB_" + barDate.ToString("yyyyMMdd");
				ibStartTime			= barDate.Add(new TimeSpan(0, startTimeHr, startTimeMinute, 0));
				ibEndTime			= ibStartTime.AddMinutes(Math.Max(1, durationMinutes));
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

			IBHigh.Set(ibHigh);
			IBLow.Set(ibLow);
			double ibMid = (ibHigh + ibLow) / 2.0;
			IBMid.Set(ibMid);

			int startBarsAgo = CurrentBar - ibStartBar;

			DrawLine(sessionTag + "_High", false, startBarsAgo, ibHigh, 0, ibHigh, ibHighColor, DashStyle.Solid, lineWidth);
			DrawLine(sessionTag + "_Low", false, startBarsAgo, ibLow, 0, ibLow, ibLowColor, DashStyle.Solid, lineWidth);

			if (showMidline)
				DrawLine(sessionTag + "_Mid", false, startBarsAgo, ibMid, 0, ibMid, ibMidColor, DashStyle.Dash, 1);
			else
				RemoveDrawObject(sessionTag + "_Mid");

			if (showRangeBox)
			{
				int boxEndBarsAgo = Time[0] < ibEndTime ? 0 : Math.Max(0, CurrentBar - Bars.GetBar(ibEndTime));
				DrawRectangle(sessionTag + "_Box", false, startBarsAgo, ibHigh, boxEndBarsAgo, ibLow, rangeBoxColor, rangeBoxColor, 8);
			}
			else
				RemoveDrawObject(sessionTag + "_Box");

			if (showLabels)
			{
				DrawText(sessionTag + "_HighTxt", "IBH", startBarsAgo, ibHigh, ibHighColor);
				DrawText(sessionTag + "_LowTxt", "IBL", startBarsAgo, ibLow, ibLowColor);
				if (showMidline)
					DrawText(sessionTag + "_MidTxt", "IBM", startBarsAgo, ibMid, ibMidColor);
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
				DrawExtension(1, showExtension1, extension1Multiple, startBarsAgo, ibHigh, ibLow, ibRange);
				DrawExtension(2, showExtension2, extension2Multiple, startBarsAgo, ibHigh, ibLow, ibRange);
				DrawExtension(3, showExtension3, extension3Multiple, startBarsAgo, ibHigh, ibLow, ibRange);
			}

			if (enableBreakoutAlerts && Time[0] >= ibEndTime)
			{
				if (!alertedHigh && Close[0] > ibHigh)
				{
					alertedHigh = true;
					Alert(sessionTag + "_AlertHigh", Cbi.Priority.Medium,
						Instrument.FullName + " broke above the Initial Balance High (" + ibHigh.ToString() + ")",
						Cbi.Core.InstallDir + @"\sounds\Alert4.wav", 0, Color.Yellow, Color.Black);
				}

				if (!alertedLow && Close[0] < ibLow)
				{
					alertedLow = true;
					Alert(sessionTag + "_AlertLow", Cbi.Priority.Medium,
						Instrument.FullName + " broke below the Initial Balance Low (" + ibLow.ToString() + ")",
						Cbi.Core.InstallDir + @"\sounds\Alert4.wav", 0, Color.Yellow, Color.Black);
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

			DrawLine(upTag, false, startBarsAgo, upValue, 0, upValue, extensionColor, DashStyle.Dot, 1);
			DrawLine(downTag, false, startBarsAgo, downValue, 0, downValue, extensionColor, DashStyle.Dot, 1);

			if (showLabels)
			{
				DrawText(upTxtTag, "+" + multiple.ToString("0.0") + "x", startBarsAgo, upValue, extensionColor);
				DrawText(downTxtTag, "-" + multiple.ToString("0.0") + "x", startBarsAgo, downValue, extensionColor);
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
		[Browsable(false)]	// this line prevents the data series from being displayed in the indicator properties dialog, do not remove
		[XmlIgnore()]		// this line ensures that the indicator can be saved/recovered as part of a chart template, do not remove
		public DataSeries IBHigh
		{
			get { return Values[0]; }
		}

		[Browsable(false)]
		[XmlIgnore()]
		public DataSeries IBLow
		{
			get { return Values[1]; }
		}

		[Browsable(false)]
		[XmlIgnore()]
		public DataSeries IBMid
		{
			get { return Values[2]; }
		}

		[Description("Hour the Initial Balance period starts (24hr time format).")]
		[Category("Parameters")]
		public int StartTimeHr
		{
			get { return startTimeHr; }
			set { startTimeHr = Math.Min(23, Math.Max(0, value)); }
		}

		[Description("Minute the Initial Balance period starts.")]
		[Category("Parameters")]
		public int StartTimeMinute
		{
			get { return startTimeMinute; }
			set { startTimeMinute = Math.Min(59, Math.Max(0, value)); }
		}

		[Description("Length of the Initial Balance period, in minutes (e.g. 60 for the classic first hour).")]
		[Category("Parameters")]
		public int DurationMinutes
		{
			get { return durationMinutes; }
			set { durationMinutes = Math.Max(1, value); }
		}

		[Description("Show the Initial Balance midline.")]
		[Category("Parameters")]
		public bool ShowMidline
		{
			get { return showMidline; }
			set { showMidline = value; }
		}

		[Description("Show IBH/IBL/IBM text labels.")]
		[Category("Parameters")]
		public bool ShowLabels
		{
			get { return showLabels; }
			set { showLabels = value; }
		}

		[Description("Shade the Initial Balance period with a rectangle.")]
		[Category("Parameters")]
		public bool ShowRangeBox
		{
			get { return showRangeBox; }
			set { showRangeBox = value; }
		}

		[Description("Play an alert sound the first time price closes beyond the Initial Balance high/low.")]
		[Category("Parameters")]
		public bool EnableBreakoutAlerts
		{
			get { return enableBreakoutAlerts; }
			set { enableBreakoutAlerts = value; }
		}

		[Description("Number of past sessions' Initial Balance drawings to keep on the chart. 0 = keep all.")]
		[Category("Parameters")]
		public int MaxDaysToShow
		{
			get { return maxDaysToShow; }
			set { maxDaysToShow = Math.Max(0, value); }
		}

		[Description("Line width for the Initial Balance high/low lines.")]
		[Category("Parameters")]
		public int LineWidth
		{
			get { return lineWidth; }
			set { lineWidth = Math.Max(1, value); }
		}

		[Description("Show the first range extension (IB range x multiple) above/below the Initial Balance.")]
		[Category("Range Extensions")]
		public bool ShowExtension1
		{
			get { return showExtension1; }
			set { showExtension1 = value; }
		}

		[Description("Multiple of the Initial Balance range for the first extension.")]
		[Category("Range Extensions")]
		public double Extension1Multiple
		{
			get { return extension1Multiple; }
			set { extension1Multiple = Math.Max(0.1, value); }
		}

		[Description("Show the second range extension (IB range x multiple) above/below the Initial Balance.")]
		[Category("Range Extensions")]
		public bool ShowExtension2
		{
			get { return showExtension2; }
			set { showExtension2 = value; }
		}

		[Description("Multiple of the Initial Balance range for the second extension.")]
		[Category("Range Extensions")]
		public double Extension2Multiple
		{
			get { return extension2Multiple; }
			set { extension2Multiple = Math.Max(0.1, value); }
		}

		[Description("Show the third range extension (IB range x multiple) above/below the Initial Balance.")]
		[Category("Range Extensions")]
		public bool ShowExtension3
		{
			get { return showExtension3; }
			set { showExtension3 = value; }
		}

		[Description("Multiple of the Initial Balance range for the third extension.")]
		[Category("Range Extensions")]
		public double Extension3Multiple
		{
			get { return extension3Multiple; }
			set { extension3Multiple = Math.Max(0.1, value); }
		}

		[XmlIgnore()]
		[Description("Color of the Initial Balance high line.")]
		[Category("Plot Colors")]
		[Gui.Design.DisplayName("Initial Balance high")]
		public Color IBHighColor
		{
			get { return ibHighColor; }
			set { ibHighColor = value; }
		}

		// Serialize Color object
		[Browsable(false)]
		public string IBHighColorSerialize
		{
			get { return NinjaTrader.Gui.Design.SerializableColor.ToString(ibHighColor); }
			set { ibHighColor = NinjaTrader.Gui.Design.SerializableColor.FromString(value); }
		}

		[XmlIgnore()]
		[Description("Color of the Initial Balance low line.")]
		[Category("Plot Colors")]
		[Gui.Design.DisplayName("Initial Balance low")]
		public Color IBLowColor
		{
			get { return ibLowColor; }
			set { ibLowColor = value; }
		}

		// Serialize Color object
		[Browsable(false)]
		public string IBLowColorSerialize
		{
			get { return NinjaTrader.Gui.Design.SerializableColor.ToString(ibLowColor); }
			set { ibLowColor = NinjaTrader.Gui.Design.SerializableColor.FromString(value); }
		}

		[XmlIgnore()]
		[Description("Color of the Initial Balance midline.")]
		[Category("Plot Colors")]
		[Gui.Design.DisplayName("Initial Balance mid")]
		public Color IBMidColor
		{
			get { return ibMidColor; }
			set { ibMidColor = value; }
		}

		// Serialize Color object
		[Browsable(false)]
		public string IBMidColorSerialize
		{
			get { return NinjaTrader.Gui.Design.SerializableColor.ToString(ibMidColor); }
			set { ibMidColor = NinjaTrader.Gui.Design.SerializableColor.FromString(value); }
		}

		[XmlIgnore()]
		[Description("Color of the range extension lines.")]
		[Category("Plot Colors")]
		[Gui.Design.DisplayName("Range extensions")]
		public Color ExtensionColor
		{
			get { return extensionColor; }
			set { extensionColor = value; }
		}

		// Serialize Color object
		[Browsable(false)]
		public string ExtensionColorSerialize
		{
			get { return NinjaTrader.Gui.Design.SerializableColor.ToString(extensionColor); }
			set { extensionColor = NinjaTrader.Gui.Design.SerializableColor.FromString(value); }
		}

		[XmlIgnore()]
		[Description("Fill color of the Initial Balance range box.")]
		[Category("Plot Colors")]
		[Gui.Design.DisplayName("Range box")]
		public Color RangeBoxColor
		{
			get { return rangeBoxColor; }
			set { rangeBoxColor = value; }
		}

		// Serialize Color object
		[Browsable(false)]
		public string RangeBoxColorSerialize
		{
			get { return NinjaTrader.Gui.Design.SerializableColor.ToString(rangeBoxColor); }
			set { rangeBoxColor = NinjaTrader.Gui.Design.SerializableColor.FromString(value); }
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
		private InitialBalanceIndicator[] cacheInitialBalanceIndicator = null;

		private static InitialBalanceIndicator checkInitialBalanceIndicator = new InitialBalanceIndicator();

		/// <summary>
		/// Plots the Initial Balance high/low of the session, its midline, and range extensions.
		/// </summary>
		/// <returns></returns>
		public InitialBalanceIndicator InitialBalanceIndicator()
		{
			return InitialBalanceIndicator(Input);
		}

		/// <summary>
		/// Plots the Initial Balance high/low of the session, its midline, and range extensions.
		/// </summary>
		/// <returns></returns>
		public InitialBalanceIndicator InitialBalanceIndicator(Data.IDataSeries input)
		{
			if (cacheInitialBalanceIndicator != null)
				for (int idx = 0; idx < cacheInitialBalanceIndicator.Length; idx++)
					if (cacheInitialBalanceIndicator[idx].EqualsInput(input))
						return cacheInitialBalanceIndicator[idx];

			lock (checkInitialBalanceIndicator)
			{
				if (cacheInitialBalanceIndicator != null)
					for (int idx = 0; idx < cacheInitialBalanceIndicator.Length; idx++)
						if (cacheInitialBalanceIndicator[idx].EqualsInput(input))
							return cacheInitialBalanceIndicator[idx];

				InitialBalanceIndicator indicator = new InitialBalanceIndicator();
				indicator.BarsRequired = BarsRequired;
				indicator.CalculateOnBarClose = CalculateOnBarClose;
#if NT7
				indicator.ForceMaximumBarsLookBack256 = ForceMaximumBarsLookBack256;
				indicator.MaximumBarsLookBack = MaximumBarsLookBack;
#endif
				indicator.Input = input;
				Indicators.Add(indicator);
				indicator.SetUp();

				InitialBalanceIndicator[] tmp = new InitialBalanceIndicator[cacheInitialBalanceIndicator == null ? 1 : cacheInitialBalanceIndicator.Length + 1];
				if (cacheInitialBalanceIndicator != null)
					cacheInitialBalanceIndicator.CopyTo(tmp, 0);
				tmp[tmp.Length - 1] = indicator;
				cacheInitialBalanceIndicator = tmp;
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
		/// Plots the Initial Balance high/low of the session, its midline, and range extensions.
		/// </summary>
		/// <returns></returns>
		[Gui.Design.WizardCondition("Indicator")]
		public Indicator.InitialBalanceIndicator InitialBalanceIndicator()
		{
			return _indicator.InitialBalanceIndicator(Input);
		}

		/// <summary>
		/// Plots the Initial Balance high/low of the session, its midline, and range extensions.
		/// </summary>
		/// <returns></returns>
		public Indicator.InitialBalanceIndicator InitialBalanceIndicator(Data.IDataSeries input)
		{
			return _indicator.InitialBalanceIndicator(input);
		}
	}
}

// This namespace holds all strategies and is required. Do not change it.
namespace NinjaTrader.Strategy
{
	public partial class Strategy : StrategyBase
	{
		/// <summary>
		/// Plots the Initial Balance high/low of the session, its midline, and range extensions.
		/// </summary>
		/// <returns></returns>
		[Gui.Design.WizardCondition("Indicator")]
		public Indicator.InitialBalanceIndicator InitialBalanceIndicator()
		{
			return _indicator.InitialBalanceIndicator(Input);
		}

		/// <summary>
		/// Plots the Initial Balance high/low of the session, its midline, and range extensions.
		/// </summary>
		/// <returns></returns>
		public Indicator.InitialBalanceIndicator InitialBalanceIndicator(Data.IDataSeries input)
		{
			if (InInitialize && input == null)
				throw new ArgumentException("You only can access an indicator with the default input/bar series from within the 'Initialize()' method");

			return _indicator.InitialBalanceIndicator(input);
		}
	}
}
#endregion
