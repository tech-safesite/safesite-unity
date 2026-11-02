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

    /// <summary>Creates a real scenario (folders, scene, definition, group) and removes it afterwards.</summary>
    public sealed class ScenarioCreatorTests
    {
        private const string Slug = "zz-authoring-test";
        private const string Title = "Zz Authoring Test";
        private const string Name = "ZzAuthoringTest";
        private const string Folder = ScenarioCreator.RootFolder + "/" + Name;
        private const string GroupName = "Scenario_Zz_Authoring_Test";
        private bool _rootExisted;

        [SetUp]
        public void SetUp() => _rootExisted = AssetDatabase.IsValidFolder(ScenarioCreator.RootFolder);

        [TearDown]
        public void TearDown()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var group = settings != null ? settings.FindGroup(GroupName) : null;
            if (group != null)
                settings.RemoveGroup(group);
            AssetDatabase.DeleteAsset(Folder);
            if (!_rootExisted)
                AssetDatabase.DeleteAsset(ScenarioCreator.RootFolder);
        }

        [Test]
        public void CreatesTheScenarioTeamsLayoutWithAnExcludedRemoteGroup()
        {
            var definition = ScenarioCreator.Create(Slug, Title, "Tests", "Created by a test.", 5, new[] { "English", " English ", "" });

            Assert.That(definition.Validate(), Is.Empty);
            Assert.That(definition.languages, Is.EqualTo(new[] { "English" }));
            Assert.That(AssetDatabase.GetAssetPath(definition), Is.EqualTo($"{Folder}/{Slug}.asset"));
            Assert.That(AssetDatabase.GetAssetPath(definition.mainScene), Is.EqualTo($"{Folder}/Scenes/{Name}.unity"));
            foreach (var sub in ScenarioCreator.Subfolders)
                Assert.That(AssetDatabase.IsValidFolder($"{Folder}/{sub}"), Is.True, sub);

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var group = ScenarioGroups.Find(definition);
            Assert.That(group, Is.Not.Null);
            Assert.That(group.Name, Is.EqualTo(GroupName));
            Assert.That(ScenarioGroups.HasMainScene(definition, group), Is.True);
            Assert.That(ScenarioGroups.MainSceneAddress(definition), Is.EqualTo(ScenarioAddresses.MainScene));

            var schema = group.GetSchema<BundledAssetGroupSchema>();
            Assert.That(schema.IncludeInBuild, Is.False, "new scenario groups stay out of the app's own builds");
            Assert.That(schema.BuildPath.GetName(settings), Is.EqualTo(AddressableAssetSettings.kRemoteBuildPath));
            Assert.That(schema.LoadPath.GetName(settings), Is.EqualTo(AddressableAssetSettings.kRemoteLoadPath));
        }

        [Test]
        public void ExportUsesTheContractAndRestoresALocalGroup()
        {
            var definition = ScenarioCreator.Create(Slug, Title, "Tests", "Created by a test.", 5, new[] { "English" });
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var group = ScenarioGroups.Find(definition);
            var schema = group.GetSchema<BundledAssetGroupSchema>();
            var entry = group.GetAssetEntry(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(definition.mainScene)));

            // Set up the way the scenario team tests in the app: own address, local paths.
            schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
            schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
            entry.address = "zz_authoring_test_scene";

            using (ScenarioGroups.ForExport(definition, group))
            {
                Assert.That(entry.address, Is.EqualTo(ScenarioAddresses.MainScene));
                Assert.That(schema.BuildPath.GetName(settings), Is.EqualTo(AddressableAssetSettings.kRemoteBuildPath));
                Assert.That(schema.LoadPath.GetName(settings), Is.EqualTo(AddressableAssetSettings.kRemoteLoadPath));
            }

            Assert.That(entry.address, Is.EqualTo("zz_authoring_test_scene"));
            Assert.That(schema.BuildPath.GetName(settings), Is.EqualTo(AddressableAssetSettings.kLocalBuildPath));
            Assert.That(schema.LoadPath.GetName(settings), Is.EqualTo(AddressableAssetSettings.kLocalLoadPath));

            // Repair keeps the team's address and paths.
            ScenarioGroups.Ensure(definition);
            Assert.That(entry.address, Is.EqualTo("zz_authoring_test_scene"));
            Assert.That(schema.LoadPath.GetName(settings), Is.EqualTo(AddressableAssetSettings.kLocalLoadPath));
        }

        [Test]
        public void ExportSplitsLargeTexturesIntoTheirOwnEntriesAndRemovesThemAfter()
        {
            var definition = ScenarioCreator.Create(Slug, Title, "Tests", "Created by a test.", 5, new[] { "English" });
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var group = ScenarioGroups.Find(definition);
            var schema = group.GetSchema<BundledAssetGroupSchema>();

            // A material in the group that uses a 1024² texture (over the split size) and a 64² one (under it).
            var large = WriteTexture($"{Folder}/Textures/large.png", 1024);
            var small = WriteTexture($"{Folder}/Textures/small.png", 64);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            material.mainTexture = large;
            material.SetTexture("_BumpMap", small);
            var materialPath = $"{Folder}/Materials/test.mat";
            AssetDatabase.CreateAsset(material, materialPath);
            settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(materialPath), group, false, false);
            var largeGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(large));
            var smallGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(small));
            var packing = schema.BundleMode;
            var entryCount = group.entries.Count;

            var lines = new List<string>();
            using (ScenarioGroups.ForExport(definition, group, true, lines.Add))
            {
                Assert.That(schema.BundleMode, Is.EqualTo(BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel));
                var entry = group.GetAssetEntry(largeGuid);
                Assert.That(entry, Is.Not.Null, "the large texture is packed apart from what uses it");
                Assert.That(entry.labels, Has.Some.StartsWith("safesite-split-"));
                Assert.That(group.GetAssetEntry(smallGuid), Is.Null, "small textures stay with what uses them");
            }

            Assert.That(lines, Has.Some.Contains("1 large asset(s) into 1 bundle"));
            Assert.That(settings.GetLabels(), Has.None.StartsWith("safesite-split-"));
            Assert.That(group.GetAssetEntry(largeGuid), Is.Null);
            Assert.That(group.entries.Count, Is.EqualTo(entryCount));
            Assert.That(schema.BundleMode, Is.EqualTo(packing));
        }

        private static Texture2D WriteTexture(string path, int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var random = new System.Random(size);
            var pixels = new Color32[size * size];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
            texture.SetPixels32(pixels);
            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        [Test]
        public void RegistersAnExistingSceneInPlace()
        {
            var first = ScenarioCreator.Create(Slug, Title, "Tests", "Created by a test.", 5, new[] { "English" });
            var scene = first.mainScene;
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(first));

            var definition = ScenarioCreator.Create("zz-authoring-again", Title, "Tests", "Created by a test.", 5, new[] { "English" }, scene);

            Assert.That(AssetDatabase.GetAssetPath(definition), Is.EqualTo($"{Folder}/zz-authoring-again.asset"));
            Assert.That(definition.mainScene, Is.EqualTo(scene));
            Assert.That(ScenarioGroups.Find(definition).Name, Is.EqualTo(GroupName));
        }

        [Test]
        public void RefusesAnExistingFolder()
        {
            ScenarioCreator.Create(Slug, Title, "Tests", "Created by a test.", 5, new[] { "English" });
            Assert.Throws<System.ArgumentException>(() =>
                ScenarioCreator.Create("zz-other", Title, "Tests", "Created by a test.", 5, new[] { "English" }));
        }

        [Test]
        public void RejectsBadDetailsWithoutCreatingAnything()
        {
            Assert.Throws<System.ArgumentException>(() =>
                ScenarioCreator.Create("Bad Slug", Title, "Tests", "x", 5, new[] { "English" }));
            Assert.That(AssetDatabase.IsValidFolder(Folder), Is.False);
        }

        [Test]
        public void NamesFollowTheScenarioTeam()
        {
            Assert.That(ScenarioCreator.FolderName("Working at Height"), Is.EqualTo("WorkingAtHeight"));
            Assert.That(ScenarioCreator.SlugFor("FallingObjects"), Is.EqualTo("falling-objects"));
            Assert.That(ScenarioCreator.TitleFor("FallingObjects"), Is.EqualTo("Falling Objects"));
        }
    }
}
