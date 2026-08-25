using NUnit.Framework;
using Rehear.Evc.Editor;

namespace Rehear.Evc.Tests
{
    public sealed class EvcSceneValidationTests
    {
        [Test]
        public void PresentationScene_HasSixUniqueAgentsAndNoMissingScripts()
        {
            Assert.That(EvcProjectValidator.ValidatePresentationScene(), Is.Empty);
        }
    }
}
