using System;
using System.Reflection;
using NUnit.Framework;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Utility;

namespace Robust.UnitTesting.Client.UserInterface;

/// <summary>Run in separate processes with capture OFF and ROBUST_TEXT_CAPTURE=ascii/1.</summary>
[TestFixture]
public sealed class TextCaptureControlsTest : RobustUnitTest
{
    public override UnitTestProject Project => UnitTestProject.Client;

    [OneTimeSetUp]
    public void Setup()
    {
        IoCManager.Resolve<IUserInterfaceManagerInternal>().InitializeTesting();
    }

    [Test]
    public void LabelGettersKeepSourceWhileDisplayUsesDiagnosticCopy()
    {
        const string original = "Capture integration original";
        var label = new Label { Text = original };
        Assert.That(label.Text, Is.EqualTo(original));
        Assert.That(label.TextMemory.ToString(), Is.EqualTo(original));
        AssertDisplay(label, original);

        var buffer = original.ToCharArray();
        label.TextMemory = buffer.AsMemory();
        Assert.That(() => _ = label.Text, Throws.InvalidOperationException);
        Assert.That(label.TextMemory.ToString(), Is.EqualTo(original));
        Assert.That(new string(buffer), Is.EqualTo(original));
        AssertDisplay(label, original);

        buffer[0] = 'X';
        label.TextMemory = buffer.AsMemory();
        AssertDisplay(label, new string(buffer));
        label.Text = null;
        Assert.That(label.TextMemory.IsEmpty, Is.True);
        AssertDisplay(label, "");
    }

    [Test]
    public void RichTextGettersKeepSourceAndDisplayCopyPreservesNodePositions()
    {
        var source = FormattedMessage.FromMarkupOrThrow("[color=red]Capture integration original[/color]");
        var markup = source.ToMarkup();
        var label = new RichTextLabel();
        label.SetMessage(source);
        Assert.That(label.Text, Is.EqualTo(markup));
        Assert.That(label.GetMessage(), Is.EqualTo(markup));
        Assert.That(label.GetFormattedMessage()!.ToMarkup(), Is.EqualTo(markup));
        Assert.That(label.Nodes[1].Value.StringValue, Is.EqualTo("Capture integration original"));

        var entry = typeof(RichTextLabel).GetField("_entry", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(label)!;
        var display = (FormattedMessage) typeof(RichTextEntry)
            .GetField("_displayMessage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(entry)!;
        Assert.That(display.Count, Is.EqualTo(source.Count));
        Assert.That(display[0], Is.SameAs(source[0]));
        Assert.That(display[2], Is.SameAs(source[2]));
        Assert.That(display[1].Value.StringValue,
            Is.EqualTo(TextCaptureDiagnostics.Session.Decorate("Capture integration original")));
        Assert.That(source.ToMarkup(), Is.EqualTo(markup));
        label.Clear();
    }

    private static void AssertDisplay(Label label, string original)
    {
        var display = (ReadOnlyMemory<char>) typeof(Label)
            .GetField("_displayTextMemory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(label)!;
        Assert.That(display.ToString(), Is.EqualTo(TextCaptureDiagnostics.Session.Decorate(original)));
    }
}
