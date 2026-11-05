using System;
using Robust.Client.UserInterface.Translation;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using Robust.Shared.Timing;
using Robust.Shared.ViewVariables;

namespace Robust.Client.UserInterface.Controls
{
    [Virtual]
    public class RichTextLabel : Control
    {
        [Dependency] private readonly MarkupTagManager _tagManager = default!;

        private RichTextEntry? _entry;
        private float _lineHeightScale = 1;
        private bool _lineHeightOverride;
        private int _translationRevision = -1;
        private float _translationRefreshCountdown;

        protected override void FrameUpdate(FrameEventArgs args)
        {
            base.FrameUpdate(args);
            var service = TextTranslationService.Session;
            if (service == null || !VisibleInTree || _entry == null)
                return;

            var revision = HashCode.Combine(service.Revision, TranslationDisplayPolicy.Revision, (int)TranslationDisplayPolicy.Get(this));
            _translationRefreshCountdown -= args.DeltaSeconds;
            if (_translationRevision == revision || (_translationRefreshCountdown > 0 && !TranslationDisplayPolicy.ChangedRecently))
                return;

            // Cache writes happen in the background. Only invalidate layout on the UI thread,
            // coalescing completed batches rather than remeasuring for every translated string.
            _translationRevision = revision;
            _translationRefreshCountdown = 0.2f;
            var entry = _entry.Value;
            if (!entry.RefreshTranslation()) return;
            _entry = entry;
            InvalidateMeasure();
        }

        [ViewVariables(VVAccess.ReadWrite)]
        public float LineHeightScale
        {
            get
            {
                if (!_lineHeightOverride && TryGetStyleProperty(nameof(LineHeightScale), out float value))
                    return value;

                return _lineHeightScale;
            }
            set
            {
                _lineHeightScale = value;
                _lineHeightOverride = true;
                InvalidateMeasure();
            }
        }

        public string? Text
        {
            get => _entry?.Message.ToMarkup();
            set
            {
                if (value == null)
                    Clear();
                else
                    SetMessage(FormattedMessage.FromMarkupPermissive(value));
            }
        }

        public void Clear()
        {
            _entry?.RemoveControls();
            _entry = null;
            InvalidateMeasure();
        }

        public IEnumerable<Control> Controls => _entry?.Controls?.Values ?? Enumerable.Empty<Control>();
        public IReadOnlyList<MarkupNode> Nodes => _entry?.Message.Nodes ?? Array.Empty<MarkupNode>();

        public RichTextLabel()
        {
            IoCManager.InjectDependencies(this);
            VerticalAlignment = VAlignment.Center;
        }

        public void SetMessage(FormattedMessage message, Type[]? tagsAllowed = null, Color? defaultColor = null)
        {
            _entry?.RemoveControls();
            _entry = new RichTextEntry(message, this, _tagManager, tagsAllowed, defaultColor);
            InvalidateMeasure();
        }

        public void SetMessage(string message, Type[]? tagsAllowed = null, Color? defaultColor = null)
        {
            var msg = new FormattedMessage();
            msg.AddText(message);
            SetMessage(msg, tagsAllowed, defaultColor);
        }

        public string? GetMessage() => _entry?.Message.ToMarkup();

        /// <summary>
        /// Returns a copy of the currently used formatted message.
        /// </summary>
        public FormattedMessage? GetFormattedMessage() => _entry == null ? null : new FormattedMessage(_entry.Value.Message);

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            if (_entry == null)
                return Vector2.Zero;

            var font = _getFont();

            // _entry is nullable struct.
            // cannot just call _entry.Value.Update() as that doesn't actually update _entry.
            _entry = _entry.Value.Update(_tagManager, font, availableSize.X * UIScale, UIScale, LineHeightScale);

            return new Vector2(_entry.Value.Width / UIScale, _entry.Value.Height / UIScale);
        }

        protected internal override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);
            if (_entry is { } entry)
                ItemGlossary.CaptureVisibleRichText(this, entry.Message, entry.DisplayMessage);
            _entry?.Draw(_tagManager, handle, _getFont(), SizeBox, 0, new MarkupDrawingContext(), UIScale, LineHeightScale);
        }

        [Pure]
        private Font _getFont()
        {
            if (TryGetStyleProperty<Font>("font", out var font))
            {
                return font;
            }

            return UserInterfaceManager.ThemeDefaults.DefaultFont;
        }
    }
}
