using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Robust.Client.Graphics;
using Robust.Shared.Animations;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using Robust.Shared.ViewVariables;
using Robust.Shared.Timing;
using Robust.Client.UserInterface.Translation;

namespace Robust.Client.UserInterface.Controls
{
    /// <summary>
    ///     A label is a GUI control that displays simple text.
    /// </summary>
    [Virtual]
    public class Label : Control
    {
        public const string StylePropertyFontColor = "font-color";
        public const string StylePropertyFont = "font";
        public const string StylePropertyAlignMode = "alignMode";

        private int _cachedTextHeight;
        private readonly List<int> _cachedTextWidths = new();
        private bool _textDimensionCacheValid;
        private string? _text;
        private ReadOnlyMemory<char> _textMemory;
        // Diagnostic decoration is display-only; public getters keep the original memory.
        private ReadOnlyMemory<char> _displayTextMemory;
        private int _translationRevision = -1;
        private bool _channelDisplayScope;
        private bool _displayRefreshPending;
        private string? _fixedChannelDisplay;
        private bool _clipText;
        private AlignMode _align;

        public Label()
        {
            VerticalAlignment = VAlignment.Center;
        }

        protected override void FrameUpdate(FrameEventArgs args)
        {
            base.FrameUpdate(args);
            RefreshDisplayText();
        }

        private void RefreshDisplayText(bool sourceChanged = false)
        {
            if (sourceChanged)
                _fixedChannelDisplay = _textMemory.Length <= 32 &&
                    ChatChannelDisplayNames.TryTranslate(_text ?? _textMemory.ToString(), out var translated)
                    ? translated : null;
            var channelScope = _fixedChannelDisplay != null && ChatChannelDisplayNames.IsChannelControl(this);
            var mode = TranslationDisplayPolicy.Get(this);
            var revision = HashCode.Combine(TextTranslationService.Session?.Revision ?? -1, TranslationDisplayPolicy.Revision, (int)TranslationDisplayPolicy.Get(this));
            if (!sourceChanged && !_displayRefreshPending &&
                _channelDisplayScope == channelScope && _translationRevision == revision) return;
            _channelDisplayScope = channelScope;
            _translationRevision = revision;
            var candidate = _fixedChannelDisplay != null;
            // Content constructs Labels before parenting them. Do not enqueue possible
            // channel names until the control ancestry can establish their meaning.
            _displayRefreshPending = candidate && Parent == null && !channelScope;
            var display = mode == TranslationDisplayMode.Original ? _textMemory : channelScope && candidate
                ? _fixedChannelDisplay.AsMemory()
                : _displayRefreshPending ? _textMemory : TextCaptureDiagnostics.Session.Decorate(_textMemory, mode == TranslationDisplayMode.Normal);
            if (display.Span.SequenceEqual(_displayTextMemory.Span)) return;
            _displayTextMemory = display;
            _textDimensionCacheValid = false;
            InvalidateMeasure();
        }

        /// <summary>
        ///     The text to display.
        /// </summary>
        /// <remarks>
        /// Replaces <see cref="TextMemory"/> when set.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// Thrown if <see cref="TextMemory"/> was set directly and there is no backing string instance to fetch.
        /// </exception>
        [ViewVariables]
        public string? Text
        {
            get => _text ?? (_textMemory.Length > 0 ? throw new InvalidOperationException("Label uses TextMemory, cannot fetch string text.") : null);
            set
            {
                _text = value;
                _textMemory = value.AsMemory();
                RefreshDisplayText(sourceChanged: true);
                _textDimensionCacheValid = false;
                InvalidateMeasure();
            }
        }

        /// <summary>
        /// The text to display, set as a read-only memory.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Note that updating the backing memory while the control is using it can result in incorrect display due to caching of measure information and similar.
        /// If you modify the backing storage, re-assign the property to invalidate these.
        /// </para>
        /// <para>
        /// Sets <see cref="Text"/> to throw an exception if read, as there is no backing string to retrieve.
        /// </para>
        /// </remarks>
        public ReadOnlyMemory<char> TextMemory
        {
            get => _textMemory;
            set
            {
                _text = null;
                _textMemory = value;
                RefreshDisplayText(sourceChanged: true);
                _textDimensionCacheValid = false;
                InvalidateMeasure();
            }
        }

        [ViewVariables]
        public bool ClipText
        {
            get => _clipText;
            set
            {
                _clipText = value;
                RectClipContent = value;
                InvalidateMeasure();
            }
        }

        [ViewVariables] public AlignMode Align {
            get
            {
                if (TryGetStyleProperty<AlignMode>(StylePropertyAlignMode, out var alignMode))
                {
                    return alignMode;
                }

                return _align;
            }
            set => _align = value;
        }

        [ViewVariables] public VAlignMode VAlign { get; set; }

        public Font? FontOverride { get; set; }

        private Font ActualFont
        {
            get
            {
                if (FontOverride != null)
                {
                    return FontOverride;
                }

                if (TryGetStyleProperty<Font>(StylePropertyFont, out var font))
                {
                    return font;
                }

                return UserInterfaceManager.ThemeDefaults.LabelFont;
            }
        }

        public Color? FontColorShadowOverride { get; set; }

        private Color ActualFontColor
        {
            get
            {
                if (FontColorOverride.HasValue)
                {
                    return FontColorOverride.Value;
                }

                if (TryGetStyleProperty<Color>(StylePropertyFontColor, out var color))
                {
                    return color;
                }

                return Color.White;
            }
        }

        [ViewVariables(VVAccess.ReadWrite)]
        [Animatable]
        public Color? FontColorOverride { get; set; }

        public int? ShadowOffsetXOverride { get; set; }

        public int? ShadowOffsetYOverride { get; set; }


        protected internal override void Draw(DrawingHandleScreen handle)
        {
            RefreshDisplayText();
            if (_textMemory.Length == 0)
            {
                return;
            }

            if (!_textDimensionCacheValid)
            {
                _calculateTextDimension();
                DebugTools.Assert(_textDimensionCacheValid);
            }

            int vOffset;
            switch (VAlign)
            {
                case VAlignMode.Top:
                    vOffset = 0;
                    break;
                case VAlignMode.Fill:
                case VAlignMode.Center:
                    vOffset = (PixelSize.Y - _cachedTextHeight) / 2;
                    break;
                case VAlignMode.Bottom:
                    vOffset = PixelSize.Y - _cachedTextHeight;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            var newlines = 0;
            var font = ActualFont;
            var actualFontColor = ActualFontColor;

            Vector2 CalcBaseline()
            {
                DebugTools.Assert(_textDimensionCacheValid);

                int hOffset;
                switch (Align)
                {
                    case AlignMode.Left:
                        hOffset = 0;
                        break;
                    case AlignMode.Center:
                    case AlignMode.Fill:
                        hOffset = (PixelSize.X - _cachedTextWidths[newlines]) / 2;
                        break;
                    case AlignMode.Right:
                        hOffset = PixelSize.X - _cachedTextWidths[newlines];
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                return new Vector2(hOffset, font.GetAscent(UIScale) + font.GetLineHeight(UIScale) * newlines + vOffset);
            }

            var baseLine = CalcBaseline();

            foreach (var rune in _displayTextMemory.Span.EnumerateRunes())
            {
                if (rune == new Rune('\n'))
                {
                    newlines += 1;
                    baseLine = CalcBaseline();
                }

                var advance = font.DrawChar(handle, rune, baseLine, UIScale, actualFontColor);
                baseLine += new Vector2(advance, 0);
            }
        }

        public enum AlignMode : byte
        {
            Left = 0,
            Center = 1,
            Right = 2,
            Fill = 3
        }

        public enum VAlignMode : byte
        {
            Top = 0,
            Center = 1,
            Bottom = 2,
            Fill = 3
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            RefreshDisplayText();
            if (!_textDimensionCacheValid)
            {
                _calculateTextDimension();
                DebugTools.Assert(_textDimensionCacheValid);
            }

            if (ClipText)
            {
                return new Vector2(0, _cachedTextHeight / UIScale);
            }

            var totalWidth = 0;
            foreach (var width in _cachedTextWidths)
            {
                totalWidth = Math.Max(totalWidth, width);
            }

            return new Vector2(totalWidth / UIScale, _cachedTextHeight / UIScale);
        }

        protected internal override void UIScaleChanged()
        {
            _textDimensionCacheValid = false;

            base.UIScaleChanged();
        }

        private void _calculateTextDimension()
        {
            _cachedTextWidths.Clear();
            _cachedTextWidths.Add(0);

            if (_textMemory.Length == 0)
            {
                _cachedTextHeight = 0;
                _textDimensionCacheValid = true;
                return;
            }

            var font = ActualFont;
            var height = font.GetHeight(UIScale);
            foreach (var rune in _displayTextMemory.Span.EnumerateRunes())
            {
                if (rune == new Rune('\n'))
                {
                    _cachedTextWidths.Add(0);
                    height += font.GetLineHeight(UIScale);
                }
                else
                {
                    var metrics = font.GetCharMetrics(rune, UIScale);
                    if (metrics == null)
                    {
                        continue;
                    }

                    _cachedTextWidths[^1] += metrics.Value.Advance;
                }
            }

            _cachedTextHeight = height;
            _textDimensionCacheValid = true;
        }

        protected override void StylePropertiesChanged()
        {
            _textDimensionCacheValid = false;

            base.StylePropertiesChanged();
        }
    }
}
