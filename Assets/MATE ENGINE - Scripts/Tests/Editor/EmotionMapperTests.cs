using Hermes;
using NUnit.Framework;

namespace Hermes.Tests
{
    /// <summary>
    /// Verifies migration plan §5 row A4: pure emotion-to-keyframe mapping loads shallow YAML rules
    /// and returns the neutral fallback for missing or empty emotions.
    /// </summary>
    [TestFixture]
    public class EmotionMapperTests
    {
        private string _testYaml;

        [SetUp]
        public void Setup()
        {
            _testYaml =
                "😊:\n" +
                "  - duration: 0.3\n" +
                "    targets:\n" +
                "      happy: 1.0\n" +
                "😢:\n" +
                "  - duration: 0.4\n" +
                "    targets:\n" +
                "      sad: 1.0\n";
        }

        [Test]
        public void Map_KnownHappyEmoji_ReturnsConfiguredKeyframes()
        {
            var mapper = new EmotionMapper(_testYaml);
            var keyframes = mapper.Map("😊");
            Assert.AreEqual(1, keyframes.Count);
            Assert.AreEqual(0.3f, keyframes[0].duration, 1e-5f);
            Assert.IsTrue(keyframes[0].targets.ContainsKey("happy"));
            Assert.AreEqual(1.0f, keyframes[0].targets["happy"], 1e-5f);
        }

        [Test]
        public void Map_KnownSadEmoji_ReturnsConfiguredKeyframes()
        {
            var mapper = new EmotionMapper(_testYaml);
            var keyframes = mapper.Map("😢");
            Assert.AreEqual(1, keyframes.Count);
            Assert.AreEqual(0.4f, keyframes[0].duration, 1e-5f);
            Assert.IsTrue(keyframes[0].targets.ContainsKey("sad"));
        }

        [Test]
        public void Map_UnknownEmoji_ReturnsDefaultNeutralKeyframe()
        {
            var mapper = new EmotionMapper(_testYaml);
            var keyframes = mapper.Map("🦄");
            Assert.AreEqual(1, keyframes.Count);
            Assert.AreEqual(0.3f, keyframes[0].duration, 1e-5f);
            Assert.IsTrue(keyframes[0].targets.ContainsKey("neutral"));
            Assert.AreEqual(1.0f, keyframes[0].targets["neutral"], 1e-5f);
        }

        [Test]
        public void Map_NullEmotion_ReturnsDefault()
        {
            var mapper = new EmotionMapper(_testYaml);
            var keyframes = mapper.Map(null);
            Assert.AreEqual(1, keyframes.Count);
            Assert.IsTrue(keyframes[0].targets.ContainsKey("neutral"));
        }

        [Test]
        public void Map_EmptyEmotion_ReturnsDefault()
        {
            var mapper = new EmotionMapper(_testYaml);
            var keyframes = mapper.Map("");
            Assert.AreEqual(1, keyframes.Count);
            Assert.IsTrue(keyframes[0].targets.ContainsKey("neutral"));
        }
    }
}
