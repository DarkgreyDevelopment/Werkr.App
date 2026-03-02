using Werkr.Server.Services;

namespace Werkr.Tests.Server;

[TestClass]
public class ActionParameterRegistryTests {
    [TestMethod]
    public void All_Contains_Eleven_Actions( ) {
        Assert.HasCount( 11, ActionParameterRegistry.All );
    }

    [TestMethod]
    public void Actions_Dictionary_Is_Case_Insensitive( ) {
        Assert.IsTrue( ActionParameterRegistry.Actions.ContainsKey( "copyfile" ) );
        Assert.IsTrue( ActionParameterRegistry.Actions.ContainsKey( "COPYFILE" ) );
        Assert.IsTrue( ActionParameterRegistry.Actions.ContainsKey( "CopyFile" ) );
    }

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
            $"Missing action key: {key}" );
    }

    [TestMethod]
    public void Every_Descriptor_Has_At_Least_One_Field( ) {
        foreach (ActionFormDescriptor desc in ActionParameterRegistry.All) {
            Assert.IsNotEmpty(
                desc.Fields,
                $"Action '{desc.Key}' has no fields." );
        }
    }

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

    [TestMethod]
    public void Encodings_Contains_Utf8( ) {
        CollectionAssert.Contains( ActionParameterRegistry.Encodings, "utf-8" );
    }

    [TestMethod]
    public void Encodings_Has_Nine_Values( ) {
        Assert.HasCount( 9, ActionParameterRegistry.Encodings );
    }

    [TestMethod]
    public void CopyFile_Has_Expected_Fields( ) {
        ActionFormDescriptor desc = ActionParameterRegistry.Actions["CopyFile"];
        Assert.HasCount( 4, desc.Fields );
        Assert.AreEqual( "Source", desc.Fields[0].Name );
        Assert.AreEqual( "Destination", desc.Fields[1].Name );
        Assert.AreEqual( "Overwrite", desc.Fields[2].Name );
        Assert.AreEqual( "Recursive", desc.Fields[3].Name );
    }

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
