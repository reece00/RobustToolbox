using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Client.UserInterface.Translation;
using Robust.Shared.Collections;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Robust.Client.UserInterface
{
    /// <summary>
    ///     Used by <see cref="OutputPanel"/> and <see cref="RichTextLabel"/> to handle rich text layout.
    ///     Note that if this text is ever removed or modified without removing the owning control,
    ///     then <see cref="RemoveControls"/> should be called to ensure that any controls that were added by this
    ///     entry are also removed.
    /// </summary>
    internal struct RichTextEntry
    {
        private readonly Color _defaultColor;
        private readonly Type[]? _tagsAllowed;
        private readonly Control _owner;
        private bool _pendingChatAdmission;

        public readonly FormattedMessage Message;
        // Keep Message unchanged for getters/copying; layout and drawing share this snapshot.
        private FormattedMessage _displayMessage;
        private bool _translationLayoutDirty;
        internal readonly FormattedMessage DisplayMessage => _displayMessage;
        internal readonly bool TranslationLayoutDirty => _translationLayoutDirty;

        /// <summary>
        ///     The vertical size of this entry, in pixels.
        /// </summary>
        public int Height;

        /// <summary>
        ///     The horizontal size of this entry, in pixels.
        /// </summary>
        public int Width;

        /// <summary>
        ///     The combined text indices in the message's text tags to put line breaks.
        /// </summary>
        public ValueList<int> LineBreaks;

        public readonly Dictionary<int, Control>? Controls;

        public RichTextEntry(FormattedMessage message, Control parent, MarkupTagManager tagManager, Type[]? tagsAllowed = null, Color? defaultColor = null)
        {
            Message = message;
            _owner = parent;
            _pendingChatAdmission = false;
            // Translation is selected during layout, when the owner has its chat ancestry.
            // Literal/tag node indices are identical in the eventual display snapshot.
            _displayMessage = message;
            _translationLayoutDirty = false;
            Height = 0;
            Width = 0;
            LineBreaks = default;
            _defaultColor = defaultColor ?? new(200, 200, 200);
            _tagsAllowed = tagsAllowed;
            Dictionary<int, Control>? tagControls = null;

            var nodeIndex = -1;
            foreach (var node in _displayMessage)
            {
                nodeIndex++;

                if (node.Name == null)
                    continue;

                if (!tagManager.TryGetMarkupTagHandler(node.Name, _tagsAllowed, out var handler) || !handler.TryCreateControl(node, out var control))
                    continue;

                // Markup tag handler instances are shared across controls. We need to ensure that the hanlder doesn't
                // store state information and return the same control for each rich text entry.
                DebugTools.Assert(handler.TryCreateControl(node, out var other) && other != control);

                parent.Children.Add(control);
                tagControls ??= new Dictionary<int, Control>();
                tagControls.Add(nodeIndex, control);
            }

            Controls = tagControls;
        }

        // TODO RICH TEXT
        // Somehow ensure that this **has** to be called when removing rich text from some control.
        /// <summary>
        /// Remove all owned controls from their parents.
        /// </summary>
        public readonly void RemoveControls()
        {
            if (Controls == null)
                return;

            foreach (var ctrl in Controls.Values)
            {
                ctrl.Orphan();
            }
        }

        /// <summary>
        ///     Recalculate line dimensions and where it has line breaks for word wrapping.
        /// </summary>
        /// <param name="defaultFont">The font being used for display.</param>
        /// <param name="maxSizeX">The maximum horizontal size of the container of this entry.</param>
        /// <param name="uiScale"></param>
        /// <param name="lineHeightScale"></param>
        public RichTextEntry Update(MarkupTagManager tagManager, Font defaultFont, float maxSizeX, float uiScale, float lineHeightScale = 1)
            => Update(tagManager, defaultFont, maxSizeX, uiScale, lineHeightScale, false);

        // Preserve the original Update signature. Only newly added/replaced chat entries
        // use this overload; reflow passes do not gain new network admission opportunities.
        internal RichTextEntry Update(MarkupTagManager tagManager, Font defaultFont, float maxSizeX, float uiScale, float lineHeightScale, bool allowChatEnqueue)
        {
            // A message can be populated before its OutputPanel is attached to ChatBox.
            // Carry its one admission opportunity until a layout recognizes that scope.
            _pendingChatAdmission |= allowChatEnqueue;
            RefreshTranslation();
            return UpdateLayout(tagManager, defaultFont, maxSizeX, uiScale, lineHeightScale);
        }

        // Revision refreshes use this only for entries whose displayed literals changed.
        // Ordinary Update still reflows unconditionally for size/font/style changes.
        internal RichTextEntry UpdateLayout(MarkupTagManager tagManager, Font defaultFont, float maxSizeX, float uiScale, float lineHeightScale = 1)
        {
            _translationLayoutDirty = false;

            // This method is gonna suck due to complexity.
            // Bear with me here.
            // I am so deeply sorry for the person adding stuff to this in the future.

            Height = defaultFont.GetHeight(uiScale);
            LineBreaks.Clear();

            int? breakLine;
            var wordWrap = new WordWrap(maxSizeX);
            var context = new MarkupDrawingContext();
            context.Font.Push(defaultFont);
            context.Color.Push(_defaultColor);

            // Go over every node.
            // Nodes can change the markup drawing context and return additional text.
            // It's also possible for nodes to return inline controls. They get treated as one large rune.
            var nodeIndex = -1;
            foreach (var node in _displayMessage)
            {
                nodeIndex++;
                var text = ProcessNode(tagManager, node, context);

                if (!context.Font.TryPeek(out var font))
                    font = defaultFont;

                // And go over every character.
                foreach (var rune in text.EnumerateRunes())
                {
                    if (ProcessRune(ref this, rune, out breakLine))
                        continue;

                    // Uh just skip unknown characters I guess.
                    if (!font.TryGetCharMetrics(rune, uiScale, out var metrics))
                        continue;

                    if (ProcessMetric(ref this, metrics, out breakLine))
                        return this;
                }

                if (Controls == null || !Controls.TryGetValue(nodeIndex, out var control))
                    continue;

                control.Measure(new Vector2(maxSizeX, Height));

                var desiredSize = control.DesiredPixelSize;
                var controlMetrics = new CharMetrics(
                    0, 0,
                    desiredSize.X,
                    desiredSize.X,
                    desiredSize.Y);

                if (ProcessMetric(ref this, controlMetrics, out breakLine))
                    return this;
            }

            Width = wordWrap.FinalizeText(out breakLine);
            CheckLineBreak(ref this, breakLine);

            return this;

            bool ProcessRune(ref RichTextEntry src, Rune rune, out int? outBreakLine)
            {
                wordWrap.NextRune(rune, out breakLine, out var breakNewLine, out var skip);
                CheckLineBreak(ref src, breakLine);
                CheckLineBreak(ref src, breakNewLine);
                outBreakLine = breakLine;
                return skip;
            }

            bool ProcessMetric(ref RichTextEntry src, CharMetrics metrics, out int? outBreakLine)
            {
                wordWrap.NextMetrics(metrics, out breakLine, out var abort);
                CheckLineBreak(ref src, breakLine);
                outBreakLine = breakLine;
                return abort;
            }

            void CheckLineBreak(ref RichTextEntry src, int? line)
            {
                if (line is { } l)
                {
                    src.LineBreaks.Add(l);
                    if (!context.Font.TryPeek(out var font))
                        font = defaultFont;

                    src.Height += GetLineHeight(font, uiScale, lineHeightScale);
                }
            }
        }

        internal bool RefreshTranslation(bool? isChatOutput = null)
        {
            // Only the actual chat output is routed here. Buttons, tooltips, other UI,
            // and debug-console OutputPanels continue using general UI translation.
            var isChat = isChatOutput ?? (_owner is OutputPanel && ChatTranslationScope.IsChatControl(_owner));
            var mode = TranslationDisplayPolicy.Get(_owner);
            var allowChatEnqueue = isChat && _pendingChatAdmission && mode == TranslationDisplayMode.Normal;
            if (isChat && mode == TranslationDisplayMode.Normal) _pendingChatAdmission = false;
            var chat = isChat ? ChatTranslationService.Session : null;
            FormattedMessage? display = _displayMessage.Count == Message.Count ? null : new FormattedMessage(Message.Count);
            for (var i = 0; i < Message.Count; i++)
            {
                // Keep one node per source node, including all embedded-control indices.
                // Translation output is literal text, never parsed as trusted markup.
                // Do not run the general decorator afterwards: chat mode off means raw.
                var node = Message[i];
                var previous = i < _displayMessage.Count ? _displayMessage[i] : null;
                var text = node.Value.StringValue ?? "";
                if (node.Name == null)
                    text = mode == TranslationDisplayMode.Original ? text : isChat
                        ? chat?.GetIncoming(text, "incoming-chat", allowEnqueue: allowChatEnqueue) ?? text
                        : TextCaptureDiagnostics.Session.Decorate(text, mode == TranslationDisplayMode.Normal);
                var same = node.Name == null
                    ? previous is { Name: null } && previous.Value.StringValue == text
                    : ReferenceEquals(previous, node);
                if (!same && display == null)
                {
                    // Reuse the complete old snapshot when nothing changed, and unchanged
                    // nodes when one literal did. Embedded-control node indices stay intact.
                    display = new FormattedMessage(Message.Count);
                    for (var before = 0; before < i; before++) display.PushTag(_displayMessage[before]);
                }
                display?.PushTag(same ? previous! : node.Name == null ? new MarkupNode(text) : node);
            }
            if (display == null) return false;
            _displayMessage = display;
            _translationLayoutDirty = true;
            return true;
        }

        internal readonly void HideControls()
        {
            if (Controls == null)
                return;

            foreach (var control in Controls.Values)
            {
                control.Visible = false;
            }
        }

        public readonly void Draw(
            MarkupTagManager tagManager,
            DrawingHandleBase handle,
            Font defaultFont,
            UIBox2 drawBox,
            float verticalOffset,
            MarkupDrawingContext context,
            float uiScale,
            float lineHeightScale = 1)
        {
            context.Clear();
            context.Color.Push(_defaultColor);
            context.Font.Push(defaultFont);

            var globalBreakCounter = 0;
            var lineBreakIndex = 0;
            var baseLine = drawBox.TopLeft + new Vector2(0, defaultFont.GetAscent(uiScale) + verticalOffset);
            var controlYAdvance = 0f;

            var nodeIndex = -1;
            foreach (var node in _displayMessage)
            {
                nodeIndex++;
                var text = ProcessNode(tagManager, node, context);
                if (!context.Color.TryPeek(out var color) || !context.Font.TryPeek(out var font))
                {
                    color = _defaultColor;
                    font = defaultFont;
                }

                foreach (var rune in text.EnumerateRunes())
                {
                    if (lineBreakIndex < LineBreaks.Count &&
                        LineBreaks[lineBreakIndex] == globalBreakCounter)
                    {
                        baseLine = new Vector2(drawBox.Left, baseLine.Y + GetLineHeight(font, uiScale, lineHeightScale) + controlYAdvance);
                        controlYAdvance = 0;
                        lineBreakIndex += 1;
                    }

                    var advance = font.DrawChar(handle, rune, baseLine, uiScale, color);
                    baseLine += new Vector2(advance, 0);

                    globalBreakCounter += 1;
                }

                if (Controls == null || !Controls.TryGetValue(nodeIndex, out var control))
                    continue;

                // Controls may have been previously hidden via HideControls due to being "out-of frame".
                // If this ever gets replaced with RectClipContents / scissor box testing, this can be removed.
                control.Visible = true;

                var invertedScale = 1f / uiScale;
                control.Position = new Vector2(baseLine.X * invertedScale, (baseLine.Y - defaultFont.GetAscent(uiScale)) * invertedScale);
                control.Measure(new Vector2(Width, Height));
                var advanceX = control.DesiredPixelSize.X;
                controlYAdvance = Math.Max(0f, (control.DesiredPixelSize.Y - GetLineHeight(font, uiScale, lineHeightScale)) * invertedScale);
                baseLine += new Vector2(advanceX, 0);
            }
        }

        private readonly string ProcessNode(MarkupTagManager tagManager, MarkupNode node, MarkupDrawingContext context)
        {
            // If a nodes name is null it's a text node.
            if (node.Name == null)
                return node.Value.StringValue ?? "";

            //Skip the node if there is no markup tag for it.
            if (!tagManager.TryGetMarkupTagHandler(node.Name, _tagsAllowed, out var tag))
                return "";

            if (!node.Closing)
            {
                tag.PushDrawContext(node, context);
                return tag.TextBefore(node);
            }

            tag.PopDrawContext(node, context);
            return tag.TextAfter(node);
        }

        private static int GetLineHeight(Font font, float uiScale, float lineHeightScale)
        {
            var height = font.GetLineHeight(uiScale);
            return (int)(height * lineHeightScale);
        }
    }
}
