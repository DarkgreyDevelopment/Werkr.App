using Werkr.Server.Services;

namespace Werkr.Tests.Server;

/// <summary>
/// Unit tests for the <see cref="ActionParameterRegistry"/> class defined in
/// the <c>Werkr.Server</c> project. Validates that the static registry of action
/// form descriptors contains the expected actions, fields, field types, default
/// values, and encoding options used by the server's action parameter system.
/// </summary>
[TestClass]
public class ActionParameterRegistryTests {
    /// <summary>
    /// Verifies that the <see cref="ActionParameterRegistry.All"/> collection
    /// contains exactly eleven registered <see cref="ActionFormDescriptor"/>
    /// entries representing all supported file and process actions.
    /// </summary>
    [TestMethod]
    public void All_Contains_Eleven_Actions( ) {
        Assert.HasCount( 11, ActionParameterRegistry.All );
    }

    /// <summary>
    /// Verifies that the <see cref="ActionParameterRegistry.Actions"/> dictionary
    /// uses a case-insensitive string comparer, so lookups for keys such as
    /// "copyfile", "COPYFILE", and "CopyFile" all succeed.
    /// </summary>
    [TestMethod]
    public void Actions_Dictionary_Is_Case_Insensitive( ) {
        Assert.IsTrue( ActionParameterRegistry.Actions.ContainsKey( "copyfile" ) );
        Assert.IsTrue( ActionParameterRegistry.Actions.ContainsKey( "COPYFILE" ) );
        Assert.IsTrue( ActionParameterRegistry.Actions.ContainsKey( "CopyFile" ) );
    }

    /// <summary>
    /// Verifies that the <see cref="ActionParameterRegistry.Actions"/> dictionary
    /// contains a specific expected action key.
    /// </summary>
    [TestMethod]
    [DataRow( "CopyFile" )]
    [DataRow( "MoveFile" )]
    [DataRow( "RenameFile" )]
    [DataRow( "DeleteFile" )]
    [DataRow( "CreateFile" )]
    [DataRow( "CreateDirectory" )]
    [DataRow( "TestExists" )]
    [DataRow( "ClearContent" )]
    [DataRow( "WriteContent" )]
    [DataRow( "StartProcess" )]
    [DataRow( "StopProcess" )]
    public void Actions_Contains_Expected_Key( string key ) {
        Assert.IsTrue(
            ActionParameterRegistry.Actions.ContainsKey( key ),
            $"Missing action key: {key}"
        );
    }

    /// <summary>
    /// Verifies that every <see cref="ActionFormDescriptor"/> in the registry has
    /// at least one <see cref="FieldDescriptor"/>, ensuring no action is registered
    /// without defining its required parameters.
    /// </summary>
    [TestMethod]
    public void Every_Descriptor_Has_At_Least_One_Field( ) {
        foreach (ActionFormDescriptor desc in ActionParameterRegistry.All) {
            Assert.IsNotEmpty(
                desc.Fields,
                $"Action '{desc.Key}' has no fields." );
        }
    }

    /// <summary>
    /// Verifies that every <see cref="ActionFormDescriptor"/> has a non-null,
    /// non-whitespace <see cref="DisplayName"/> and <see cref="Description"/>
    /// so that the UI can present meaningful labels.
    /// </summary>
    [TestMethod]
    public void Every_Descriptor_Has_NonEmpty_DisplayName_And_Description( ) {
        foreach (ActionFormDescriptor desc in ActionParameterRegistry.All) {
            Assert.IsFalse(
                string.IsNullOrWhiteSpace( desc.DisplayName ),
                $"Action '{desc.Key}' has empty DisplayName." );
            Assert.IsFalse(
                string.IsNullOrWhiteSpace( desc.Description ),
                $"Action '{desc.Key}' has empty Description." );
        }
    }

    /// <summary>
    /// Verifies that every <see cref="FieldDescriptor"/> whose
    /// <see cref="Type"/> is <see cref="FieldType.Select"/> has a non-null,
    /// non-empty <see cref="Options"/> list so that dropdowns in the UI
    /// are populated.
    /// </summary>
    [TestMethod]
    public void Select_Fields_Have_Options( ) {
        foreach (ActionFormDescriptor desc in ActionParameterRegistry.All) {
            foreach (FieldDescriptor field in desc.Fields) {
                if (field.Type == FieldType.Select) {
                    Assert.IsNotNull(
                        field.Options,
                        $"'{desc.Key}.{field.Name}' is Select but has no Options." );
                    Assert.IsNotEmpty(
                        field.Options,
                        $"'{desc.Key}.{field.Name}' is Select but Options is empty." );
                }
            }
        }
    }

    /// <summary>
    /// Verifies that the <see cref="ActionParameterRegistry.Encodings"/> list
    /// contains the common "utf-8" encoding value which is the default for
    /// most file operations.
    /// </summary>
    [TestMethod]
    public void Encodings_Contains_Utf8( ) {
        CollectionAssert.Contains( ActionParameterRegistry.Encodings, "utf-8" );
    }

    /// <summary>
    /// Verifies that the <see cref="ActionParameterRegistry.Encodings"/> list
    /// contains exactly nine supported character encoding values.
    /// </summary>
    [TestMethod]
    public void Encodings_Has_Nine_Values( ) {
        Assert.HasCount( 9, ActionParameterRegistry.Encodings );
    }

    /// <summary>
    /// Verifies that the "CopyFile" action's <see cref="ActionFormDescriptor"/>
    /// has exactly four fields named "Source", "Destination", "Overwrite",
    /// and "Recursive" in the expected order.
    /// </summary>
    [TestMethod]
    public void CopyFile_Has_Expected_Fields( ) {
        ActionFormDescriptor desc = ActionParameterRegistry.Actions["CopyFile"];
        Assert.HasCount( 4, desc.Fields );
        Assert.AreEqual( "Source", desc.Fields[0].Name );
        Assert.AreEqual( "Destination", desc.Fields[1].Name );
        Assert.AreEqual( "Overwrite", desc.Fields[2].Name );
        Assert.AreEqual( "Recursive", desc.Fields[3].Name );
    }

    /// <summary>
    /// Verifies that the "TestExists" action's "Type" field is a
    /// <see cref="FieldType.Select"/> with exactly three options: "Any",
    /// "File", and "Directory".
    /// </summary>
    [TestMethod]
    public void TestExists_Type_Is_Select_With_Three_Options( ) {
        ActionFormDescriptor desc = ActionParameterRegistry.Actions["TestExists"];
        FieldDescriptor typeField = desc.Fields[1];
        Assert.AreEqual( "Type", typeField.Name );
        Assert.AreEqual( FieldType.Select, typeField.Type );
        Assert.HasCount( 3, typeField.Options! );
        CollectionAssert.Contains( typeField.Options, "Any" );
        CollectionAssert.Contains( typeField.Options, "File" );
        CollectionAssert.Contains( typeField.Options, "Directory" );
    }

    /// <summary>
    /// Verifies that the "CreateFile" action's "Encoding" field has a default
    /// value of "utf-8", ensuring new files will use UTF-8 encoding unless
    /// explicitly overridden.
    /// </summary>
    [TestMethod]
    public void CreateFile_Encoding_Default_Is_Utf8( ) {
        ActionFormDescriptor desc = ActionParameterRegistry.Actions["CreateFile"];
        FieldDescriptor? enc = null;
        foreach (FieldDescriptor f in desc.Fields) {
            if (f.Name == "Encoding") { enc = f; break; }
        }
        Assert.IsNotNull( enc );
        Assert.AreEqual( "utf-8", enc.DefaultValue );
    }
}
