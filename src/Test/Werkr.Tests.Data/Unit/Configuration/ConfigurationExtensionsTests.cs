using System.Reflection;
using Microsoft.Extensions.Configuration;
using Werkr.Common.Extensions;

namespace Werkr.Tests.Data.Unit.Configuration;

/// <summary>
/// Tests for <see cref="ConfigurationBuilderExtensions"/>.
/// </summary>
[TestClass]
public sealed class ConfigurationExtensionsTests {
    /// <summary>
    /// Verifies that <see cref="ConfigurationBuilderExtensions.AddWerkrConfigPath"/>
    /// loads values from a JSON file specified by the WERKR_CONFIG_PATH environment variable.
    /// </summary>
    [TestMethod]
    public void AddWerkrConfigPath_WithValidJsonFile_LoadsValues( ) {
        // Arrange
        string tempFile = Path.GetTempFileName();
        try {
            File.WriteAllText(
                tempFile,
                """{"TestSection": {"Key1": "Value1"}}"""
            );
            Environment.SetEnvironmentVariable(
                "WERKR_CONFIG_PATH",
                tempFile
            );

            IConfigurationBuilder builder = new ConfigurationBuilder();

            // Act
            _ = builder.AddWerkrConfigPath( );
            IConfigurationRoot config = builder.Build();

            // Assert
            Assert.AreEqual(
                "Value1",
                config["TestSection:Key1"]
            );
        } finally {
            Environment.SetEnvironmentVariable(
                "WERKR_CONFIG_PATH",
                null
            );
            File.Delete( tempFile );
        }
    }

    /// <summary>
    /// Verifies that <see cref="ConfigurationBuilderExtensions.AddWerkrConfigPath"/>
    /// gracefully handles the case where WERKR_CONFIG_PATH is not set.
    /// </summary>
    [TestMethod]
    public void AddWerkrConfigPath_WithNoEnvVar_ReturnsEmptyConfig( ) {
        // Arrange
        Environment.SetEnvironmentVariable(
            "WERKR_CONFIG_PATH",
            null
        );
        IConfigurationBuilder builder = new ConfigurationBuilder();

        // Act
        _ = builder.AddWerkrConfigPath( );
        IConfigurationRoot config = builder.Build();

        // Assert — should not throw and should produce a valid (empty) config
        Assert.IsNotNull( config );
        Assert.IsNull( config["NonExistent:Key"] );
    }

    /// <summary>
    /// Verifies that <see cref="ConfigurationBuilderExtensions.AddWerkrConfigPath"/>
    /// gracefully handles a WERKR_CONFIG_PATH pointing to a nonexistent file
    /// (the file is added as optional).
    /// </summary>
    [TestMethod]
    public void AddWerkrConfigPath_WithMissingFile_DoesNotThrow( ) {
        // Arrange
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"werkr-test-missing-{Guid.NewGuid()}.json"
        );
        Environment.SetEnvironmentVariable(
            "WERKR_CONFIG_PATH",
            missingPath
        );
        IConfigurationBuilder builder = new ConfigurationBuilder();

        try {
            // Act
            _ = builder.AddWerkrConfigPath( );
            IConfigurationRoot config = builder.Build();

            // Assert — should not throw and should produce a valid (empty) config
            Assert.IsNotNull( config );
        } finally {
            Environment.SetEnvironmentVariable(
                "WERKR_CONFIG_PATH",
                null
            );
        }
    }

    /// <summary>
    /// Verifies that <see cref="ConfigurationBuilderExtensions.AddWerkrConfigPath"/>
    /// returns the builder for fluent chaining.
    /// </summary>
    [TestMethod]
    public void AddWerkrConfigPath_ReturnsSameBuilder_ForChaining( ) {
        // Arrange
        Environment.SetEnvironmentVariable(
            "WERKR_CONFIG_PATH",
            null
        );
        IConfigurationBuilder builder = new ConfigurationBuilder();

        // Act
        IConfigurationBuilder result = builder.AddWerkrConfigPath();

        // Assert
        Assert.AreSame(
            builder,
            result
        );
    }

    /// <summary>
    /// Verifies that the assembly <see cref="InformationalVersion"/> attribute is present on the
    /// entry assembly, validating the GitVersion integration produces a version string.
    /// </summary>
    [TestMethod]
    public void AssemblyVersion_InformationalVersion_IsPresent( ) {
        // Arrange — use the test assembly itself (it inherits Directory.Build.props versioning)
        Assembly assembly = typeof( ConfigurationExtensionsTests ).Assembly;

        // Act
        AssemblyInformationalVersionAttribute? attr = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        // Assert — the attribute should always be present (defaults to 1.0.0 without GitVersion)
        Assert.IsNotNull(
            attr,
            "AssemblyInformationalVersionAttribute should be present."
        );
        Assert.IsFalse(
            string.IsNullOrWhiteSpace( attr.InformationalVersion ),
            "InformationalVersion should not be empty."
        );
    }
}
