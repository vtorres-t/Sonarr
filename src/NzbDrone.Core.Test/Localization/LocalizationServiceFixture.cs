using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Localization
{
    [TestFixture]
    public class LocalizationServiceFixture : CoreTest<LocalizationService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigService>().Setup(m => m.UILanguage).Returns((int)Language.English);

            Mocker.GetMock<IAppFolderInfo>().Setup(m => m.StartUpFolder).Returns(TestContext.CurrentContext.TestDirectory);
        }

        [Test]
        public void should_get_string_in_dictionary_if_lang_exists_and_string_exists()
        {
            var localizedString = Subject.GetLocalizedString("UiLanguage");

            localizedString.Should().Be("UI Language");
        }

        [Test]
        public void should_get_string_in_spanish()
        {
            Mocker.GetMock<IConfigService>().Setup(m => m.UILanguage).Returns((int)Language.Spanish);

            var localizedString = Subject.GetLocalizedString("UiLanguage");

            localizedString.Should().Be("Idioma de interfaz");
        }

        [Test]
        public void should_get_string_in_default_dictionary_if_unknown_language_and_string_exists()
        {
            Mocker.GetMock<IConfigService>().Setup(m => m.UILanguage).Returns(0);
            var localizedString = Subject.GetLocalizedString("UiLanguage");

            localizedString.Should().Be("UI Language");
        }

        [Test]
        public void should_return_argument_if_string_doesnt_exists()
        {
            var localizedString = Subject.GetLocalizedString("badString");

            localizedString.Should().Be("badString");
        }

        [Test]
        public void should_return_argument_if_string_doesnt_exists_default_lang()
        {
            var localizedString = Subject.GetLocalizedString("badString");

            localizedString.Should().Be("badString");
        }

        [Test]
        public void should_throw_if_empty_string_passed()
        {
            Assert.Throws<ArgumentNullException>(() => Subject.GetLocalizedString(""));
        }

        [Test]
        public void should_throw_if_null_string_passed()
        {
            Assert.Throws<ArgumentNullException>(() => Subject.GetLocalizedString(null));
        }
    }
}
