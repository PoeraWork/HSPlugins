using System;
using System.Collections.Generic;
using UnityEngine;

namespace HSPE.AMModules
{
    // Resolves the animated body independently of the Studio object's transform.
    // Cache the reference bone, but always read its current rotation at the physics step.
    internal sealed class BodyHeadingReference
    {
        // Prefer the lower body over upper-body and whole-model motion controls.
        private static readonly string[][] _boneAliasesByPriority =
        {
            new[] { "cf_j_hips", "cm_j_hips", "下半身", "hips", "pelvis", "lower body", "lower_body", "lowerbody" },
            new[] { "腰", "体の重心", "waist" },
            new[] { "上半身", "upper body", "upper_body", "spine" },
            new[] { "グルーブ", "groove" },
            new[] { "センター", "center" },
            new[] { "全ての親", "全亲", "root", "master" }
        };
        private static readonly Dictionary<string, int> _bonePriorities = CreateBonePriorities();

        private readonly PoseController _owner;
        private readonly GenericOCITarget _target;
        private readonly IList<DynamicBone> _dynamicBones;
        private Transform _cachedBone;
        private Quaternion _lastRotation = Quaternion.identity;
        private bool _hasRotation;

        public BodyHeadingReference(PoseController owner, GenericOCITarget target, IList<DynamicBone> dynamicBones)
        {
            _owner = owner;
            _target = target;
            _dynamicBones = dynamicBones;
        }

        public Quaternion Rotation
        {
            get
            {
                Quaternion fallback = _hasRotation ? _lastRotation :
                    ExtractHorizontalRotation(_owner.transform.rotation, Quaternion.identity);
                _lastRotation = ExtractHorizontalRotation(GetBone().rotation, fallback);
                _hasRotation = true;
                return _lastRotation;
            }
        }

        public bool UsesObjectTransform { get { return GetBone() == _owner.transform; } }

        public void Reset()
        {
            _cachedBone = null;
            _hasRotation = false;
        }

        private Transform GetBone()
        {
            // FinalIK exposes the native character's animated pelvis, including MMD turns.
            if (_target != null && _target.type == GenericOCITarget.Type.Character &&
                _target.ociChar != null && _target.ociChar.finalIK != null &&
                _target.ociChar.finalIK.references != null)
            {
                Transform pelvis = _target.ociChar.finalIK.references.pelvis;
                if (pelvis != null && IsOwnTransform(pelvis))
                    return pelvis;
            }

            if (_target == null || _target.type != GenericOCITarget.Type.Item)
                return _owner.transform;

            if (_cachedBone != null && IsOwnTransform(_cachedBone) && !IsMotionControl(_cachedBone))
                return _cachedBone;

            _cachedBone = FindBodyBone();
            return _cachedBone;
        }

        private Transform FindBodyBone()
        {
            Transform result = _owner.transform;
            int bestPriority = int.MaxValue;
            foreach (Transform candidate in _owner.GetComponentsInChildren<Transform>(true))
            {
                int priority;
                if (!_bonePriorities.TryGetValue(NormalizeBoneName(candidate.name), out priority) ||
                    priority >= bestPriority || !IsOwnTransform(candidate) || IsMotionControl(candidate) || IsSimulated(candidate))
                    continue;

                bestPriority = priority;
                result = candidate;
            }
            return result;
        }

        private bool IsOwnTransform(Transform candidate)
        {
            if (!candidate.IsChildOf(_owner.transform))
                return false;

            foreach (GameObject child in _owner._childObjects)
                if (child != null && candidate.IsChildOf(child.transform))
                    return false;
            return true;
        }

        private bool IsMotionControl(Transform candidate)
        {
            // Imported models can contain MMDD controls with the same body names.
            for (Transform ancestor = candidate; ancestor != _owner.transform; ancestor = ancestor.parent)
                if (ancestor.name == "VmdDummyBone")
                    return true;
            return false;
        }

        private bool IsSimulated(Transform candidate)
        {
            foreach (DynamicBone bone in _dynamicBones)
                if (bone != null && bone.m_Root != null && candidate.IsChildOf(bone.m_Root))
                    return true;
            return false;
        }

        private static Dictionary<string, int> CreateBonePriorities()
        {
            var priorities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int priority = 0; priority < _boneAliasesByPriority.Length; priority++)
                foreach (string alias in _boneAliasesByPriority[priority])
                    priorities.Add(alias, priority);
            return priorities;
        }

        private static string NormalizeBoneName(string name)
        {
            // AssetImport adds PMX_<number>_ before the original bone name.
            // Strip only that prefix; partial name matches can pick unrelated bones.
            if (name.StartsWith("PMX_", StringComparison.OrdinalIgnoreCase))
            {
                int separator = name.IndexOf('_', 4);
                if (separator > 4)
                {
                    for (int i = 4; i < separator; i++)
                        if (!char.IsDigit(name[i]))
                            return name.Trim();
                    name = name.Substring(separator + 1);
                }
            }
            return name.Trim();
        }

        private static Quaternion ExtractHorizontalRotation(Quaternion rotation, Quaternion fallback)
        {
            // Extract twist about world-up. Projecting forward onto the ground
            // would flip the force by 180 degrees when a bend crosses vertical.
            float lengthSquared = rotation.y * rotation.y + rotation.w * rotation.w;
            if (lengthSquared < 0.00000001f)
                return fallback; // A fully inverted swing has no unique heading.
            float inverseLength = 1f / Mathf.Sqrt(lengthSquared);
            return new Quaternion(0f, rotation.y * inverseLength, 0f, rotation.w * inverseLength);
        }
    }
}
