# Dynamic-bone direction regression tests

Install the .NET 8 SDK, then run from the repository root:

```powershell
cd tests/RelativeForces
dotnet run -c Release
```

The runner requires no NuGet packages or installed game. Roslyn is referenced
from the SDK. `tests/global.json` selects SDK 8 even when newer SDKs are installed.
The nested `tests/Directory.Build.props` isolates the test project
from the plugin projects' proprietary game references and release packaging.

## What is tested

The runner parses the current `PoseEditor.Core/AMModules/DynamicBonesEditor.cs`
using Roslyn, selects an explicit set of complete production members, and
compiles those unchanged bodies with the complete production
`BodyHeadingReference` class and the real `EditableValue<T>`. Small test
doubles provide transforms, quaternion/vector operations, dynamic-bone fields,
checkbox clicks, and immediate execution of the delayed-copy callback. Renamed
or removed required production members fail compilation rather than silently
testing a stale copy. No source-text assertions replace behavior checks.

The cases exercise:

- The two independent, mutually exclusive direction checkboxes, including both off.
- World, animated horizontal heading, and attachment-bone frames.
- A fixed Studio root with a turning native pelvis, reproducing the MMD regression.
- Native pelvis references that work independently of bone names, apply replacement
  references immediately, and fall back to the object transform when invalid.
- Object heading for items regardless of child bone names, and reset of
  inverted-pose fallback history.
- Sharing one resolved heading between gravity and force in a physics update,
  verified by a transform-read counter.
- Horizontal twist extraction while bending past 90 degrees, side tilts, turn
  wraparound and fully inverted poses; attachment mode still uses all three axes.
- Preserving current world direction when switching modes, repeated mode selection,
  authored-vector editing, zero vectors, magnitude preservation, and drift.
- Gravity and force with different modes; scale and simulated-root independence.
- Missing root/parent fallback to the dynamic-bone component's transform.
- XML world snapshots for older KKPE, heading vectors and modes for new scenes,
  reload under another pose, and legacy/incomplete/unknown metadata fallback.
- Per-vector/all reset, pending reconstruction cleanup, copied data,
  duplication through `LoadFrom`, particle rebuild, and owner cleanup.

## Boundary

These are isolated behavior tests of production method bodies, not a full Unity
or game integration test. The transform doubles expose world rotations directly
and use `System.Numerics` for quaternion arithmetic. They do not simulate Unity's
destroyed-object null semantics or the physics solver. GUI drawing/layout,
scene callbacks, skeleton reconstruction, drag interaction, and Harmony hook
installation still require a plugin build and an in-game smoke test. The data
copy test covers the shared copy constructor; it does not click the complete
copy/paste panel. `LoadFrom` runs its delayed callback immediately.

A manual smoke check should keep a native character's Studio object fixed while
animation turns the body, bend the body past 90 degrees, switch each checkbox
on/off, change gravity/force values, copy/paste settings, reset, then save and
reload a scene. Also check that an item's heading follows its object transform.
Confirm the two modes follow their respective frames and that the flags survive
character duplication and clothes changes.
