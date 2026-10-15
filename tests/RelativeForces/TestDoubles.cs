using System.Xml;

namespace UnityEngine
{
    public static class GUILayout
    {
        public static int ClickIndex = -1;
        public static int CallIndex;
        public static bool Toggle(bool value, string label) => CallIndex++ == ClickIndex ? !value : value;
    }

    // Only the mathematical operations used by the selected production methods.
    public struct Vector3(float x, float y, float z)
    {
        public float x = x, y = y, z = z;
        public float magnitude => MathF.Sqrt(x * x + y * y + z * z);
        public Vector3 normalized => magnitude > 0.000001f ? this / magnitude : zero;
        public static Vector3 zero => new(0, 0, 0);
        public static Vector3 one => new(1, 1, 1);
        public static Vector3 right => new(1, 0, 0);
        public static Vector3 forward => new(0, 0, 1);
        public static Vector3 operator /(Vector3 a, float divisor) => new(a.x / divisor, a.y / divisor, a.z / divisor);
        public override string ToString() => $"({x:G6}, {y:G6}, {z:G6})";
    }

    public struct Vector2
    {
        public float x, y;
    }

    public struct Quaternion
    {
        private System.Numerics.Quaternion value;
        public float x { get => value.X; set => this.value.X = value; }
        public float y { get => value.Y; set => this.value.Y = value; }
        public float z { get => value.Z; set => this.value.Z = value; }
        public float w { get => value.W; set => this.value.W = value; }
        public Quaternion(float x, float y, float z, float w) => value = new System.Numerics.Quaternion(x, y, z, w);
        public static Quaternion identity => new() { value = System.Numerics.Quaternion.Identity };
        public static Quaternion Inverse(Quaternion q) => new() { value = System.Numerics.Quaternion.Inverse(q.value) };
        public static Quaternion Yaw(float degrees) => new()
        {
            value = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, degrees * MathF.PI / 180)
        };
        public static Quaternion Roll(float degrees) => new()
        {
            value = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, degrees * MathF.PI / 180)
        };
        public static Quaternion Pitch(float degrees) => new()
        {
            value = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitX, degrees * MathF.PI / 180)
        };
        public static Quaternion Euler(float x, float y, float z) => Yaw(y) * Pitch(x) * Roll(z);
        public static Quaternion AngleAxis(float degrees, Vector3 axis) => new()
        {
            value = System.Numerics.Quaternion.CreateFromAxisAngle(new System.Numerics.Vector3(axis.normalized.x, axis.normalized.y, axis.normalized.z), degrees * MathF.PI / 180)
        };
        public static Quaternion operator *(Quaternion a, Quaternion b) => new() { value = a.value * b.value };
        public static Vector3 operator *(Quaternion q, Vector3 v)
        {
            var result = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(v.x, v.y, v.z), q.value);
            return new Vector3(result.X, result.Y, result.Z);
        }
    }

    public class Transform
    {
        public string name = "bone";
        private Transform currentParent;
        private readonly List<Transform> children = new();
        public Transform parent
        {
            get => currentParent;
            set
            {
                currentParent?.children.Remove(this);
                currentParent = value;
                currentParent?.children.Add(this);
            }
        }
        public int childCount => children.Count;
        public Transform GetChild(int index) => children[index];
        public bool IsChildOf(Transform ancestor)
        {
            for (Transform current = this; current != null; current = current.parent)
                if (current == ancestor) return true;
            return false;
        }
        private GameObject owner;
        public GameObject gameObject => owner ??= new GameObject(this);
        private Quaternion currentRotation = Quaternion.identity;
        public int RotationReadCount;
        public Quaternion rotation
        {
            get { RotationReadCount++; return currentRotation; }
            set => currentRotation = value;
        }
        public Vector3 localScale = Vector3.one;
    }

    public class GameObject(Transform transform)
    {
        public Transform transform = transform;
    }

    public static class Mathf
    {
        public static float Sqrt(float value) => MathF.Sqrt(value);
    }
}

public class DynamicBone
{
    public enum FreezeAxis { None, X, Y, Z }
    public string name = "dynamicBone";
    public bool enabled = true;
    public UnityEngine.Transform transform = new();
    public UnityEngine.Transform m_Root;
    public UnityEngine.Vector3 m_Gravity, m_Force;
    public FreezeAxis m_FreezeAxis;
    public float m_Damping, m_Elasticity, m_Stiffness, m_Inert, m_Radius;
    private float weight = 1;
    public float GetWeight() => weight;
    public void SetWeight(float value) => weight = value;
    public int SetupCount;
    public UnityEngine.Vector3 GravityAtSetup;
    public void InitTransforms() { }
    public void SetupParticles() { SetupCount++; GravityAtSetup = m_Gravity; }
}

namespace HSPE
{
    public class PoseController
    {
        public UnityEngine.Transform transform = new() { name = "character" };
        public readonly HashSet<UnityEngine.GameObject> _childObjects = new();
        public int HierarchyScanCount;
        public T[] GetComponentsInChildren<T>(bool includeInactive)
        {
            HierarchyScanCount++;
            var result = new List<T>();
            void Walk(UnityEngine.Transform current)
            {
                if (current is T component) result.Add(component);
                for (int i = 0; i < current.childCount; i++) Walk(current.GetChild(i));
            }
            Walk(transform);
            return result.ToArray();
        }
    }

    public class MainWindow
    {
        public static readonly MainWindow _self = new();
        public void ExecuteDelayed(Action action, int frames) => action();
    }
}

namespace HSPE.AMModules
{
    public class GenericOCITarget
    {
        public enum Type { Character, Item }
        public Type type = Type.Character;
        public Studio.OCIChar ociChar = new();
    }

    public class AdvancedModeModule
    {
        public HSPE.PoseController _parent = new();
        public virtual int SaveXml(XmlTextWriter writer) => 0;
    }
}

namespace Studio
{
    public class OCIChar
    {
        public FinalIK finalIK = new();
    }

    public class FinalIK
    {
        public IKReferences references = new();
    }

    public class IKReferences
    {
        public UnityEngine.Transform pelvis = new() { name = "hips" };
    }
}

namespace ToolBox.Extensions
{
    public static class TransformExtensions
    {
        public static string GetPathFrom(this UnityEngine.Transform transform, UnityEngine.Transform root)
        {
            var names = new List<string>();
            while (transform != null && transform != root)
            {
                names.Add(transform.name);
                transform = transform.parent;
            }
            names.Reverse();
            return string.Join("/", names);
        }
    }
}
