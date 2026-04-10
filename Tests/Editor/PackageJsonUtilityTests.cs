using System.Collections.Generic;
using NUnit.Framework;
using VladislavTsurikov.AnalyzeDependencies.Editor.Core.Utilities;

namespace VladislavTsurikov.AnalyzeDependencies.Tests.Editor
{
    public class PackageJsonUtilityTests
    {
        [Test]
        public void BuildPackageName_StripsPrefixAndLowercasesSegments()
        {
            string packageName = PackageJsonUtility.BuildPackageName("VladislavTsurikov.EntityDataAction.Shared");

            Assert.That(packageName, Is.EqualTo("com.vladislavtsurikov.entitydataaction.shared"));
        }

        [Test]
        public void BuildPackageMetadata_StripsLegacyTypoPrefix()
        {
            string packageName = PackageJsonUtility.BuildPackageName("VladislavTrurikov.GameObjectCollider");
            string displayName = PackageJsonUtility.BuildDisplayName("VladislavTrurikov.GameObjectCollider");
            string description = PackageJsonUtility.BuildDescription("VladislavTrurikov.GameObjectCollider");

            Assert.That(packageName, Is.EqualTo("com.vladislavtsurikov.gameobjectcollider"));
            Assert.That(displayName, Is.EqualTo("GameObjectCollider"));
            Assert.That(description, Is.EqualTo("UPM package for GameObjectCollider."));
        }

        [Test]
        public void UpsertDependencies_PreservesMetadataAndSortsDependencies()
        {
            const string json = "{\n" +
                                "  \"name\": \"com.vladislavtsurikov.test\",\n" +
                                "  \"version\": \"1.2.3\",\n" +
                                "  \"author\": {\n" +
                                "    \"name\": \"Vlad\"\n" +
                                "  },\n" +
                                "  \"dependencies\": {\n" +
                                "    \"com.legacy.dep\": \"0.1.0\"\n" +
                                "  }\n" +
                                "}";

            var dependencies = new Dictionary<string, string>
            {
                ["com.unity.addressables"] = "2.8.0",
                ["com.vladislavtsurikov.core"] = "1.0.0"
            };

            string updatedJson = PackageJsonUtility.UpsertDependencies(json, dependencies);

            Assert.That(updatedJson, Does.Contain("\"author\": {"));
            Assert.That(updatedJson, Does.Contain("\"name\": \"Vlad\""));
            Assert.That(updatedJson, Does.Contain("\"com.unity.addressables\": \"2.8.0\""));
            Assert.That(updatedJson, Does.Contain("\"com.vladislavtsurikov.core\": \"1.0.0\""));
            Assert.That(updatedJson.IndexOf("com.unity.addressables"), Is.LessThan(updatedJson.IndexOf("com.vladislavtsurikov.core")));
            Assert.That(updatedJson, Does.Not.Contain("com.legacy.dep"));
        }
    }
}
