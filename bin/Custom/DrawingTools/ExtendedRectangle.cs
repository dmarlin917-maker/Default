#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Core;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using Point = System.Windows.Point;
#endregion

namespace NinjaTrader.NinjaScript.DrawingTools
{
	/// <summary>
	/// Rectangle drawing tool that always extends to the right edge of the chart
	/// and renders persistent price labels at the high and low edges.
	/// Two-click placement: first click sets the high price, second click sets the low.
	/// </summary>
	public class ExtendedRectangle : DrawingTool
	{
		private const double cursorSensitivity = 15;
		private ChartAnchor editingAnchor;

		[Display(Order = 1)]
		public ChartAnchor StartAnchor { get; set; }

		[Display(Order = 2)]
		public ChartAnchor EndAnchor { get; set; }

		[Browsable(false)]
		public ChartAnchor CutAnchor { get; set; }

		[Display(Name = "Has Cutoff", GroupName = "NinjaScriptGeneral", Order = 8)]
		public bool HasCutoff { get; set; }

		public override IEnumerable<ChartAnchor> Anchors
		{
			get { return new[] { StartAnchor, EndAnchor }; }
		}

		[Display(Name = "Outline", GroupName = "NinjaScriptGeneral", Order = 1)]
		public Stroke OutlineStroke { get; set; }

		[XmlIgnore]
		[Display(Name = "Area Brush", GroupName = "NinjaScriptGeneral", Order = 2)]
		public System.Windows.Media.Brush AreaBrush { get; set; }

		[Browsable(false)]
		public string AreaBrushSerialize
		{
			get { return Serialize.BrushToString(AreaBrush); }
			set { AreaBrush = Serialize.StringToBrush(value); }
		}

		[Range(0, 100)]
		[Display(Name = "Area Opacity", GroupName = "NinjaScriptGeneral", Order = 3)]
		public int AreaOpacity { get; set; }

		[Display(Name = "Show Price Labels", GroupName = "NinjaScriptGeneral", Order = 4)]
		public bool ShowPriceLabels { get; set; }

		[XmlIgnore]
		[Display(Name = "Label Text Color", GroupName = "NinjaScriptGeneral", Order = 5)]
		public System.Windows.Media.Brush LabelTextBrush { get; set; }

		[Browsable(false)]
		public string LabelTextBrushSerialize
		{
			get { return Serialize.BrushToString(LabelTextBrush); }
			set { LabelTextBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Label Background", GroupName = "NinjaScriptGeneral", Order = 6)]
		public System.Windows.Media.Brush LabelBackgroundBrush { get; set; }

		[Browsable(false)]
		public string LabelBackgroundBrushSerialize
		{
			get { return Serialize.BrushToString(LabelBackgroundBrush); }
			set { LabelBackgroundBrush = Serialize.StringToBrush(value); }
		}

		[Range(6, 48)]
		[Display(Name = "Label Font Size", GroupName = "NinjaScriptGeneral", Order = 7)]
		public int LabelFontSize { get; set; }

		public override object Icon { get { return Gui.Tools.Icons.DrawRectangle; } }

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= "Rectangle that extends right indefinitely with persistent price labels at the high and low.";
				Name						= "Extended Rectangle";
				DrawingState				= DrawingState.Building;

				StartAnchor					= new ChartAnchor { IsEditing = true, DrawingTool = this, DisplayName = "High Anchor" };
				EndAnchor					= new ChartAnchor { IsEditing = true, DrawingTool = this, DisplayName = "Low Anchor" };
				CutAnchor					= new ChartAnchor { IsEditing = false, DrawingTool = this, DisplayName = "Cut Anchor" };
				HasCutoff					= false;

				OutlineStroke				= new Stroke(System.Windows.Media.Brushes.DodgerBlue, 2f);
				AreaBrush					= System.Windows.Media.Brushes.DodgerBlue;
				AreaOpacity					= 25;
				ShowPriceLabels				= true;
				LabelTextBrush				= System.Windows.Media.Brushes.White;
				LabelBackgroundBrush		= System.Windows.Media.Brushes.DodgerBlue;
				LabelFontSize				= 12;
			}
			else if (State == State.Terminated)
				Dispose();
		}

		public override System.Windows.Input.Cursor GetCursor(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, Point point)
		{
			switch (DrawingState)
			{
				case DrawingState.Building:	return Cursors.Pen;
				case DrawingState.Moving:	return IsLocked ? Cursors.No : Cursors.SizeAll;
				case DrawingState.Editing:	return IsLocked ? Cursors.No : Cursors.SizeNS;
				default:
					Point startPoint	= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
					Point endPoint		= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
					double top			= Math.Min(startPoint.Y, endPoint.Y);
					double bottom		= Math.Max(startPoint.Y, endPoint.Y);
					double left			= startPoint.X;
					double right		= chartPanel.X + chartPanel.W;

					// near top or bottom edge for resize
					if (point.X >= left - cursorSensitivity && point.X <= right
						&& (Math.Abs(point.Y - top) <= cursorSensitivity || Math.Abs(point.Y - bottom) <= cursorSensitivity))
						return IsLocked ? Cursors.Arrow : Cursors.SizeNS;

					// inside body for move
					if (point.X >= left - cursorSensitivity && point.X <= right
						&& point.Y >= top - cursorSensitivity && point.Y <= bottom + cursorSensitivity)
						return IsLocked ? Cursors.Arrow : Cursors.SizeAll;

					return null;
			}
		}

		public override Point[] GetSelectionPoints(ChartControl chartControl, ChartScale chartScale)
		{
			ChartPanel chartPanel	= chartControl.ChartPanels[chartScale.PanelIndex];
			Point startPoint		= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point endPoint			= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);

			if (HasCutoff && CutAnchor != null)
			{
				Point cutPoint	= CutAnchor.GetPoint(chartControl, chartPanel, chartScale);
				double midY		= (startPoint.Y + endPoint.Y) / 2.0;
				return new[]
				{
					new Point(startPoint.X, startPoint.Y),
					new Point(startPoint.X, endPoint.Y),
					new Point(cutPoint.X,   midY)
				};
			}

			return new[] { new Point(startPoint.X, startPoint.Y), new Point(startPoint.X, endPoint.Y) };
		}

		public override bool IsAlertConditionTrue(AlertConditionItem conditionItem, Condition condition, ChartAlertValue[] values, ChartControl chartControl, ChartScale chartScale)
		{
			return false;
		}

		public override bool IsVisibleOnChart(ChartControl chartControl, ChartScale chartScale, DateTime firstTimeOnChart, DateTime lastTimeOnChart)
		{
			if (DrawingState == DrawingState.Building)
				return true;
			if (StartAnchor.Time > lastTimeOnChart) return false;
			if (HasCutoff && CutAnchor != null && CutAnchor.Time < firstTimeOnChart) return false;
			return true;
		}

		public override void OnCalculateMinMax()
		{
			MinValue = double.MaxValue;
			MaxValue = double.MinValue;
			if (Anchors == null) return;
			foreach (ChartAnchor anchor in Anchors)
			{
				if (anchor.IsEditing) continue;
				MinValue = Math.Min(anchor.Price, MinValue);
				MaxValue = Math.Max(anchor.Price, MaxValue);
			}
		}

		public override void OnMouseDown(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			switch (DrawingState)
			{
				case DrawingState.Building:
					if (StartAnchor.IsEditing)
					{
						// First click: lock in the high (top) anchor
						dataPoint.CopyDataValues(StartAnchor);
						StartAnchor.IsEditing = false;
						// Seed the low anchor at the same point so it follows the mouse on move
						dataPoint.CopyDataValues(EndAnchor);
					}
					else if (EndAnchor.IsEditing)
					{
						// Second click: lock in the low (bottom) anchor; force same time as start
						dataPoint.CopyDataValues(EndAnchor);
						EndAnchor.Time		= StartAnchor.Time;
						EndAnchor.SlotIndex	= StartAnchor.SlotIndex;
						EndAnchor.IsEditing	= false;
					}

					if (!StartAnchor.IsEditing && !EndAnchor.IsEditing)
					{
						DrawingState	= DrawingState.Normal;
						IsSelected		= false;
					}
					break;
				case DrawingState.Normal:
					// Shift+Click sets/updates the right-edge cutoff at the click time.
					if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) == System.Windows.Input.ModifierKeys.Shift)
					{
						if (CutAnchor == null)
							CutAnchor = new ChartAnchor { DrawingTool = this, DisplayName = "Cut Anchor" };
						dataPoint.CopyDataValues(CutAnchor);
						CutAnchor.Price = (StartAnchor.Price + EndAnchor.Price) / 2.0;
						HasCutoff = true;
						return;
					}

					Point pt = dataPoint.GetPoint(chartControl, chartPanel, chartScale);

					// Manual hit-test for the cut handle (CutAnchor is not in Anchors)
					if (HasCutoff && CutAnchor != null)
					{
						Point cutPoint	= CutAnchor.GetPoint(chartControl, chartPanel, chartScale);
						Point sP		= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
						Point eP		= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
						double midY		= (sP.Y + eP.Y) / 2.0;
						if (Math.Abs(pt.X - cutPoint.X) <= cursorSensitivity && Math.Abs(pt.Y - midY) <= (Math.Abs(eP.Y - sP.Y) / 2.0 + cursorSensitivity))
						{
							editingAnchor			= CutAnchor;
							CutAnchor.IsEditing		= true;
							DrawingState			= DrawingState.Editing;
							break;
						}
					}

					editingAnchor = GetClosestAnchor(chartControl, chartPanel, chartScale, cursorSensitivity, pt);
					if (editingAnchor != null)
					{
						editingAnchor.IsEditing	= true;
						DrawingState			= DrawingState.Editing;
					}
					else
						DrawingState = DrawingState.Moving;
					break;
			}
		}

		public override void OnMouseMove(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			if (IsLocked && DrawingState != DrawingState.Building) return;

			if (DrawingState == DrawingState.Building && EndAnchor.IsEditing)
			{
				dataPoint.CopyDataValues(EndAnchor);
				// Keep both anchors anchored at the same X (start time) so left edge is fixed
				EndAnchor.Time		= StartAnchor.Time;
				EndAnchor.SlotIndex	= StartAnchor.SlotIndex;
			}
			else if (DrawingState == DrawingState.Editing && editingAnchor != null)
			{
				if (editingAnchor == CutAnchor)
				{
					// Cut handle: only adjust time; price snaps to vertical center
					CutAnchor.Time		= dataPoint.Time;
					CutAnchor.SlotIndex	= dataPoint.SlotIndex;
					CutAnchor.Price		= (StartAnchor.Price + EndAnchor.Price) / 2.0;
				}
				else
				{
					dataPoint.CopyDataValues(editingAnchor);
					// Right side (low anchor) always shares start anchor's time
					EndAnchor.Time		= StartAnchor.Time;
					EndAnchor.SlotIndex	= StartAnchor.SlotIndex;
				}
			}
			else if (DrawingState == DrawingState.Moving)
			{
				foreach (ChartAnchor anchor in Anchors)
					anchor.MoveAnchor(InitialMouseDownAnchor, dataPoint, chartControl, chartPanel, chartScale, this);
				// After a move both anchors should still share start time
				EndAnchor.Time		= StartAnchor.Time;
				EndAnchor.SlotIndex	= StartAnchor.SlotIndex;
			}
		}

		public override void OnMouseUp(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			if (DrawingState == DrawingState.Editing || DrawingState == DrawingState.Moving)
			{
				if (editingAnchor != null) editingAnchor.IsEditing = false;
				editingAnchor	= null;
				DrawingState	= DrawingState.Normal;
			}
		}

		public override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (StartAnchor.IsEditing) return;

			RenderTarget.AntialiasMode = AntialiasMode.PerPrimitive;

			ChartPanel chartPanel	= chartControl.ChartPanels[chartScale.PanelIndex];
			Point startPoint		= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point endPoint			= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);

			float left		= (float)startPoint.X;
			float right		= (float)(chartPanel.X + chartPanel.W);
			if (HasCutoff && CutAnchor != null)
			{
				Point cutPoint = CutAnchor.GetPoint(chartControl, chartPanel, chartScale);
				right = (float)Math.Min(right, cutPoint.X);
				if (right < left) right = left;
			}
			float top		= (float)Math.Min(startPoint.Y, endPoint.Y);
			float bottom	= (float)Math.Max(startPoint.Y, endPoint.Y);

			if (right <= left || bottom <= top) return;

			SharpDX.RectangleF rect = new SharpDX.RectangleF(left, top, right - left, bottom - top);

			// Filled area
			if (AreaBrush != null && AreaOpacity > 0)
			{
				System.Windows.Media.Brush cloneArea			= AreaBrush.Clone();
				cloneArea.Opacity							= AreaOpacity / 100.0;
				SharpDX.Direct2D1.Brush areaBrushDx			= cloneArea.ToDxBrush(RenderTarget);
				RenderTarget.FillRectangle(rect, areaBrushDx);
				areaBrushDx.Dispose();
			}

			// Outline: draw top, bottom, and left edges only (right side is open / extends infinitely)
			OutlineStroke.RenderTarget				= RenderTarget;
			SharpDX.Direct2D1.Brush outlineDx		= OutlineStroke.BrushDX;
			if (outlineDx != null)
			{
				RenderTarget.DrawLine(new SharpDX.Vector2(left, top),    new SharpDX.Vector2(right, top),    outlineDx, OutlineStroke.Width, OutlineStroke.StrokeStyle);
				RenderTarget.DrawLine(new SharpDX.Vector2(left, bottom), new SharpDX.Vector2(right, bottom), outlineDx, OutlineStroke.Width, OutlineStroke.StrokeStyle);
				RenderTarget.DrawLine(new SharpDX.Vector2(left, top),    new SharpDX.Vector2(left,  bottom), outlineDx, OutlineStroke.Width, OutlineStroke.StrokeStyle);
				if (HasCutoff)
					RenderTarget.DrawLine(new SharpDX.Vector2(right, top), new SharpDX.Vector2(right, bottom), outlineDx, OutlineStroke.Width, OutlineStroke.StrokeStyle);
			}

			if (!ShowPriceLabels) return;

			double highPrice	= Math.Max(StartAnchor.Price, EndAnchor.Price);
			double lowPrice		= Math.Min(StartAnchor.Price, EndAnchor.Price);

			string highText		= FormatPrice(highPrice, chartPanel);
			string lowText		= FormatPrice(lowPrice,  chartPanel);

			SharpDX.DirectWrite.TextFormat textFormat = null;
			try
			{
				textFormat					= new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", LabelFontSize <= 0 ? 12 : LabelFontSize);
				textFormat.TextAlignment	= SharpDX.DirectWrite.TextAlignment.Leading;
				textFormat.WordWrapping		= SharpDX.DirectWrite.WordWrapping.NoWrap;

				DrawPriceLabel(highText, left, top,    textFormat);
				DrawPriceLabel(lowText,  left, bottom, textFormat);
			}
			finally
			{
				if (textFormat != null) textFormat.Dispose();
			}
		}

		private void DrawPriceLabel(string text, float x, float y, SharpDX.DirectWrite.TextFormat textFormat)
		{
			using (SharpDX.DirectWrite.TextLayout textLayout = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, text, textFormat, 400, textFormat.FontSize + 8))
			{
				const float padding		= 4f;
				float textWidth			= textLayout.Metrics.Width  + padding * 2;
				float textHeight		= textLayout.Metrics.Height + padding * 2;
				SharpDX.RectangleF labelRect = new SharpDX.RectangleF(x + 4, y - textHeight / 2f, textWidth, textHeight);

				if (LabelBackgroundBrush != null)
				{
					SharpDX.Direct2D1.Brush bg = LabelBackgroundBrush.ToDxBrush(RenderTarget);
					RenderTarget.FillRectangle(labelRect, bg);
					bg.Dispose();
				}

				SharpDX.Direct2D1.Brush textBrush = (LabelTextBrush ?? System.Windows.Media.Brushes.White).ToDxBrush(RenderTarget);
				RenderTarget.DrawTextLayout(new SharpDX.Vector2(labelRect.X + padding, labelRect.Y + padding), textLayout, textBrush);
				textBrush.Dispose();
			}
		}

		private static string FormatPrice(double price, ChartPanel chartPanel)
		{
			try
			{
				if (chartPanel != null)
				{
					ChartBars cb = chartPanel.ChartObjects.OfType<ChartBars>().FirstOrDefault();
					if (cb != null && cb.Bars != null && cb.Bars.Instrument != null && cb.Bars.Instrument.MasterInstrument != null)
						return cb.Bars.Instrument.MasterInstrument.FormatPrice(price);
				}
			}
			catch { /* fall through */ }
			return price.ToString("F2");
		}
	}
}
