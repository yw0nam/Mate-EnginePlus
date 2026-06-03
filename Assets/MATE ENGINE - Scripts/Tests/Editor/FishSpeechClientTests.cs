using NUnit.Framework;
using OpenaiCompatibleAgent;

namespace OpenaiCompatibleAgent.Tests
{
    [TestFixture]
    public class FishSpeechClientTests
    {
        [Test]
        public void BuildRequest_MapsFields()
        {
            var req = FishSpeechClient.BuildRequest("fishaudio/s2-pro", "こんにちは", "七海", "default", "wav", "");
            Assert.AreEqual("fishaudio/s2-pro", req.Model);
            Assert.AreEqual("こんにちは", req.Input);
            Assert.AreEqual("七海", req.Voice);
            Assert.AreEqual("wav", req.ResponseFormat);
            Assert.IsNull(req.Language);
        }

        [Test]
        public void BuildRequest_FallsBackToDefaultVoice_WhenReferenceIdMissing()
        {
            Assert.AreEqual("七海", FishSpeechClient.BuildRequest("m", "t", null, "七海", "wav", "").Voice);
            Assert.AreEqual("七海", FishSpeechClient.BuildRequest("m", "t", "", "七海", "wav", "").Voice);
        }

        [Test]
        public void BuildRequest_OmitsLanguage_WhenBlank()
        {
            Assert.IsNull(FishSpeechClient.BuildRequest("m", "t", "v", "d", "wav", "").Language);
        }

        [Test]
        public void BuildRequest_IncludesLanguage_WhenSet()
        {
            Assert.AreEqual("Japanese", FishSpeechClient.BuildRequest("m", "t", "v", "d", "wav", "Japanese").Language);
        }

        [Test]
        public void BuildRequest_DefaultsResponseFormatToWav_WhenBlank()
        {
            Assert.AreEqual("wav", FishSpeechClient.BuildRequest("m", "t", "v", "d", "", "").ResponseFormat);
        }

        [Test]
        public void BuildRequest_NullText_YieldsEmptyInput()
        {
            var req = FishSpeechClient.BuildRequest("m", null, "v", "d", "wav", "");
            Assert.AreEqual(string.Empty, req.Input);
        }
    }
}
