using System.Collections;
using System.Reflection;
using System.Text;
using System.Xml;
using HSPE.AMModules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UnityEngine;

// Compile the actual production members, not a reimplementation of the feature.
// UI, game lifecycle, Harmony dispatch, and Unity's physics engine remain outside
// this isolated test boundary and require a game integration check.
internal static class Program
{
    private static Type editorType;
    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static int Main(string[] args)
    {
        try
        {
            string repo = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRepository();
            editorType = CompileProductionMembers(repo);
            var tests = new (string Name, Action Test)[]
            {
                ("Animated hips turn heading while the Studio object stays fixed", AnimatedHeading),
                ("World mode remains fixed when either frame rotates", WorldMode),
                ("Direction checkboxes switch mutually exclusively and can both turn off", DirectionCheckboxes),
                ("Enabling heading mode preserves the current world vector", EnablePreservesWorld),
                ("Heading and bone modes use distinct reference frames", DistinctFrames),
                ("Heading ignores animated pitch and roll", HeadingIgnoresPitchRoll),
                ("Full body mode still follows all three axes", FullBodyRotation),
                ("Heading remains finite across wraparound and vertical poses", HeadingEdgeCases),
                ("Initial fully inverted body uses the Studio root heading", InitialInversionFallback),
                ("Reset clears the previous inverted-pose fallback", ResetClearsHeading),
                ("The native pelvis reference is independent of names and updates immediately", NativePelvisReference),
                ("Item motion-control names do not exclude an explicit native pelvis", NativePelvisUnderNamedAncestor),
                ("Both heading directions share one reference read per physics update", SharedHeadingRead),
                ("PMX body aliases resolve from the animated item hierarchy", PmxAliases),
                ("PMX resolver prefers lower-body bones over upper-body controls", PmxBodyPriority),
                ("PMX resolver excludes nested Studio objects", PmxNestedObject),
                ("PMX resolver excludes VMD driver skeletons", PmxDriverSkeleton),
                ("PMX resolver excludes simulated bones and their descendants", PmxSimulatedBones),
                ("Body names require exact aliases and valid PMX prefixes", ExactBodyNames),
                ("Reset reselects the current imported skeleton", ItemResetReselectsSkeleton),
                ("Reset clears the imported skeleton's inverted-pose fallback", ItemResetClearsHeading),
                ("Unrecognized items use root heading without following head or limb bones", ItemFallback),
                ("Changing mode after animation preserves world direction", SwitchPreservesWorld),
                ("Selecting the active mode again preserves authored coordinates", RepeatedMode),
                ("Gravity and force have independent modes", IndependentModes),
                ("Authored local vectors remain stable over 1,441 updates", NoDrift),
                ("Scale and simulated root rotation do not affect vectors", ScaleAndSimulatedRoot),
                ("Missing root or attachment falls back to component rotation", FallbackFrame),
                ("Vector setters interpret values in the selected space", AuthoredSetters),
                ("Zero vectors remain finite through mode changes", ZeroVectors),
                ("XML saves local vectors and a fresh world snapshot", SaveLocalAndWorld),
                ("XML roundtrip applies local vectors at the new pose", RoundtripAtDifferentPose),
                ("Legacy XML retains fixed world directions", LegacyXml),
                ("Incomplete local XML falls back to the world snapshot", IncompleteLocalXml),
                ("Unrecognized XML spaces fall back to world mode", UnknownSpaceXml),
                ("Reset gravity leaves relative force active", ResetOne),
                ("Reset all restores originals and unregisters updates", ResetEverything),
                ("Data copies preserve independent spaces and authored values", DataCopy),
                ("LoadFrom uses the destination reference frame", LoadFromUsesDestination),
                ("Physics rebuild uses original gravity then restores authored gravity", ParticleRebuild),
                ("Destroyed owner is removed from runtime registry", OwnerCleanup)
            };
            int failures = 0;
            foreach (var (name, test) in tests)
            {
                try
                {
                    test();
                    Console.WriteLine($"PASS {name}");
                }
                catch (Exception error)
                {
                    failures++;
                    Console.Error.WriteLine($"FAIL {name}\n{Unwrap(error)}");
                }
            }
            Console.WriteLine($"{tests.Length - failures}/{tests.Length} regression cases passed.");
            return failures == 0 ? 0 : 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(Unwrap(error));
            return 1;
        }
    }

    private static string FindRepository()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "PoseEditor.Core", "AMModules", "DynamicBonesEditor.cs")))
                return dir.FullName;
        throw new InvalidOperationException("Pass the repository root as the first argument.");
    }

    private static Type CompileProductionMembers(string repo)
    {
        string path = Path.Combine(repo, "PoseEditor.Core", "AMModules", "DynamicBonesEditor.cs");
        var parseOptions = new CSharpParseOptions(preprocessorSymbols: ["KOIKATSU"]);
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions).GetCompilationUnitRoot();
        var production = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(n => n.Identifier.Text == "DynamicBonesEditor");
        string[] required =
        [
            "DirectionSpace", "DynamicBoneData", "_dynamicBones", "_dirtyDynamicBones", "_relativeDirectionOwners",
            "_dynamicBonesScroll", "_headlessDirtyDynamicBones", "_toUpdate", "_target", "_bodyHeading",
            "SetDynamicBoneGravity", "SetDynamicBoneForce", "GetReferenceRotation",
            "GetGravitySpace", "GetForceSpace", "IsGravityRelative", "IsForceRelative", "DirectionSpaceEditor", "ReadDirectionSpace",
            "GetDynamicBoneGravity", "GetDynamicBoneForce", "SetGravitySpace", "SetForceSpace",
            "UpdateRelativeDirectionRegistration", "ApplyRelativeDirections", "UnregisterRelativeDirections", "ResetGravity", "ResetForce",
            "WriteRelativeDirection", "TryReadRelativeDirection",
            "LoadFrom", "SaveXml", "CalculateRoot", "LoadSingleBone", "ResetAll",
            "SetDynamicBoneNotDirty", "SetDynamicBoneDirty", "NotifyDynamicBoneForUpdate", "UpdateDynamicBone"
        ];
        var members = production.Members.ToDictionary(MemberName);
        var selected = required.Select(name => members.TryGetValue(name, out var member)
            ? new RemoveInactiveDirectives().Visit(member).NormalizeWhitespace().ToFullString()
            : throw new InvalidOperationException($"Production member '{name}' was renamed or removed; update the explicit test boundary."));
        string projected = "using System; using System.Collections.Generic; using System.Linq; using System.Xml; using UnityEngine; using ToolBox.Extensions;\n" +
            "namespace HSPE.AMModules { public class DynamicBonesEditor : AdvancedModeModule {\n" +
            string.Join("\n", selected) + "\n} }";
        var trees = new[]
        {
            CSharpSyntaxTree.ParseText(projected, parseOptions, path: "ProductionProjection.cs"),
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(repo, "PoseEditor.Core", "AMModules", "BodyHeadingReference.cs")), parseOptions, path: "BodyHeadingReference.cs"),
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(repo, "PoseEditor.Core", "EditableValue.cs")), parseOptions, path: "EditableValue.cs")
        };
        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator);
        var references = trustedAssemblies.Append(typeof(Program).Assembly.Location).Distinct()
            .Select(name => MetadataReference.CreateFromFile(name));
        var compilation = CSharpCompilation.Create("ProductionRelativeForces", trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        if (!result.Success)
            throw new InvalidOperationException("Could not compile production methods:\n" + string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return Assembly.Load(output.ToArray()).GetType("HSPE.AMModules.DynamicBonesEditor", throwOnError: true);
    }

    private static string MemberName(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax method => method.Identifier.Text,
        FieldDeclarationSyntax field => field.Declaration.Variables.Single().Identifier.Text,
        BaseTypeDeclarationSyntax type => type.Identifier.Text,
        PropertyDeclarationSyntax property => property.Identifier.Text,
        ConstructorDeclarationSyntax constructor => ".ctor" + constructor.ParameterList.Parameters.Count,
        _ => member.Kind().ToString()
    };

    private sealed class RemoveInactiveDirectives : CSharpSyntaxRewriter
    {
        public override SyntaxTrivia VisitTrivia(SyntaxTrivia trivia) =>
            trivia.IsDirective || trivia.IsKind(SyntaxKind.DisabledTextTrivia) ? default : base.VisitTrivia(trivia);
    }

    private sealed class Fixture
    {
        public readonly object Editor = Activator.CreateInstance(editorType);
        public readonly DynamicBone Bone = new();
        public readonly Transform Attachment = new() { name = "attachment" };
        public readonly Transform Root = new() { name = "root" };
        public readonly GenericOCITarget Target = new();
        public readonly object BodyHeading;
        public Transform Hips => Target.ociChar.finalIK.references.pelvis;
        public Transform Model => ((AdvancedModeModule)Editor)._parent.transform;
        public HSPE.PoseController Controller => ((AdvancedModeModule)Editor)._parent;

        public Fixture()
        {
            editorType.GetField("_target", Instance).SetValue(Editor, Target);
            Hips.parent = Model;
            Attachment.parent = Model;
            Root.parent = Attachment;
            Bone.m_Root = Root;
            Bone.transform.name = "component";
            Bone.transform.parent = Model;
            Bone.m_Gravity = new Vector3(0, -2, 0);
            Bone.m_Force = new Vector3(1, 0, 0);
            ((IList)GetField(Editor, "_dynamicBones")).Add(Bone);
            var headingType = editorType.Assembly.GetType("HSPE.AMModules.BodyHeadingReference", throwOnError: true);
            BodyHeading = Activator.CreateInstance(headingType, Instance, null, [Controller, Target, GetField(Editor, "_dynamicBones")], null);
            editorType.GetField("_bodyHeading", Instance).SetValue(Editor, BodyHeading);
        }

        public void UseItem()
        {
            Hips.parent = null;
            Target.type = GenericOCITarget.Type.Item;
            Target.ociChar = null;
        }

        public object Call(string method, params object[] args) =>
            editorType.GetMethod(method, Instance).Invoke(Editor, args);
        public void Space(string vector, string space) => Call("Set" + vector + "Space", Bone,
            Enum.Parse(editorType.GetNestedType("DirectionSpace", BindingFlags.NonPublic | BindingFlags.Public), space));
        public string Space(string vector) => Call("Get" + vector + "Space", Bone).ToString();
        public Vector3 Authored(string vector) => (Vector3)Call("GetDynamicBone" + vector, Bone);
        public void Set(string vector, Vector3 value) => Call("SetDynamicBone" + vector, Bone, value);
        public void Apply() => editorType.GetMethod("ApplyRelativeDirections", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static).Invoke(null, [Bone]);
        public void ResetHeading() => BodyHeading.GetType().GetMethod("Reset", Instance).Invoke(BodyHeading, null);
        public bool UsesObjectTransform => (bool)BodyHeading.GetType().GetProperty("UsesObjectTransform", Instance).GetValue(BodyHeading);
        public void Load(string xml)
        {
            var document = new XmlDocument();
            document.LoadXml(xml);
            Equal(true, Call("LoadSingleBone", Bone, document.DocumentElement), "loaded XML");
        }
        public XmlElement Save()
        {
            var buffer = new StringBuilder();
            using (var writer = new XmlTextWriter(new StringWriter(buffer)))
            {
                writer.WriteStartElement("scene");
                Equal(1, Call("SaveXml", writer), "saved bone count");
                writer.WriteEndElement();
            }
            var document = new XmlDocument();
            document.LoadXml(buffer.ToString());
            return (XmlElement)document.SelectSingleNode("/scene/dynamicBones/dynamicBone");
        }
    }

    private static object GetField(object owner, string field) => editorType.GetField(field, Instance).GetValue(owner);
    private static Exception Unwrap(Exception error) => error is TargetInvocationException { InnerException: not null } invocation ? Unwrap(invocation.InnerException) : error;
    private static void Equal(object expected, object actual, string label)
    {
        if (!Equals(expected, actual)) throw new Exception($"{label}: expected {expected}, got {actual}");
    }
    private static void Near(float expected, float actual, string label, float tolerance = 0.00002f)
    {
        if (!float.IsFinite(actual) || MathF.Abs(expected - actual) > tolerance)
            throw new Exception($"{label}: expected {expected:G9}, got {actual:G9}");
    }
    private static void Near(Vector3 expected, Vector3 actual, string label)
    {
        Near(expected.x, actual.x, label + ".x");
        Near(expected.y, actual.y, label + ".y");
        Near(expected.z, actual.z, label + ".z");
    }
    private static Vector3 ReadVector(XmlElement xml, string prefix) => new(
        XmlConvert.ToSingle(xml.GetAttribute(prefix + "X")), XmlConvert.ToSingle(xml.GetAttribute(prefix + "Y")), XmlConvert.ToSingle(xml.GetAttribute(prefix + "Z")));

    private static void WorldMode()
    {
        var f = new Fixture();
        f.Set("Gravity", new Vector3(2, 3, 4));
        f.Set("Force", new Vector3(5, 6, 7));
        f.Model.rotation = Quaternion.Roll(90);
        f.Attachment.rotation = Quaternion.Yaw(90);
        f.Apply();
        Equal("World", f.Space("Gravity"), "default gravity space");
        Equal("World", f.Space("Force"), "default force space");
        Near(new Vector3(2, 3, 4), f.Bone.m_Gravity, "world gravity");
        Near(new Vector3(5, 6, 7), f.Bone.m_Force, "world force");
    }

    private static void AnimatedHeading()
    {
        var f = new Fixture();
        f.Space("Force", "Heading");
        f.Hips.rotation = Quaternion.Yaw(90);
        f.Apply();
        Near(new Vector3(0, 0, 1), f.Model.rotation * new Vector3(0, 0, 1), "Studio object stays fixed");
        Near(new Vector3(0, 0, -1), f.Bone.m_Force, "force follows animated hips");
    }

    private static void DirectionCheckboxes()
    {
        var spaceType = editorType.GetNestedType("DirectionSpace", BindingFlags.NonPublic | BindingFlags.Public);
        var method = editorType.GetMethod("DirectionSpaceEditor", BindingFlags.NonPublic | BindingFlags.Static);
        foreach (var (from, click, expected) in new[]
        {
            ("World", -1, "World"), ("World", 0, "Heading"), ("Heading", -1, "Heading"),
            ("Heading", 1, "Bone"), ("Bone", -1, "Bone"), ("Bone", 1, "World"),
            ("World", 1, "Bone"), ("Bone", 0, "Heading"), ("Heading", 0, "World")
        })
        {
            GUILayout.CallIndex = 0;
            GUILayout.ClickIndex = click;
            var result = method.Invoke(null, [Enum.Parse(spaceType, from)]);
            Equal(expected, result.ToString(), $"checkbox {from}, click {click}");
            Equal(2, GUILayout.CallIndex, "both checkboxes rendered");
        }
    }

    private static void EnablePreservesWorld()
    {
        var f = new Fixture();
        f.Bone.m_Gravity = new Vector3(2, -2, 0);
        f.Hips.rotation = Quaternion.Yaw(90);
        f.Space("Gravity", "Heading");
        Near(new Vector3(2, -2, 0), f.Bone.m_Gravity, "enable keeps world");
        Near(new Vector3(0, -2, 2), f.Authored("Gravity"), "local conversion");
        f.Apply();
        Near(new Vector3(2, -2, 0), f.Bone.m_Gravity, "first physics tick");
        f.Hips.rotation = Quaternion.Yaw(180);
        f.Apply();
        Near(new Vector3(0, -2, -2), f.Bone.m_Gravity, "follow heading");
        Near(MathF.Sqrt(8), f.Bone.m_Gravity.magnitude, "magnitude");
    }

    private static void DistinctFrames()
    {
        var f = new Fixture();
        f.Space("Gravity", "Heading");
        f.Set("Gravity", new Vector3(1, -2, 0));
        f.Space("Force", "Bone");
        f.Hips.rotation = Quaternion.Yaw(90);
        f.Attachment.rotation = Quaternion.Yaw(90);
        f.Apply();
        Near(new Vector3(0, -2, -1), f.Bone.m_Gravity, "heading gravity");
        Near(new Vector3(0, 0, -1), f.Bone.m_Force, "bone force");
        f.Attachment.rotation = Quaternion.Roll(90);
        f.Apply();
        Near(new Vector3(0, -2, -1), f.Bone.m_Gravity, "bone motion leaves heading gravity");
        Near(new Vector3(0, 1, 0), f.Bone.m_Force, "bone force follows pose");
    }

    private static void SwitchPreservesWorld()
    {
        foreach (string vector in new[] { "Gravity", "Force" })
        {
            var f = new Fixture();
            f.Space(vector, "Heading");
            f.Set(vector, new Vector3(1, 2, 3));
            f.Hips.rotation = Quaternion.Yaw(90); // Do not apply: switching must catch up.
            f.Attachment.rotation = Quaternion.Roll(90);
            var world = new Vector3(3, 2, -1);
            f.Space(vector, "Bone");
            f.Apply();
            Near(world, vector == "Gravity" ? f.Bone.m_Gravity : f.Bone.m_Force, "heading to bone");
            Near(Quaternion.Inverse(f.Attachment.rotation) * world, f.Authored(vector), "new bone coordinates");
            f.Attachment.rotation = Quaternion.Roll(180);
            var finalWorld = f.Attachment.rotation * f.Authored(vector);
            f.Space(vector, "World");
            Near(finalWorld, f.Authored(vector), "disable catches latest pose");
            f.Model.rotation = Quaternion.identity;
            f.Attachment.rotation = Quaternion.identity;
            f.Apply();
            Near(finalWorld, f.Authored(vector), "world direction stays fixed");
        }
    }

    private static void HeadingIgnoresPitchRoll()
    {
        var f = new Fixture();
        f.Space("Gravity", "Heading");
        f.Space("Force", "Heading");
        var gravity = new Vector3(1, -2, 3);
        var force = new Vector3(4, 5, 6);
        f.Set("Gravity", gravity);
        f.Set("Force", force);
        foreach (var axis in new[] { Vector3.right, Vector3.forward, new Vector3(1, 0, 1), new Vector3(-2, 0, 1) })
        foreach (float tilt in new[] { -179f, -135f, -80f, -35f, 0f, 50f, 80f, 135f, 179f })
        {
            f.Hips.rotation = Quaternion.Yaw(42) * Quaternion.AngleAxis(tilt, axis);
            f.Apply();
            Near(Quaternion.Yaw(42) * gravity, f.Bone.m_Gravity, "gravity ignores lean");
            Near(Quaternion.Yaw(42) * force, f.Bone.m_Force, "force ignores lean");
            Near(-2, f.Bone.m_Gravity.y, "vertical gravity stays vertical");
            Near(5, f.Bone.m_Force.y, "vertical force stays vertical");
        }
    }

    private static void FullBodyRotation()
    {
        var f = new Fixture();
        var authored = new Vector3(1, 2, 3);
        f.Space("Force", "Bone");
        f.Set("Force", authored);
        foreach (var rotation in new[] { Quaternion.Pitch(90), Quaternion.Roll(90), Quaternion.Yaw(90), Quaternion.Euler(43, 57, 31) })
        {
            f.Attachment.rotation = rotation;
            f.Apply();
            Near(rotation * authored, f.Bone.m_Force, "full parent orientation");
            Near(authored.magnitude, f.Bone.m_Force.magnitude, "full mode magnitude");
        }
    }

    private static void HeadingEdgeCases()
    {
        var f = new Fixture();
        f.Space("Force", "Heading");
        var authored = new Vector3(1, 2, 3);
        f.Set("Force", authored);
        foreach (float yaw in new[] { -361f, -360f, -180.001f, -180f, -179.999f, -0.001f, 0f, 0.001f, 179.999f, 180f, 180.001f, 360f, 361f })
        {
            f.Hips.rotation = Quaternion.Yaw(yaw);
            f.Apply();
            Near(Quaternion.Yaw(yaw) * authored, f.Bone.m_Force, "wrapped heading");
        }
        foreach (float pitch in new[] { -90.01f, -90f, -89.99f, 89.99f, 90f, 90.01f })
        {
            f.Hips.rotation = Quaternion.Euler(pitch, 37, 0);
            for (int i = 0; i < 20; i++) f.Apply();
            Near(Quaternion.Yaw(37) * authored, f.Bone.m_Force, "near vertical heading");
            Near(authored.magnitude, f.Bone.m_Force.magnitude, "near vertical magnitude");
        }
        foreach (float tilt in new[] { 170f, 179f, 179.99f, 180f, 180.01f, 181f, 190f })
        {
            f.Hips.rotation = Quaternion.Yaw(37) * Quaternion.Pitch(tilt);
            f.Apply();
            Near(Quaternion.Yaw(37) * authored, f.Bone.m_Force, "inversion retains heading");
        }
    }

    private static void InitialInversionFallback()
    {
        foreach (bool rootAlsoInverted in new[] { false, true })
        {
            var f = new Fixture();
            f.Model.rotation = rootAlsoInverted ? Quaternion.Pitch(180) : Quaternion.Yaw(63) * Quaternion.Roll(22);
            f.Hips.rotation = Quaternion.Pitch(180);
            f.Space("Force", "Heading");
            f.Set("Force", Vector3.right);
            f.Apply();
            var expected = rootAlsoInverted ? Vector3.right : Quaternion.Yaw(63) * Vector3.right;
            Near(expected, f.Bone.m_Force, "initial inversion fallback");
            for (int i = 0; i < 20; i++) f.Apply();
            Near(expected, f.Bone.m_Force, "initial inversion stays stable");
        }
    }

    private static void ItemFallback()
    {
        var f = new Fixture();
        f.UseItem();
        foreach (string name in new[] { "head", "頭", "left arm", "右足", "hips_helper", "PMX_bad_下半身" })
            _ = new Transform { name = name, parent = f.Model, rotation = Quaternion.Yaw(120) };
        f.Space("Force", "Heading");
        f.Model.rotation = Quaternion.Yaw(90) * Quaternion.AngleAxis(60, new Vector3(1, 0, 1));
        f.Apply();
        Near(new Vector3(0, 0, -1), f.Bone.m_Force, "unrecognized item uses horizontal root rotation");
    }

    private static void ResetClearsHeading()
    {
        var f = new Fixture();
        f.Space("Force", "Heading");
        f.Hips.rotation = Quaternion.Yaw(80);
        f.Apply();
        Near(Quaternion.Yaw(80) * Vector3.right, f.Bone.m_Force, "previous skeleton heading");
        f.Hips.rotation = Quaternion.Pitch(180);
        f.Model.rotation = Quaternion.Yaw(25);
        f.ResetHeading();
        f.Apply();
        Near(Quaternion.Yaw(25) * Vector3.right, f.Bone.m_Force, "new inverted skeleton uses fresh object heading");
        f.Model.rotation = Quaternion.Yaw(35);
        f.Apply();
        Near(Quaternion.Yaw(25) * Vector3.right, f.Bone.m_Force, "new fallback remains stable while inverted");
    }

    private static void NativePelvisReference()
    {
        var f = new Fixture();
        var namedCandidate = new Transform { name = "hips", parent = f.Model };
        f.Hips.name = "native-animation-control";
        f.Space("Force", "Heading");
        f.Hips.rotation = Quaternion.Yaw(90);
        namedCandidate.rotation = Quaternion.Yaw(180);
        f.Apply();
        Near(new Vector3(0, 0, -1), f.Bone.m_Force, "authoritative FinalIK pelvis");
        Equal(false, f.UsesObjectTransform, "native pelvis is a body reference");
        var replacement = new Transform { name = "replacement-control", parent = f.Model, rotation = Quaternion.Yaw(180) };
        f.Hips.rotation = Quaternion.identity;
        f.Target.ociChar.finalIK.references.pelvis = replacement;
        f.Apply();
        Near(new Vector3(-1, 0, 0), f.Bone.m_Force, "replacement reference applies immediately");
        replacement.parent = null;
        f.Model.rotation = Quaternion.Yaw(45);
        f.Apply();
        Near(Quaternion.Yaw(45) * Vector3.right, f.Bone.m_Force, "detached pelvis falls back to object heading");
        Equal(true, f.UsesObjectTransform, "detached pelvis is not a valid body reference");
    }

    private static void SharedHeadingRead()
    {
        foreach (bool item in new[] { false, true })
        {
            var f = new Fixture();
            Transform body = f.Hips;
            if (item)
            {
                f.UseItem();
                body = new Transform { name = "hips", parent = f.Model };
            }
            f.Space("Gravity", "Heading");
            f.Space("Force", "Heading");
            f.Set("Gravity", new Vector3(1, -2, 0));
            f.Set("Force", new Vector3(3, 4, 5));
            body.rotation = Quaternion.Yaw(90);
            body.RotationReadCount = 0;
            f.Controller.HierarchyScanCount = 0;
            f.Apply();
            Equal(1, body.RotationReadCount, "one heading read for both vectors");
            Equal(0, f.Controller.HierarchyScanCount, "cached reference avoids hierarchy scanning");
            Near(new Vector3(0, -2, -1), f.Bone.m_Gravity, "shared heading gravity");
            Near(new Vector3(5, 4, -3), f.Bone.m_Force, "shared heading force");
        }
    }

    private static void NativePelvisUnderNamedAncestor()
    {
        var f = new Fixture();
        var ancestor = new Transform { name = "VmdDummyBone", parent = f.Model };
        f.Hips.parent = ancestor;
        f.Space("Force", "Heading");
        f.Hips.rotation = Quaternion.Yaw(90);
        f.Apply();
        Near(new Vector3(0, 0, -1), f.Bone.m_Force, "explicit native pelvis ignores item-only name filters");
        Equal(false, f.UsesObjectTransform, "native pelvis remains the selected reference");
        Equal(0, f.Controller.HierarchyScanCount, "explicit native reference needs no item name scan");
    }

    private static void PmxAliases()
    {
        foreach (string name in new[] { "PMX_0003_下半身", "下半身", "PMX_12_腰", "腰", "hips", "Pelvis", "cf_j_hips", "lower body" })
        {
            var f = new Fixture();
            f.UseItem();
            var modelContainer = new Transform { name = "imported model", parent = f.Model };
            var body = new Transform { name = name, parent = modelContainer };
            f.Space("Force", "Heading");
            body.rotation = Quaternion.Yaw(90);
            f.Apply();
            Near(new Vector3(0, 0, -1), f.Bone.m_Force, "animated PMX alias " + name);
            body.rotation = Quaternion.Yaw(180) * Quaternion.AngleAxis(60, new Vector3(1, 0, 1));
            f.Apply();
            Near(new Vector3(-1, 0, 0), f.Bone.m_Force, "cached PMX reference stays animated");
            Near(Vector3.forward, f.Model.rotation * Vector3.forward, "Studio item stays fixed");
        }
    }

    private static void PmxBodyPriority()
    {
        var f = new Fixture();
        f.UseItem();
        _ = new Transform { name = "PMX_0000_センター", parent = f.Model, rotation = Quaternion.Yaw(100) };
        _ = new Transform { name = "PMX_0001_上半身", parent = f.Model, rotation = Quaternion.Yaw(110) };
        _ = new Transform { name = "PMX_0002_腰", parent = f.Model, rotation = Quaternion.Yaw(120) };
        var body = new Transform { name = "PMX_0003_下半身", parent = f.Model };
        f.Space("Force", "Heading");
        body.rotation = Quaternion.Yaw(90);
        f.Apply();
        Near(new Vector3(0, 0, -1), f.Bone.m_Force, "lower-body selected over controls");
    }

    private static void PmxNestedObject()
    {
        var f = new Fixture();
        f.UseItem();
        var nested = new Transform { name = "another Studio character", parent = f.Model };
        _ = new Transform { name = "hips", parent = nested, rotation = Quaternion.Yaw(120) };
        f.Controller._childObjects.Add(nested.gameObject);
        var body = new Transform { name = "腰", parent = f.Model };
        f.Space("Force", "Heading");
        body.rotation = Quaternion.Yaw(90);
        f.Apply();
        Near(new Vector3(0, 0, -1), f.Bone.m_Force, "own lower-ranked body selected");
    }

    private static void PmxDriverSkeleton()
    {
        var f = new Fixture();
        f.UseItem();
        var driver = new Transform { name = "VmdDummyBone", parent = f.Model };
        _ = new Transform { name = "下半身", parent = driver, rotation = Quaternion.Yaw(120) };
        var body = new Transform { name = "腰", parent = f.Model };
        f.Space("Force", "Heading");
        body.rotation = Quaternion.Yaw(90);
        f.Apply();
        Near(new Vector3(0, 0, -1), f.Bone.m_Force, "real skeleton selected");
    }

    private static void PmxSimulatedBones()
    {
        foreach (bool nested in new[] { false, true })
        {
            var f = new Fixture();
            f.UseItem();
            var simulated = nested ? new Transform { parent = f.Root } : f.Root;
            simulated.name = "PMX_0014_hips";
            simulated.rotation = Quaternion.Yaw(120);
            var body = new Transform { name = "PMX_0003_腰", parent = f.Model };
            f.Space("Force", "Heading");
            body.rotation = Quaternion.Yaw(90);
            simulated.rotation = Quaternion.Yaw(180);
            f.Apply();
            Near(new Vector3(0, 0, -1), f.Bone.m_Force, "animated waist chosen over simulated hips");
            simulated.rotation = Quaternion.Yaw(270);
            f.Apply();
            Near(new Vector3(0, 0, -1), f.Bone.m_Force, "physics cannot feed back into heading");
        }
    }

    private static void ExactBodyNames()
    {
        foreach (string name in new[] { "hips2", "left_hips", "hips_helper", "lowerbody_extra", "PMX__hips", "PMX_a1_hips", "PMX_12x_下半身", "PMX_123hips", "PMX_12_hips_helper" })
        {
            var f = new Fixture();
            f.UseItem();
            var candidate = new Transform { name = name, parent = f.Model };
            f.Space("Force", "Heading");
            candidate.rotation = Quaternion.Yaw(90);
            f.Apply();
            Equal(true, f.UsesObjectTransform, "unrecognized body name " + name);
            Near(Vector3.right, f.Bone.m_Force, "invalid candidate cannot steer force");
        }
    }

    private static void ItemResetReselectsSkeleton()
    {
        var f = new Fixture();
        f.UseItem();
        var original = new Transform { name = "waist", parent = f.Model };
        f.Space("Force", "Heading");
        var replacement = new Transform { name = "PMX_0003_下半身", parent = f.Model, rotation = Quaternion.Yaw(90) };
        original.rotation = Quaternion.Yaw(30);
        f.Apply();
        Near(Quaternion.Yaw(30) * Vector3.right, f.Bone.m_Force, "cached skeleton before reset");
        int previousScans = f.Controller.HierarchyScanCount;
        f.ResetHeading();
        f.Apply();
        Near(new Vector3(0, 0, -1), f.Bone.m_Force, "replacement selected after reset");
        Equal(previousScans + 1, f.Controller.HierarchyScanCount, "one new hierarchy scan");
        Equal(false, f.UsesObjectTransform, "replacement uses a body bone");
        replacement.rotation = Quaternion.Yaw(180);
        f.Apply();
        Near(new Vector3(-1, 0, 0), f.Bone.m_Force, "replacement stays live");
    }

    private static void ItemResetClearsHeading()
    {
        var f = new Fixture();
        f.UseItem();
        var original = new Transform { name = "waist", parent = f.Model };
        f.Space("Force", "Heading");
        original.rotation = Quaternion.Yaw(80);
        f.Apply();
        Near(Quaternion.Yaw(80) * Vector3.right, f.Bone.m_Force, "previous skeleton heading");
        _ = new Transform { name = "hips", parent = f.Model, rotation = Quaternion.Pitch(180) };
        f.Model.rotation = Quaternion.Yaw(25);
        f.ResetHeading();
        f.Apply();
        Near(Quaternion.Yaw(25) * Vector3.right, f.Bone.m_Force, "new inverted skeleton uses fresh object heading");
        f.Model.rotation = Quaternion.Yaw(35);
        f.Apply();
        Near(Quaternion.Yaw(25) * Vector3.right, f.Bone.m_Force, "new fallback remains stable while inverted");
    }

    private static void IndependentModes()
    {
        var f = new Fixture();
        f.Space("Gravity", "Bone");
        f.Attachment.rotation = Quaternion.Roll(90);
        f.Apply();
        Near(new Vector3(1, 0, 0), f.Bone.m_Force, "force stays absolute");
        f.Space("Force", "Heading");
        f.Space("Gravity", "World");
        f.Hips.rotation = Quaternion.Yaw(90);
        f.Attachment.rotation = Quaternion.identity;
        f.Apply();
        Near(new Vector3(2, 0, 0), f.Bone.m_Gravity, "gravity stays absolute");
        Near(new Vector3(0, 0, -1), f.Bone.m_Force, "force remains registered");
    }

    private static void RepeatedMode()
    {
        foreach (string vector in new[] { "Gravity", "Force" })
        foreach (string space in new[] { "Heading", "Bone" })
        {
            var f = new Fixture();
            f.Space(vector, space);
            var authored = new Vector3(0.1f, 0.2f, 0.3f);
            f.Set(vector, authored);
            f.Hips.rotation = Quaternion.Yaw(90);
            f.Attachment.rotation = Quaternion.Yaw(90);
            f.Space(vector, space);
            f.Apply();
            Near(authored, f.Authored(vector), "reselect preserves local vector");
            var expected = (space == "Heading" ? f.Hips.rotation : f.Attachment.rotation) * authored;
            Near(expected, vector == "Gravity" ? f.Bone.m_Gravity : f.Bone.m_Force, "reselect still follows");
        }
    }

    private static void NoDrift()
    {
        var f = new Fixture();
        var gravity = new Vector3(0.031f, -0.027f, 0.013f);
        var force = new Vector3(-0.019f, 0.007f, 0.003f);
        f.Space("Gravity", "Heading");
        f.Space("Force", "Bone");
        f.Set("Gravity", gravity);
        f.Set("Force", force);
        for (int i = 0; i <= 1440; i++)
        {
            f.Hips.rotation = Quaternion.Yaw(i * 0.5f);
            f.Attachment.rotation = Quaternion.Yaw(i * 0.5f);
            f.Apply();
            Near(f.Hips.rotation * gravity, f.Bone.m_Gravity, "rotated gravity");
            Near(f.Attachment.rotation * force, f.Bone.m_Force, "rotated force");
            Near(gravity.magnitude, f.Bone.m_Gravity.magnitude, "gravity magnitude");
            Near(force.magnitude, f.Bone.m_Force.magnitude, "force magnitude");
            Near(gravity, f.Authored("Gravity"), "gravity authored value");
            Near(force, f.Authored("Force"), "force authored value");
        }
        Near(gravity, f.Bone.m_Gravity, "full turns gravity");
        Near(force, f.Bone.m_Force, "full turns force");
    }

    private static void ScaleAndSimulatedRoot()
    {
        var f = new Fixture();
        f.Space("Force", "Bone");
        f.Model.localScale = new Vector3(10, -0.5f, 3);
        f.Attachment.localScale = new Vector3(-4, 2, 0.01f);
        f.Root.rotation = Quaternion.Yaw(90);
        f.Bone.transform.rotation = Quaternion.Roll(90);
        f.Apply();
        Near(new Vector3(1, 0, 0), f.Bone.m_Force, "simulation does not rotate attachment force");
        f.Attachment.rotation = Quaternion.Yaw(90);
        f.Apply();
        Near(new Vector3(0, 0, -1), f.Bone.m_Force, "attachment rotates without scale");
        Near(1, f.Bone.m_Force.magnitude, "scale independent magnitude");
    }

    private static void FallbackFrame()
    {
        foreach (bool absentRoot in new[] { false, true })
        {
            var f = new Fixture();
            if (absentRoot) f.Bone.m_Root = null;
            else f.Root.parent = null;
            f.Space("Force", "Bone");
            f.Bone.transform.rotation = Quaternion.Yaw(90);
            f.Apply();
            Near(new Vector3(0, 0, -1), f.Bone.m_Force, "component fallback");
        }
    }

    private static void AuthoredSetters()
    {
        var f = new Fixture();
        f.Hips.rotation = Quaternion.Yaw(90);
        f.Attachment.rotation = Quaternion.Yaw(90);
        f.Space("Gravity", "Heading");
        f.Space("Force", "Bone");
        f.Set("Gravity", new Vector3(1, 0, 0));
        f.Set("Force", new Vector3(0, 0, 2));
        Near(new Vector3(0, 0, -1), f.Bone.m_Gravity, "gravity set in heading space");
        Near(new Vector3(2, 0, 0), f.Bone.m_Force, "force set in bone space");
        f.Apply();
        Near(new Vector3(1, 0, 0), f.Authored("Gravity"), "gravity getter");
        Near(new Vector3(0, 0, 2), f.Authored("Force"), "force getter");
    }

    private static void ZeroVectors()
    {
        var f = new Fixture();
        f.Set("Gravity", Vector3.zero);
        f.Set("Force", Vector3.zero);
        f.Model.rotation = Quaternion.Roll(45);
        f.Attachment.rotation = Quaternion.Yaw(37);
        foreach (string space in new[] { "Heading", "Bone", "World" })
        {
            f.Space("Gravity", space);
            f.Space("Force", space);
            f.Apply();
            Near(Vector3.zero, f.Bone.m_Gravity, "zero gravity");
            Near(Vector3.zero, f.Bone.m_Force, "zero force");
        }
    }

    private static void SaveLocalAndWorld()
    {
        var f = new Fixture();
        f.Space("Gravity", "Heading");
        f.Set("Gravity", new Vector3(1, -2, 0));
        f.Space("Force", "Bone");
        f.Hips.rotation = Quaternion.Yaw(90);
        f.Attachment.rotation = Quaternion.Yaw(90);
        var xml = f.Save(); // Save must update world values even before the next physics tick.
        Equal("Heading", xml.GetAttribute("gravitySpace"), "saved gravity space");
        Equal("Bone", xml.GetAttribute("forceSpace"), "saved force space");
        Near(new Vector3(1, -2, 0), ReadVector(xml, "gravityLocal"), "saved local gravity");
        Near(new Vector3(1, 0, 0), ReadVector(xml, "forceLocal"), "saved local force");
        Near(new Vector3(0, -2, -1), ReadVector(xml, "gravity"), "legacy gravity snapshot");
        Near(new Vector3(0, 0, -1), ReadVector(xml, "force"), "legacy force snapshot");
    }

    private static void RoundtripAtDifferentPose()
    {
        var source = new Fixture();
        source.Space("Gravity", "Heading");
        source.Set("Gravity", new Vector3(1, -2, 0));
        source.Space("Force", "Bone");
        source.Hips.rotation = Quaternion.Yaw(45);
        source.Attachment.rotation = Quaternion.Yaw(45);
        var destination = new Fixture();
        destination.Hips.rotation = Quaternion.Yaw(90);
        destination.Attachment.rotation = Quaternion.Yaw(90);
        destination.Load(source.Save().OuterXml);
        Equal("Heading", destination.Space("Gravity"), "restored gravity space");
        Equal("Bone", destination.Space("Force"), "restored force space");
        Near(new Vector3(0, -2, -1), destination.Bone.m_Gravity, "loaded at current heading pose");
        Near(new Vector3(0, 0, -1), destination.Bone.m_Force, "loaded at current bone pose");
        destination.Hips.rotation = Quaternion.Yaw(180);
        destination.Attachment.rotation = Quaternion.Yaw(180);
        destination.Apply();
        Near(new Vector3(-1, -2, 0), destination.Bone.m_Gravity, "loaded gravity registered");
        Near(new Vector3(-1, 0, 0), destination.Bone.m_Force, "loaded force registered");
    }

    private static void LegacyXml()
    {
        var f = new Fixture();
        f.Load("<dynamicBone gravityX='1' gravityY='2' gravityZ='3' forceX='4' forceY='5' forceZ='6' />");
        f.Model.rotation = Quaternion.Roll(90);
        f.Attachment.rotation = Quaternion.Yaw(90);
        f.Apply();
        Equal("World", f.Space("Gravity"), "legacy gravity space");
        Equal("World", f.Space("Force"), "legacy force space");
        Near(new Vector3(1, 2, 3), f.Bone.m_Gravity, "legacy gravity unchanged");
        Near(new Vector3(4, 5, 6), f.Bone.m_Force, "legacy force unchanged");
        Equal(false, f.Save().HasAttribute("gravitySpace"), "no relative metadata for legacy scene");
    }

    private static void IncompleteLocalXml()
    {
        var f = new Fixture();
        f.Load("<dynamicBone gravityX='1' gravityY='2' gravityZ='3' gravitySpace='Heading' gravityLocalX='99' gravityLocalY='98' forceX='4' forceY='5' forceZ='6' forceSpace='Bone' />");
        f.Model.rotation = Quaternion.Roll(90);
        f.Attachment.rotation = Quaternion.Yaw(90);
        f.Apply();
        Equal("World", f.Space("Gravity"), "incomplete gravity uses world");
        Equal("World", f.Space("Force"), "incomplete force uses world");
        Near(new Vector3(1, 2, 3), f.Bone.m_Gravity, "fallback gravity");
        Near(new Vector3(4, 5, 6), f.Bone.m_Force, "fallback force");
    }

    private static void ResetOne()
    {
        var f = new Fixture();
        f.Space("Gravity", "Heading");
        f.Space("Force", "Bone");
        f.Model.rotation = Quaternion.Roll(90);
        f.Attachment.rotation = Quaternion.Yaw(90);
        f.Call("ResetGravity", f.Bone);
        f.Apply();
        Equal("World", f.Space("Gravity"), "reset gravity space");
        Equal("Bone", f.Space("Force"), "force still relative");
        Near(new Vector3(0, -2, 0), f.Bone.m_Gravity, "original gravity restored");
        Near(new Vector3(0, 0, -1), f.Bone.m_Force, "other vector still follows");
        f.Call("ResetForce", f.Bone);
        f.Attachment.rotation = Quaternion.Yaw(180);
        f.Apply();
        Near(new Vector3(1, 0, 0), f.Bone.m_Force, "original force restored");
    }

    private static void UnknownSpaceXml()
    {
        foreach (string space in new[] { "World", "UnknownFutureMode", "heading", "2", "" })
        {
            var f = new Fixture();
            f.Load($"<dynamicBone gravityX='1' gravityY='2' gravityZ='3' gravitySpace='{space}' gravityLocalX='99' gravityLocalY='98' gravityLocalZ='97' forceX='4' forceY='5' forceZ='6' forceSpace='{space}' forceLocalX='96' forceLocalY='95' forceLocalZ='94' />");
            f.Model.rotation = Quaternion.Roll(90);
            f.Attachment.rotation = Quaternion.Yaw(90);
            f.Apply();
            Equal("World", f.Space("Gravity"), "unknown gravity space");
            Equal("World", f.Space("Force"), "unknown force space");
            Near(new Vector3(1, 2, 3), f.Bone.m_Gravity, "unknown mode gravity snapshot");
            Near(new Vector3(4, 5, 6), f.Bone.m_Force, "unknown mode force snapshot");
        }
    }

    private static void ResetEverything()
    {
        var f = new Fixture();
        f.Space("Gravity", "Heading");
        f.Space("Force", "Bone");
        f.Set("Gravity", new Vector3(10, 11, 12));
        f.Set("Force", new Vector3(13, 14, 15));
        var dirty = (IDictionary)GetField(f.Editor, "_dirtyDynamicBones");
        var pending = (IDictionary)GetField(f.Editor, "_headlessDirtyDynamicBones");
        pending.Add(f.Root, dirty[f.Bone]);
        f.Call("ResetAll");
        f.Model.rotation = Quaternion.Roll(90);
        f.Attachment.rotation = Quaternion.Yaw(90);
        f.Apply();
        Near(new Vector3(0, -2, 0), f.Bone.m_Gravity, "all reset gravity");
        Near(new Vector3(1, 0, 0), f.Bone.m_Force, "all reset force");
        Equal(0, ((IDictionary)GetField(f.Editor, "_dirtyDynamicBones")).Count, "dirty data removed");
        Equal(0, pending.Count, "pending reconstruction data removed");
        Equal(false, Registry().Contains(f.Bone), "runtime registry removed");
    }

    private static void DataCopy()
    {
        var f = new Fixture();
        f.Space("Gravity", "Heading");
        f.Space("Force", "Bone");
        f.Set("Gravity", new Vector3(1, 2, 3));
        f.Set("Force", new Vector3(4, 5, 6));
        object source = ((IDictionary)GetField(f.Editor, "_dirtyDynamicBones"))[f.Bone];
        object copy = Activator.CreateInstance(source.GetType(), Instance, null, [source], null);
        foreach (string field in new[] { "gravitySpace", "forceSpace", "currentGravity", "currentForce", "originalGravity", "originalForce" })
            Equal(source.GetType().GetField(field).GetValue(source), source.GetType().GetField(field).GetValue(copy), "copied " + field);
        f.Set("Force", new Vector3(7, 8, 9));
        Near(new Vector3(4, 5, 6), (Vector3)source.GetType().GetField("currentForce").GetValue(copy), "copy not aliased");
    }

    private static void LoadFromUsesDestination()
    {
        var source = new Fixture();
        source.Space("Gravity", "Heading");
        source.Set("Gravity", new Vector3(1, -2, 0));
        source.Space("Force", "Bone");
        source.Hips.rotation = Quaternion.Yaw(45);
        source.Attachment.rotation = Quaternion.Yaw(45);
        source.Apply();
        var destination = new Fixture();
        destination.Hips.rotation = Quaternion.Yaw(90);
        destination.Attachment.rotation = Quaternion.Yaw(90);
        destination.Call("LoadFrom", source.Editor);
        Equal("Heading", destination.Space("Gravity"), "copied heading space");
        Equal("Bone", destination.Space("Force"), "copied bone space");
        Near(new Vector3(0, -2, -1), destination.Bone.m_Gravity, "destination heading rotation");
        Near(new Vector3(0, 0, -1), destination.Bone.m_Force, "destination bone rotation");
        destination.Attachment.rotation = Quaternion.Yaw(180);
        destination.Apply();
        Near(new Vector3(-1, 0, 0), destination.Bone.m_Force, "copied registration");
    }

    private static void ParticleRebuild()
    {
        var f = new Fixture();
        f.Space("Gravity", "Heading");
        f.Set("Gravity", new Vector3(1, 0, 0));
        f.Hips.rotation = Quaternion.Yaw(90);
        f.Call("UpdateDynamicBone", f.Bone);
        Equal(1, f.Bone.SetupCount, "particle rebuild");
        Near(new Vector3(0, -2, 0), f.Bone.GravityAtSetup, "original gravity at setup");
        Near(new Vector3(0, 0, -1), f.Bone.m_Gravity, "relative gravity restored after setup");
    }

    private static IDictionary Registry() => (IDictionary)editorType.GetField("_relativeDirectionOwners", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

    private static void OwnerCleanup()
    {
        var f = new Fixture();
        f.Space("Gravity", "Heading");
        Equal(true, Registry().Contains(f.Bone), "registered owner");
        ((AdvancedModeModule)f.Editor)._parent = null;
        f.Apply();
        Equal(false, Registry().Contains(f.Bone), "missing owner unregistered");
    }
}
