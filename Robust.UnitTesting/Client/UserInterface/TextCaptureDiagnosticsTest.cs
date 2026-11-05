using System;
using NUnit.Framework;
using Robust.Client.UserInterface;
using Robust.Shared.Utility;

namespace Robust.UnitTesting.Client.UserInterface;

[TestFixture]
[TestOf(typeof(TextCaptureDiagnostics))]
public sealed class TextCaptureDiagnosticsTest
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("0")]
    [TestCase("true")]
    public void DefaultOffPreservesOriginal(string? mode)
    {
        var capture = TextCaptureDiagnostics.FromMode(mode);
        var text = "Original {0} [color=red]";
        Assert.That(capture.Decorate(text), Is.SameAs(text));
        var message = FormattedMessage.FromUnformatted(text);
        Assert.That(capture.Decorate(message), Is.SameAs(message));
    }

    [Test]
    public void RepeatedExactTextReusesIdAcrossMemoryAndRichText()
    {
        var capture = new TextCaptureDiagnostics(true);
        Assert.That(capture.Decorate("Hello"), Is.EqualTo("#测试1 Hello"));
        Assert.That(capture.Decorate("Hello".ToCharArray().AsMemory()).ToString(), Is.EqualTo("#测试1 Hello"));
        Assert.That(capture.Decorate(FormattedMessage.FromUnformatted("Hello")).ToString(), Is.EqualTo("#测试1 Hello"));
        Assert.That(capture.Decorate("hello"), Is.EqualTo("#测试2 hello"));
        Assert.That(capture.Decorate("Hello "), Is.EqualTo("#测试3 Hello "));
    }

    [Test]
    public void RichTextPreservesTagsIndicesAndSource()
    {
        var capture = new TextCaptureDiagnostics(true);
        var message = FormattedMessage.FromMarkupOrThrow("[color=red]Hello[/color]");
        message.AddText("{0} [font=evil]\\");
        var before = message.ToMarkup();
        var display = capture.Decorate(message);
        Assert.That(message.ToMarkup(), Is.EqualTo(before));
        Assert.That(display.Count, Is.EqualTo(message.Count));
        Assert.That(display[0], Is.SameAs(message[0]));
        Assert.That(display[2], Is.SameAs(message[2]));
        Assert.That(display[1].Value.StringValue, Is.EqualTo("#测试1 Hello"));
        Assert.That(display[3].Name, Is.Null);
        Assert.That(display[3].Value.StringValue, Is.EqualTo("#测试2 {0} [font=evil]\\"));
    }

    [Test]
    public void AsciiModeAndMemoryPreserveSource()
    {
        var capture = TextCaptureDiagnostics.FromMode("ascii");
        var source = "Original".ToCharArray();
        Assert.That(capture.Decorate(source.AsMemory()).ToString(), Is.EqualTo("#test1 Original"));
        Assert.That(new string(source), Is.EqualTo("Original"));
    }

    [Test]
    public void BoundsSkipNewTextWithoutRecyclingExistingIds()
    {
        var capture = new TextCaptureDiagnostics(true);
        for (var i = 0; i < TextCaptureDiagnostics.MaxEntries; i++)
            capture.Decorate(i.ToString());
        Assert.That(capture.Decorate("overflow"), Is.EqualTo("overflow"));
        Assert.That(capture.Decorate("0"), Is.EqualTo("#测试1 0"));
    }

    [Test]
    public void CharacterBudgetAndOversizeAreBounded()
    {
        var capture = new TextCaptureDiagnostics(true);
        var oversized = new string('a', TextCaptureDiagnostics.MaxTextLength + 1);
        Assert.That(capture.Decorate(oversized), Is.SameAs(oversized));
        var text = new string('a', TextCaptureDiagnostics.MaxTextLength - 4);
        for (var i = 0; i < 130; i++)
            capture.Decorate(text + i.ToString("D4"));
        Assert.That(capture.Decorate(text + "9999"), Is.EqualTo(text + "9999"));
        Assert.That(capture.Decorate(text + "0000"), Does.StartWith("#测试1 "));
    }

    [Test]
    public void ExportIsUnavailableToConnectedServers()
    {
        Assert.That(new TextCaptureExportCommand().RequireServerOrSingleplayer, Is.True);
    }

    [Test]
    public void SnapshotIsAnIndependentBoundedMapping()
    {
        var capture = new TextCaptureDiagnostics(true);
        capture.Decorate("Private original");
        var snapshot = capture.Snapshot();
        capture.Decorate("Second");
        Assert.That(snapshot, Has.Length.EqualTo(1));
        Assert.That(snapshot[0].Id, Is.EqualTo(1));
        Assert.That(snapshot[0].Text, Is.EqualTo("Private original"));
        Assert.That(snapshot[0].Display, Is.EqualTo("#测试1 Private original"));
        Assert.That(new TextCaptureDiagnostics().Snapshot(), Is.Empty);
    }

    [TestCase("")]
    [TestCase(" \n\t")]
    public void EmptyAndWhitespaceAreNotMarked(string source)
    {
        Assert.That(new TextCaptureDiagnostics(true).Decorate(source), Is.SameAs(source));
    }
}
