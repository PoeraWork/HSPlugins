# Dynamic-bone directions

In **Advanced Mode > Dynamic Bones**, gravity and force each have two mutually
exclusive options:

| Follow horizontal rotation | Follow body movement | Direction |
| --- | --- | --- |
| Off | Off | Fixed in world space, preserving the default behavior. |
| On | Off | Follows the animated body's horizontal heading, including MMD turns with a fixed Studio object transform. |
| Off | On | Follows the attachment bone's full rotation, including pitch and roll. |

Enabling one option disables the other. Gravity and force can use different
modes. Switching modes preserves the current world direction and vector length.
With following enabled, the XYZ controls and Timeline's **DB Gravity / DB Force**
tracks edit values in the selected reference frame. Existing keyframes should be
reviewed when changing modes.

Horizontal heading uses the animated pelvis of native characters. Imported PMX
items are matched by body-bone names such as `下半身`, `hips`, `pelvis`, `腰` and
`上半身`, including AssetImport's `PMX_<number>_<name>` prefixes. Nested Studio
objects, MMD driver skeletons and simulated dynamic-bone subtrees are excluded
from this lookup. If no body bone is recognized, the panel reports that it is
using the object's heading; **Force refresh list** retries the lookup after a
skeleton change.

Imported-model lookup relies on these naming conventions. Custom or renamed
bones may not be recognized, and a matching name does not guarantee that a bone
controls the body's heading. This lookup does not provide automatic support
for arbitrary skeletons.

Heading extracts rotation about world up, so a bend past 90 degrees does not
reverse the direction. At a fully inverted pose, heading is ambiguous: the last
valid heading is retained, or the Studio object's heading is used for the first
sample. Full body movement instead uses the dynamic-bone root's parent, falling
back to the component transform if that parent is unavailable.

Modes and authored vectors persist through scene save/load, copy/paste, object
duplication and dynamic-bone reconstruction. Resetting a vector restores its
original value and world-space mode. Resetting a bone or all bones clears their
following options as well. Following continues when the editor window is closed.
These controls apply to the Dynamic Bones panel, not the separate breast editor.

## Scene compatibility

Scenes without direction metadata keep their existing world-space behavior.
Saves retain the original world-space XYZ attributes for older plugin versions,
alongside the direction mode and local XYZ values. Older versions can read the
world-space snapshot but cannot follow the animation or preserve the new metadata
when saving again.

## Validation

The [regression runner](../tests/RelativeForces/README.md) tests the production
direction and persistence methods with small Unity/Studio doubles. It does not
replace an in-game check of animation timing or interactions with other plugins.

To check in Studio, test a native character and an imported PMX item with a fixed
object transform while an MMD animation turns the body. Compare world, horizontal
and full-body modes, then test bends past 90 degrees, copy/paste, resets,
duplication and scene reload.
