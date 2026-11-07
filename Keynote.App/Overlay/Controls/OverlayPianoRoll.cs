using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Keynote.Core;

namespace Keynote.App.Overlay.Controls
{
    public sealed class OverlayPianoRoll : FrameworkElement
    {
        private MidiSong? _song;
        private MidiNote[] _notes = Array.Empty<MidiNote>();
        private int _minPitch = 36;
        private int _maxPitch = 84;
        private double _durationSeconds = 120;
        private bool _isDraggingSeek = false;

        public static readonly DependencyProperty PositionProperty =
            DependencyProperty.Register(
                nameof(Position),
                typeof(TimeSpan),
                typeof(OverlayPianoRoll),
                new FrameworkPropertyMetadata(TimeSpan.Zero, FrameworkPropertyMetadataOptions.AffectsRender));

        public TimeSpan Position
        {
            get => (TimeSpan)GetValue(PositionProperty);
            set => SetValue(PositionProperty, value);
        }

        public MidiSong? Song
        {
            get => _song;
            set
            {
                _song = value;
                _notes = value?.Notes.ToArray() ?? Array.Empty<MidiNote>();
                if (_notes.Length > 0)
                {
                    _minPitch = Math.Max(0, _notes.Min(n => n.Pitch) - 2);
                    _maxPitch = Math.Min(127, _notes.Max(n => n.Pitch) + 2);
                    _durationSeconds = Math.Max(1.0, _notes.Max(n => (n.Start + n.Duration).TotalSeconds));
                }
                else
                {
                    _minPitch = 36;
                    _maxPitch = 84;
                    _durationSeconds = 120;
                }
                InvalidateVisual();
            }
        }

        public KeyboardMapping? Mapping { get; set; }
        public int Octave { get; set; }

        public event Action<TimeSpan>? SeekRequested;

        // Visual Brushes & Pens
        private static readonly Brush BackgroundBrush = MakeFrozenBrush(14, 11, 23);
        private static readonly Brush TimeHeaderBrush = MakeFrozenBrush(19, 15, 30);
        private static readonly Brush AccidentalLaneBrush = MakeFrozenBrush(13, 10, 21);
        private static readonly Brush NaturalLaneBrush = MakeFrozenBrush(17, 13, 27);

        private static readonly Brush MappedNoteBrush1 = MakeFrozenBrush(179, 136, 255);      // #B388FF
        private static readonly Brush MappedNoteBrush2 = MakeFrozenBrush(199, 167, 255);      // #C7A7FF
        private static readonly Brush MappedNoteGlowBrush = MakeFrozenBrush(168, 121, 255, 40); // Translucent glow
        private static readonly Brush UnmappedNoteBrush = MakeFrozenBrush(255, 107, 131);     // #FF6B83
        private static readonly Brush UnmappedNoteGlowBrush = MakeFrozenBrush(255, 75, 105, 50);

        private static readonly Brush LabelBrush = MakeFrozenBrush(163, 152, 178);            // Soft purple-grey
        private static readonly Brush EmptyTextBrush = MakeFrozenBrush(140, 130, 155);

        private static readonly Pen TimeHeaderBorderPen = MakeFrozenPen(46, 37, 66, 1.0);
        private static readonly Pen GridPen = MakeFrozenPen(32, 25, 48, 0.9);
        private static readonly Pen OctavePen = MakeFrozenPen(56, 43, 79, 1.0);

        private static readonly Pen PlayheadGlowPen = MakeFrozenPen(168, 121, 255, 5.0, 70);
        private static readonly Pen PlayheadCorePen = MakeFrozenPen(255, 255, 255, 1.5);
        private static readonly Brush PlayheadNodeBrush = MakeFrozenBrush(255, 255, 255);
        private static readonly Pen PlayheadNodePen = MakeFrozenPen(168, 121, 255, 1.5);

        private static Brush MakeFrozenBrush(byte r, byte g, byte b, byte a = 255)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }

        private static Pen MakeFrozenPen(byte r, byte g, byte b, double width, byte a = 255)
        {
            var pen = new Pen(MakeFrozenBrush(r, g, b, a), width);
            pen.Freeze();
            return pen;
        }

        public OverlayPianoRoll()
        {
            Focusable = false;
            ClipToBounds = true;
            Cursor = Cursors.Hand;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 40 || h < 30) return;

            // 1. Overall Dark Surface
            dc.DrawRoundedRectangle(
                BackgroundBrush,
                null,
                new Rect(0, 0, w, h),
                8, 8);

            const double leftMargin = 34.0;
            const double topMargin = 20.0;
            double chartW = Math.Max(10, w - leftMargin - 6);
            double chartH = Math.Max(10, h - topMargin - 4);
            double duration = Math.Max(0.1, _durationSeconds);
            int pitchRange = Math.Max(12, _maxPitch - _minPitch + 1);
            double rowH = chartH / pitchRange;
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            FormattedText MakeText(string text, double size, Brush brush) =>
                new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Variable Text, Segoe UI, sans-serif"), size, brush, dpi);

            // 2. Pitch Lanes (Alternating Naturals & Accidentals)
            for (int p = _minPitch; p <= _maxPitch; p++)
            {
                double y = topMargin + chartH - ((p - _minPitch + 1) * rowH);
                int noteInOctave = p % 12;
                bool isAccidental = noteInOctave is 1 or 3 or 6 or 8 or 10;
                var laneBrush = isAccidental ? AccidentalLaneBrush : NaturalLaneBrush;

                dc.DrawRectangle(laneBrush, null, new Rect(leftMargin, y, chartW, rowH));

                // Octave line & pitch label for each C
                if (noteInOctave == 0)
                {
                    dc.DrawLine(OctavePen, new Point(leftMargin, y), new Point(leftMargin + chartW, y));
                    var ft = MakeText(Notes.Name(p), 9.5, LabelBrush);
                    dc.DrawText(ft, new Point(Math.Max(2, leftMargin - ft.Width - 6), Math.Clamp(y - (ft.Height / 2), topMargin, h - 14)));
                }
            }

            // 3. Time Header Bar Background
            dc.DrawRectangle(TimeHeaderBrush, null, new Rect(0, 0, w, topMargin));
            dc.DrawLine(TimeHeaderBorderPen, new Point(0, topMargin), new Point(w, topMargin));

            // Time Ticks & Vertical Grid Lines
            for (int i = 0; i <= 4; i++)
            {
                double x = leftMargin + (chartW * i / 4.0);
                dc.DrawLine(GridPen, new Point(x, topMargin), new Point(x, topMargin + chartH));

                var time = TimeSpan.FromSeconds(duration * i / 4.0);
                string label = $"{(int)time.TotalMinutes}:{time.Seconds:D2}";
                var ft = MakeText(label, 9.5, LabelBrush);
                double textX = Math.Clamp(x - (ft.Width / 2), leftMargin, w - ft.Width - 4);
                dc.DrawText(ft, new Point(textX, 3));
            }

            // 4. Render MIDI Note Blocks
            if (_notes.Length > 0)
            {
                foreach (var n in _notes)
                {
                    double startSec = n.Start.TotalSeconds;
                    double noteX = leftMargin + (startSec / duration * chartW);
                    double rawW = n.Duration.TotalSeconds / duration * chartW;
                    double noteW = Math.Max(7.0, rawW); // Sensible visual minimum width so short notes are rounded bars, not dots

                    if (noteX + noteW > leftMargin + chartW)
                    {
                        noteW = Math.Max(4.0, (leftMargin + chartW) - noteX);
                    }

                    double noteY = topMargin + chartH - ((n.Pitch - _minPitch + 1) * rowH) + 1.0;
                    double noteH = Math.Max(3.5, Math.Min(8.0, rowH - 1.2));

                    // Check whether note is genuinely unmapped
                    bool isUnmapped = Mapping != null &&
                                      Mapping.Keys.Count > 0 &&
                                      string.IsNullOrWhiteSpace(Mapping.Lookup(n.Pitch, Octave));

                    Brush noteBrush;
                    Brush glowBrush;

                    if (isUnmapped)
                    {
                        noteBrush = UnmappedNoteBrush;
                        glowBrush = UnmappedNoteGlowBrush;
                    }
                    else
                    {
                        noteBrush = (n.Pitch % 2 == 0) ? MappedNoteBrush1 : MappedNoteBrush2;
                        glowBrush = MappedNoteGlowBrush;
                    }

                    // Subtle outer glow halo around each note
                    dc.DrawRoundedRectangle(glowBrush, null, new Rect(noteX - 0.8, noteY - 0.8, noteW + 1.6, noteH + 1.6), 2.5, 2.5);

                    // Crisp rounded rectangular note block
                    dc.DrawRoundedRectangle(noteBrush, null, new Rect(noteX, noteY, noteW, noteH), 2.0, 2.0);
                }
            }
            else
            {
                var emptyFt = MakeText("No MIDI song loaded in Larpiano", 12, EmptyTextBrush);
                dc.DrawText(emptyFt, new Point((w - emptyFt.Width) / 2 + 10, (h - emptyFt.Height) / 2 + 5));
            }

            // 5. Luminous Playhead Cursor
            double progressFraction = Math.Clamp(Position.TotalSeconds / duration, 0.0, 1.0);
            double cursorX = leftMargin + (progressFraction * chartW);

            // Playhead outer glow
            dc.DrawLine(PlayheadGlowPen, new Point(cursorX, topMargin), new Point(cursorX, topMargin + chartH));
            // Playhead crisp core line
            dc.DrawLine(PlayheadCorePen, new Point(cursorX, topMargin), new Point(cursorX, topMargin + chartH));

            // Top Playhead Diamond / Indicator Node
            var nodeCenter = new Point(cursorX, topMargin);
            var streamGeom = new StreamGeometry();
            using (var ctx = streamGeom.Open())
            {
                ctx.BeginFigure(new Point(cursorX, topMargin + 7), true, true);
                ctx.LineTo(new Point(cursorX - 4.5, topMargin), true, false);
                ctx.LineTo(new Point(cursorX + 4.5, topMargin), true, false);
            }
            streamGeom.Freeze();
            dc.DrawGeometry(PlayheadNodeBrush, PlayheadNodePen, streamGeom);
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isDraggingSeek = true;
                CaptureMouse();
                TriggerSeek(e.GetPosition(this));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_isDraggingSeek && e.LeftButton == MouseButtonState.Pressed)
            {
                TriggerSeek(e.GetPosition(this));
            }
            else if (e.LeftButton == MouseButtonState.Released && _isDraggingSeek)
            {
                _isDraggingSeek = false;
                ReleaseMouseCapture();
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if (_isDraggingSeek)
            {
                _isDraggingSeek = false;
                ReleaseMouseCapture();
            }
        }

        private void TriggerSeek(Point pt)
        {
            const double leftMargin = 34.0;
            double chartW = ActualWidth - leftMargin - 6;
            if (chartW <= 0) return;

            double fraction = Math.Clamp((pt.X - leftMargin) / chartW, 0.0, 1.0);
            double targetSec = fraction * _durationSeconds;
            SeekRequested?.Invoke(TimeSpan.FromSeconds(targetSec));
        }
    }
}
