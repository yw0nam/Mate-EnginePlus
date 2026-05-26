using OpenaiCompatibleAgent;
using NUnit.Framework;

namespace OpenaiCompatibleAgent.Tests
{
    /// <summary>
    /// Verifies migration plan §5 row A3: pure text preprocessing strips action/meta blocks,
    /// normalizes whitespace, and preserves configured emotion detection for Hermes TTS input.
    /// </summary>
    [TestFixture]
    public class PreprocessorTests
    {
        [Test]
        public void PlainAscii_NoEmotion()
        {
            var (clean, emotion) = Preprocessor.Process("hello");
            Assert.AreEqual("hello", clean);
            Assert.IsNull(emotion);
        }

        [Test]
        public void WithEmoji_DetectedAndKept()
        {
            var (clean, emotion) = Preprocessor.Process("こんにちは 😊");
            Assert.AreEqual("こんにちは 😊", clean);
            Assert.AreEqual("😊", emotion);
        }

        [Test]
        public void ActionBlockStripped()
        {
            var (clean, emotion) = Preprocessor.Process("*action* hello");
            Assert.AreEqual("hello", clean);
            Assert.IsNull(emotion);
        }

        [Test]
        public void MetaBlockStripped()
        {
            var (clean, emotion) = Preprocessor.Process("[meta] hello");
            Assert.AreEqual("hello", clean);
            Assert.IsNull(emotion);
        }

        [Test]
        public void MultipleSpacesCollapsed()
        {
            var (clean, emotion) = Preprocessor.Process("  multiple   spaces  ");
            Assert.AreEqual("multiple spaces", clean);
            Assert.IsNull(emotion);
        }

        [Test]
        public void EmptyInput_Empty()
        {
            var (clean, emotion) = Preprocessor.Process("");
            Assert.AreEqual("", clean);
            Assert.IsNull(emotion);
        }

        [Test]
        public void WhitespaceOnly_Empty()
        {
            var (clean, emotion) = Preprocessor.Process("   ");
            Assert.AreEqual("", clean);
            Assert.IsNull(emotion);
        }

        [Test]
        public void MixedActionAndEmoji()
        {
            var (clean, emotion) = Preprocessor.Process("*thinks* deeply 🤔");
            Assert.AreEqual("deeply 🤔", clean);
            Assert.AreEqual("🤔", emotion);
        }
    }
}
