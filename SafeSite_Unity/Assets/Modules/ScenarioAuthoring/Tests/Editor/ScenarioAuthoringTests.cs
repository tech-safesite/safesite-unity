using System.Collections.Generic;
using System.Linq;
using Modules.SafeSiteApiService.External;
using Modules.ScenarioAuthoring.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace Modules.ScenarioAuthoring.Tests.Editor
{
    public sealed class ScenarioDefinitionTests
    {
        private ScenarioDefinition _definition;

        [SetUp]
        public void SetUp()
        {
            _definition = ScriptableObject.CreateInstance<ScenarioDefinition>();
            _definition.slug = "ladder-access-setup";
            _definition.title = "Ladder Access & Setup";
            _definition.category = "Working at Height";
            _definition.description = "Set up a ladder safely.";
            _definition.durationMinutes = 8;
            _definition.languages = new List<string> { "English" };
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_definition);

        private List<string> ErrorsIgnoringScene() => _definition.Validate().Where(e => !e.Contains("main scene")).ToList();

        [Test]
        public void ValidDetailsPass() => Assert.That(ErrorsIgnoringScene(), Is.Empty);

        [Test]
        public void MissingSceneFails() => Assert.That(_definition.Validate(), Has.Some.Contains("main scene"));

        [TestCase("Ladder")]
        [TestCase("ladder--access")]
        [TestCase("-ladder")]
        [TestCase("ladder access")]
        [TestCase("")]
        public void BadSlugFails(string slug)
        {
            _definition.slug = slug;
            Assert.That(ErrorsIgnoringScene(), Has.Some.Contains("Slug"));
        }

        [Test]
        public void TooLongTitleFails()
        {
            _definition.title = new string('a', 61);
            Assert.That(ErrorsIgnoringScene(), Has.Some.Contains("Title"));
        }

        [TestCase(0)]
        [TestCase(121)]
        public void DurationOutOfRangeFails(int minutes)
        {
            _definition.durationMinutes = minutes;
            Assert.That(ErrorsIgnoringScene(), Has.Some.Contains("Duration"));
        }

        [Test]
        public void BlankLanguagesFail()
        {
            _definition.languages = new List<string> { " " };
            Assert.That(ErrorsIgnoringScene(), Has.Some.Contains("language"));
        }

        [TestCase(null, "1.0.0")]
        [TestCase("", "1.0.0")]
        [TestCase("1.2.3", "1.2.4")]
        public void SuggestedVersionBumpsPatch(string last, string expected)
        {
            _definition.lastExportedVersion = last;
            Assert.That(_definition.SuggestedVersion(), Is.EqualTo(expected));
        }
    }

    public sealed class ScenarioPackageManifestTests
    {
        [Test]
        public void SerializesCamelCaseForTheDashboard()
        {
            var manifest = new ScenarioPackageManifest
            {
                Slug = "ladder-access-setup",
                SuggestedVersion = "1.4.0",
                Platforms = new List<string> { "meta" },
                MainScene = ScenarioAddresses.MainScene,
                Files = { new PackageFile { Platform = "meta", Path = "catalog_1.4.0.bin", SizeBytes = 10, Sha256 = "ab" } },
            };

            var json = JObject.Parse(manifest.ToJson());
            Assert.That((int)json["formatVersion"], Is.EqualTo(ScenarioPackageManifest.CurrentFormatVersion));
            Assert.That((string)json["suggestedVersion"], Is.EqualTo("1.4.0"));
            Assert.That((string)json["mainScene"], Is.EqualTo("scenario/main"));
            Assert.That((string)json["files"][0]["sha256"], Is.EqualTo("ab"));
            Assert.That((long)json["files"][0]["sizeBytes"], Is.EqualTo(10));
            Assert.That(json["builtWith"]["appVersion"], Is.Not.Null);
        }

        [Test]
        public void RoundTrips()
        {
            var manifest = new ScenarioPackageManifest { Slug = "a", Languages = { "English", "Romanian" } };
            var copy = ScenarioPackageManifest.FromJson(manifest.ToJson());
            Assert.That(copy.Slug, Is.EqualTo("a"));
            Assert.That(copy.Languages, Is.EqualTo(new[] { "English", "Romanian" }));
        }
    }

    /// <summary>Creates a real scenario (folder, scene, definition, group) and removes it afterwards.</summary>
    public sealed class ScenarioCreatorTests
    {
        private const string Slug = "zz-authoring-test";
        private const string Folder = ScenarioCreator.RootFolder + "/" + Slug;
        private bool _rootExisted;

        [SetUp]
        public void SetUp() => _rootExisted = AssetDatabase.IsValidFolder(ScenarioCreator.RootFolder);

        [TearDown]
        public void TearDown()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var group = settings != null ? settings.FindGroup("Scenario_" + Slug) : null;
            if (group != null)
                settings.RemoveGroup(group);
            AssetDatabase.DeleteAsset(Folder);
            if (!_rootExisted)
                AssetDatabase.DeleteAsset(ScenarioCreator.RootFolder);
        }

        [Test]
        public void CreatesAnExcludedRemoteGroupWithTheMainScene()
        {
            var definition = ScenarioCreator.Create(Slug, "Test", "Tests", "Created by a test.", 5, new[] { "English", " English ", "" });

            Assert.That(definition.Validate(), Is.Empty);
            Assert.That(definition.languages, Is.EqualTo(new[] { "English" }));
            Assert.That(AssetDatabase.GetAssetPath(definition.mainScene), Is.EqualTo($"{Folder}/{Slug}.unity"));

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var group = ScenarioGroups.Find(definition);
            Assert.That(group, Is.Not.Null);
            Assert.That(ScenarioGroups.HasMainScene(definition, group), Is.True);

            var schema = group.GetSchema<BundledAssetGroupSchema>();
            Assert.That(schema.IncludeInBuild, Is.False, "scenario groups stay out of the app's own builds");
            Assert.That(schema.BuildPath.GetName(settings), Is.EqualTo(AddressableAssetSettings.kRemoteBuildPath));
            Assert.That(schema.LoadPath.GetName(settings), Is.EqualTo(AddressableAssetSettings.kRemoteLoadPath));
        }

        [Test]
        public void RefusesAnExistingSlug()
        {
            ScenarioCreator.Create(Slug, "Test", "Tests", "Created by a test.", 5, new[] { "English" });
            Assert.Throws<System.ArgumentException>(() =>
                ScenarioCreator.Create(Slug, "Test", "Tests", "Created by a test.", 5, new[] { "English" }));
        }

        [Test]
        public void RejectsBadDetailsWithoutCreatingAnything()
        {
            Assert.Throws<System.ArgumentException>(() =>
                ScenarioCreator.Create("Bad Slug", "Test", "Tests", "x", 5, new[] { "English" }));
            Assert.That(AssetDatabase.IsValidFolder(ScenarioCreator.RootFolder + "/Bad Slug"), Is.False);
        }
    }
}
