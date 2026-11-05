using System;
using System.Diagnostics.Contracts;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.RichText;
using Robust.Client.UserInterface.Translation;
using Robust.Shared.Collections;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using Robust.Shared.Timing;

namespace Robust.Client.UserInterface.Controls
{
    /// <summary>
    ///     A control to handle output of message-by-message output panels, like the debug console and chat panel.
    /// </summary>
    [Virtual]
    public class OutputPanel : Control
    {
        public const string StyleClassOutputPanelScrollDownButton = "outputPanelScrollDownButton";

        [Dependency] private readonly MarkupTagManager _tagManager = default!;

        public const string StylePropertyStyleBox = "stylebox";

        public bool ShowScrollDownButton
        {
            get => _showScrollDownButton;
            set
            {
                _showScrollDownButton = value;
                _updateScrollButtonVisibility();
            }
        }
        private bool _showScrollDownButton;

        private readonly RingBufferList<RichTextEntry> _entries = new();
        private bool _isAtBottom = true;

        private int _totalContentHeight;
        private bool _firstLine = true;
        private StyleBox? _styleBoxOverride;
        private VScrollBar _scrollBar;
        private Button _scrollDownButton;

        public bool ScrollFollowing { get; set; } = true;

        private bool _invalidOnVisible;
        private int _translationRevision = -1;
        private int _chatTranslationRevision = -1;
        private bool _wasChatOutput;
        private float _translationRefreshCountdown;

        protected override void FrameUpdate(FrameEventArgs args)
        {
            base.FrameUpdate(args);
            if (!VisibleInTree || _entries.Count == 0)
                return;

            var isChatOutput = ChatTranslationScope.IsChatControl(this);
            var uiRevision = HashCode.Combine(TextTranslationService.Session?.Revision ?? -1, TranslationDisplayPolicy.Revision, (int)TranslationDisplayPolicy.Get(this));
            var chatRevision = HashCode.Combine(ChatTranslationService.Session?.Revision ?? -1, TranslationDisplayPolicy.Revision, (int)TranslationDisplayPolicy.Get(this));
            _translationRefreshCountdown -= args.DeltaSeconds;
            // Separate counters prevent equal revision numbers in unrelated services
            // from hiding a completed chat translation. Non-chat panels ignore chat work.
            var changed = isChatOutput != _wasChatOutput || (isChatOutput
                ? _chatTranslationRevision != chatRevision
                : _translationRevision != uiRevision);
            if (!changed || (_translationRefreshCountdown > 0 && !TranslationDisplayPolicy.ChangedRecently))
                return;

            _wasChatOutput = isChatOutput;
            _translationRevision = uiRevision;
            _chatTranslationRevision = chatRevision;
            _translationRefreshCountdown = 0.2f;
            RefreshTranslatedEntries(isChatOutput);
        }

        private void RefreshTranslatedEntries(bool isChatOutput)
        {
            var changed = false;
            foreach (ref var entry in _entries)
                changed |= entry.RefreshTranslation(isChatOutput);
            if (!changed) return;

            var followBottom = _isAtBottom && ScrollFollowing;
            var font = _getFont();
            var separation = font.GetLineSeparation(UIScale);
            var valueAnchor = GetScrollAnchor(_scrollBar.Value, separation);
            var targetAnchor = GetScrollAnchor(_scrollBar.ValueTarget, separation);

            // Update existing entries in place: their markup controls and original messages
            // must survive. Rebuilding messages would also reset the chat scroll position.
            // Translation completion does not change every historical line. Preserve the
            // old layout of unchanged entries; resize/font paths still invalidate all.
            var sizeX = _getContentBox().Width;
            _totalContentHeight = 0;
            foreach (ref var entry in _entries)
            {
                if (entry.TranslationLayoutDirty)
                    entry.UpdateLayout(_tagManager, font, sizeX, UIScale);
                _totalContentHeight += entry.Height + separation;
            }
            _scrollBar.MaxValue = Math.Max(_scrollBar.Page, _totalContentHeight);
            if (followBottom)
            {
                _scrollBar.MoveToEnd();
                _isAtBottom = true;
            }
            else
            {
                _scrollBar.Value = RestoreScrollAnchor(valueAnchor, separation);
                _scrollBar.ValueTarget = RestoreScrollAnchor(targetAnchor, separation);
                _isAtBottom = _scrollBar.IsAtEnd;
            }
            _updateScrollButtonVisibility();
        }

        private (int Entry, float Offset) GetScrollAnchor(float value, int separation)
        {
            var top = 0f;
            for (var i = 0; i < _entries.Count; i++)
            {
                var span = _entries[i].Height + separation;
                if (value < top + span || i == _entries.Count - 1)
                    return (i, Math.Max(0, value - top));
                top += span;
            }
            return (0, 0);
        }

        private float RestoreScrollAnchor((int Entry, float Offset) anchor, int separation)
        {
            var top = 0f;
            for (var i = 0; i < anchor.Entry; i++)
                top += _entries[i].Height + separation;
            return top + Math.Min(anchor.Offset, Math.Max(0, _entries[anchor.Entry].Height + separation - 1));
        }

        public OutputPanel()
        {
            IoCManager.InjectDependencies(this);
            MouseFilter = MouseFilterMode.Pass;
            RectClipContent = true;

            _scrollBar = new VScrollBar
            {
                Name = "_v_scroll",
                HorizontalAlignment = HAlignment.Right
            };
            AddChild(_scrollBar);

            AddChild(_scrollDownButton = new Button()
            {
                Name = "scrollLiveBtn",
                StyleClasses = { StyleClassOutputPanelScrollDownButton },
                VerticalAlignment = VAlignment.Bottom,
                HorizontalAlignment = HAlignment.Center,
                Text = String.Format("⬇    {0}    ⬇", Loc.GetString("output-panel-scroll-down-button-text")),
                MaxWidth = 300,
                Visible = false,
            });

            _scrollDownButton.OnPressed += _ => ScrollToBottom();

            _scrollBar.OnValueChanged += _ =>
            {
                _isAtBottom = _scrollBar.IsAtEnd;
                _updateScrollButtonVisibility();
            };
        }

        public int EntryCount => _entries.Count;

        public StyleBox? StyleBoxOverride
        {
            get => _styleBoxOverride;
            set
            {
                _styleBoxOverride = value;
                InvalidateMeasure();
                _invalidateEntries();
            }
        }

        public void Clear()
        {
            _firstLine = true;

            foreach (var entry in _entries)
            {
                entry.RemoveControls();
            }

            _entries.Clear();
            _totalContentHeight = 0;
            _scrollBar.MaxValue = Math.Max(_scrollBar.Page, _totalContentHeight);
            _scrollBar.Value = 0;
        }

        public FormattedMessage GetMessage(Index index)
        {
            return new FormattedMessage(_entries[index].Message);
        }

        public void RemoveEntry(Index index)
        {
            var entry = _entries[index];
            entry.RemoveControls();
            _entries.RemoveAt(index.GetOffset(_entries.Count));

            var font = _getFont();
            _totalContentHeight -= entry.Height + font.GetLineSeparation(UIScale);
            if (_entries.Count == 0)
            {
                Clear();
            }

            _scrollBar.MaxValue = Math.Max(_scrollBar.Page, _totalContentHeight);
        }

        // UI-thread-only, read-only context snapshot. Use original literal text, never
        // translated display nodes, markup attributes, or embedded child controls.
        internal string? GetRecentContextMessage(int offsetFromNewest)
        {
            if (offsetFromNewest < 0 || offsetFromNewest >= 200 || offsetFromNewest >= _entries.Count)
                return null;
            return _entries[_entries.Count - 1 - offsetFromNewest].Message.ToString();
        }

        public void AddText(string text)
        {
            var msg = new FormattedMessage();
            msg.AddText(text);
            AddMessage(msg);
        }

        public void AddMessage(FormattedMessage message)
        {
            var entry = new RichTextEntry(message, this, _tagManager, null);

            entry.Update(_tagManager, _getFont(), _getContentBox().Width, UIScale, lineHeightScale: 1, allowChatEnqueue: true);

            _entries.Add(entry);
            var font = _getFont();
            AddNewItemHeight(font, entry);

            _scrollBar.MaxValue = Math.Max(_scrollBar.Page, _totalContentHeight);
            if (_isAtBottom && ScrollFollowing)
            {
                _scrollBar.MoveToEnd();
            }
        }

        public void SetMessage(Index index, FormattedMessage message, Type[]? tagsAllowed = null, Color? defaultColor = null)
        {
            var atBottom = !_scrollDownButton.Visible;
            var oldEntry = _entries[index];
            var font = _getFont();
            _totalContentHeight -= oldEntry.Height + font.GetLineSeparation(UIScale);
            _scrollBar.MaxValue = Math.Max(_scrollBar.Page, _totalContentHeight);

            var entry = new RichTextEntry(message, this, _tagManager, tagsAllowed, defaultColor);
            entry.Update(_tagManager, _getFont(), _getContentBox().Width, UIScale, lineHeightScale: 1, allowChatEnqueue: true);
            _entries[index] = entry;

            AddNewItemHeight(font, in entry);

            _scrollBar.MaxValue = Math.Max(_scrollBar.Page, _totalContentHeight);
            if (atBottom)
                _scrollBar.Value = _scrollBar.MaxValue;
        }

        private void AddNewItemHeight(Font font, in RichTextEntry entry)
        {
            _totalContentHeight += entry.Height;
            if (_firstLine)
            {
                _firstLine = false;
            }
            else
            {
                _totalContentHeight += font.GetLineSeparation(UIScale);
            }
        }

        public void ScrollToBottom()
        {
            _scrollBar.MoveToEnd();
            _isAtBottom = true;
        }

        protected internal override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);

            var style = _getStyleBox();
            var font = _getFont();
            var lineSeparation = font.GetLineSeparation(UIScale);
            style?.Draw(handle, PixelSizeBox, UIScale);
            var contentBox = _getContentBox();

            var entryOffset = -_scrollBar.Value;

            // A stack for format tags.
            // This stack contains the format tag to RETURN TO when popped off.
            // So when a new color tag gets hit this stack gets the previous color pushed on.
            var context = new MarkupDrawingContext(2);

            foreach (ref var entry in _entries)
            {
                if (entryOffset + entry.Height < 0)
                {
                    // Controls within the entry are the children of this control, which means they are drawn separately
                    // after this Draw call, so we have to mark them as invisible to prevent them from being drawn.
                    //
                    // An alternative option is to ensure that the control position updating logic in entry.Draw is always
                    // run, and then setting RectClipContent = true to use scissor box testing to handle the controls
                    // visibility
                    entry.HideControls();
                    entryOffset += entry.Height + lineSeparation;
                    continue;
                }

                if (entryOffset > contentBox.Height)
                {
                    entry.HideControls();

                    // We know that every subsequent entry will also fail the test, but we also need to
                    // hide all the controls, so we cannot simply break out of the loop
                    continue;
                }

                entry.Draw(_tagManager, handle, font, contentBox, entryOffset, context, UIScale);

                entryOffset += entry.Height + lineSeparation;
            }
        }

        protected internal override void MouseWheel(GUIMouseWheelEventArgs args)
        {
            base.MouseWheel(args);

            if (MathHelper.CloseToPercent(0, args.Delta.Y))
            {
                return;
            }

            _scrollBar.ValueTarget -= _getScrollSpeed() * args.Delta.Y;
        }

        protected override void Resized()
        {
            base.Resized();

            var styleBoxSize = _getStyleBox()?.MinimumSize.Y ?? 0;

            _scrollBar.Page = UIScale * (Height - styleBoxSize);
            _updateScrollButtonVisibility();
            _invalidateEntries();
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            return _getStyleBox()?.MinimumSize ?? Vector2.Zero;
        }

        private void _invalidateEntries()
        {
            _totalContentHeight = 0;
            var font = _getFont();
            var sizeX = _getContentBox().Width;
            // History is cache-only: a completed translation may change geometry,
            // but resizing/revisions must never re-enqueue evicted old chat requests.
            foreach (ref var entry in _entries)
            {
                entry.Update(_tagManager, font, sizeX, UIScale, lineHeightScale: 1, allowChatEnqueue: false);
                _totalContentHeight += entry.Height + font.GetLineSeparation(UIScale);
            }

            _scrollBar.MaxValue = Math.Max(_scrollBar.Page, _totalContentHeight);
            if (_isAtBottom && ScrollFollowing)
            {
                _scrollBar.MoveToEnd();
            }
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

        [Pure]
        private StyleBox? _getStyleBox()
        {
            if (StyleBoxOverride != null)
            {
                return StyleBoxOverride;
            }

            TryGetStyleProperty<StyleBox>(StylePropertyStyleBox, out var box);
            return box;
        }

        [Pure]
        private float _getScrollSpeed()
        {
            // The scroll speed depends on the UI scale because the scroll bar is working with physical pixels.
            return GetScrollSpeed(_getFont(), UIScale);
        }

        [Pure]
        private UIBox2 _getContentBox()
        {
            var style = _getStyleBox();
            var box = style?.GetContentBox(PixelSizeBox, UIScale) ?? PixelSizeBox;
            box.Right = Math.Max(box.Left, box.Right - _scrollBar.DesiredPixelSize.X);
            return box;
        }

        protected internal override void UIScaleChanged()
        {
            // If this control isn't visible, don't invalidate entries immediately.
            // This saves invalidating the debug console if it's hidden,
            // which is a huge boon as auto-scaling changes UI scale a lot in that scenario.
            if (!VisibleInTree)
                _invalidOnVisible = true;
            else
                _invalidateEntries();

            base.UIScaleChanged();
        }

        internal static float GetScrollSpeed(Font font, float scale)
        {
            return font.GetLineHeight(scale) * 2;
        }

        protected override void EnteredTree()
        {
            base.EnteredTree();
            // Due to any number of reasons the entries may be invalidated if added when not visible in the tree.
            // e.g. the control has not had its UI scale set and the messages were added, but the
            // existing ones were valid when the UI scale was set.
            _invalidateEntries();
        }

        protected override void VisibilityChanged(bool newVisible)
        {
            if (newVisible && _invalidOnVisible)
            {
                _invalidateEntries();
                _invalidOnVisible = false;
            }
        }

        private void _updateScrollButtonVisibility()
        {
            _scrollDownButton.Visible = ShowScrollDownButton && !_isAtBottom;
        }
    }
}
