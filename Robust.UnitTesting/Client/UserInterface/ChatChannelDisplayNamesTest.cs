using System;
using System.Reflection;
using NUnit.Framework;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.Translation;
using Robust.Shared.IoC;

namespace Robust.UnitTesting.Client.UserInterface
{
    [TestFixture]
    public sealed class ChatChannelDisplayNamesTest : RobustUnitTest
    {
        public override UnitTestProject Project => UnitTestProject.Client;

        [OneTimeSetUp]
        public void Setup() => IoCManager.Resolve<IUserInterfaceManagerInternal>().InitializeTesting();

        [TestCase("> Local", "> 本地")]
        [TestCase("; Radio", "; 无线电")]
        [TestCase("Local (3)", "本地 (3)")]
        [TestCase("Radio (9+)", "无线电 (9+)")]
        [TestCase("Local", "本地")]
        [TestCase("Radio", "无线电")]
        [TestCase("Almayer", "阿尔迈耶")]
        [TestCase("Command", "指挥")]
        [TestCase("MedSci", "医疗科研")]
        [TestCase("Engi", "工程")]
        [TestCase("Intel", "情报")]
        [TestCase("Alpha", "Alpha队")]
        [TestCase("Bravo", "Bravo队")]
        [TestCase("Charlie", "Charlie队")]
        [TestCase("Delta", "Delta队")]
        [TestCase("HighCom", "最高指挥")]
        [TestCase("MP", "宪兵")]
        [TestCase("Req", "军需")]
        [TestCase("JTAC", "火力协调（JTAC）")]
        [TestCase("Echo", "Echo队")]
        [TestCase("Foxtrot", "Foxtrot队")]
        [TestCase("SOF", "特战")]
        [TestCase("Marine Colonial", "陆战队殖民地")]
        public void FixedNames(string source, string expected)
        {
            Assert.That(ChatChannelDisplayNames.TryTranslate(source, out var display), Is.True);
            Assert.That(display, Is.EqualTo(expected));
        }

        [TestCase("[Alpha] hello")]
        [TestCase("Local person")]
        [TestCase("Command says hello")]
        [TestCase("Unknown")]
        [TestCase("; Alpha")]
        [TestCase("Local (100)")]
        [TestCase("Intel (player)")]
        public void UnrecognizedLabelsPreserved(string source)
        {
            Assert.That(ChatChannelDisplayNames.TryTranslate(source, out var display), Is.False);
            Assert.That(display, Is.EqualTo(source));
        }

        [Test]
        public void LateParentingAndReparentingRefreshWithoutServiceRevision()
        {
            var channel = new Content.Client.UserInterface.Systems.Chat.Controls.ChannelSelectorButton();
            var label = channel.Label;
            channel.RemoveChild(label);
            label.Text = "Intel";
            Assert.That(Display(label), Is.EqualTo("Intel"));
            var intermediate = new BoxContainer();
            intermediate.AddChild(label);
            channel.AddChild(intermediate);
            Refresh(label);
            Assert.That(Display(label), Is.EqualTo("情报"));
            Assert.That(label.Text, Is.EqualTo("Intel"));
            Assert.That(label.TextMemory.ToString(), Is.EqualTo("Intel"));

            // An old cached/diagnostic display cannot overwrite the manual mapping.
            Refresh(label);
            Assert.That(Display(label), Is.EqualTo("情报"));
            channel.RemoveChild(intermediate);
            Refresh(label);
            Assert.That(ChatChannelDisplayNames.IsChannelControl(label), Is.False);
            Assert.That(Display(label), Is.EqualTo(TextCaptureDiagnostics.Session.Decorate("Intel")));
        }

        [Test]
        public void BodyLabelNeverAcquiresChannelScope()
        {
            var label = new Label { Text = "Command" };
            var ordinary = new BoxContainer();
            ordinary.AddChild(label);
            Assert.That(ChatChannelDisplayNames.IsChannelControl(label), Is.False);
        }

        [Test]
        public void OnlyTheChannelControlsOwnLabelIsEligible()
        {
            var channel = new Content.Client.UserInterface.Systems.Chat.Controls.ChannelSelectorButton();
            var unrelated = new Label { Text = "Alpha" };
            channel.AddChild(unrelated);
            Assert.That(ChatChannelDisplayNames.IsChannelControl(unrelated), Is.False);
            channel.Text = "Alpha";
            Assert.That(Display(channel.Label), Is.EqualTo("Alpha队"));

            var filter = new Content.Client.UserInterface.Systems.Chat.Controls.ChannelFilterCheckbox();
            filter.Text = "Radio (9+)";
            Assert.That(Display(filter.Label), Is.EqualTo("无线电 (9+)"));
            Assert.That(filter.Text, Is.EqualTo("Radio (9+)"));
        }

        private static void Refresh(Label label) => typeof(Label)
            .GetMethod("RefreshDisplayText", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(label, new object[] { false });
        private static string Display(Label label) => ((ReadOnlyMemory<char>) typeof(Label)
            .GetField("_displayTextMemory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(label)!).ToString();
    }
}

// Content is deliberately not referenced by the engine: model only its type identity.
namespace Content.Client.UserInterface.Systems.Chat.Controls
{
    public sealed class ChannelSelectorButton : Button { }
    public sealed class ChannelFilterCheckbox : CheckBox { }
}
